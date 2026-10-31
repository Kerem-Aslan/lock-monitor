# Lock Monitor

A Windows 11 system-monitor widget for the **Widgets board** (Win+W) and the **lock screen**.
It runs as a packaged (MSIX) out-of-process COM widget provider built with C#/.NET 10 and the
Windows App SDK, and needs no administrator rights or kernel driver.

> Status: **phase 2 — live CPU, RAM and GPU usage** on the small card (used by the lock screen).
> Medium/large cards, network, disk, VRAM and GPU temperature follow in phase 3.

## Requirements

| Tool | Version used | Notes |
|---|---|---|
| Windows 11 | 22H2 (22621) or later | Lock-screen widgets need a recent build with the lock-screen widgets feature |
| Developer Mode | On | Settings > System > For developers. Needed to register a loose (unsigned) package |
| .NET SDK | 10.0.x (LTS) | `dotnet --list-sdks` |
| Windows SDK | 10.0.26100 | Installed with Visual Studio |
| Visual Studio 2026 (optional) | 18.x with *.NET desktop development* + *WinUI application development* | Only needed for F5 debugging; the scripts work without it |
| MSVC linker (only for `-Aot`) | VS *Desktop development with C++* workload | Native AOT links with `link.exe` |
| NuGet packages | Microsoft.WindowsAppSDK.Widgets + .Runtime 2.6.4 | Component packages instead of the metapackage, which would add ~40 MB of ONNX Runtime/DirectML/WebView2. Pinned in `Directory.Packages.props` |
| Windows App Runtime | 2.6.x | Installed by the Store as a dependency; present on most machines |

## Build, test and deploy (development)

```powershell
# from the repository root, in PowerShell 7 (pwsh)
dotnet test --project tests\LockMonitor.Core.Tests                 # unit tests (xUnit v3, Microsoft.Testing.Platform)
dotnet run --project tools\LockMonitor.Probe -- --seconds 20       # read the sensors without the widget host

pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1          # Debug | x64
pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1 -Aot     # Release, Native AOT (smallest footprint)
pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1 -Clean   # unregister first (removes pinned widgets)

pwsh -File scripts\measure-footprint.ps1 -Seconds 60              # CPU/memory of provider + widget host
```

The deploy script builds the project, mirrors the output into `artifacts\dev-layout` and registers
it with `Add-AppxPackage -Register`. Registering from a separate folder keeps the build output free
of file locks the widget host holds on `resources.pri`. Each deploy stamps a time-based
build/revision number into the *layout* manifest, because Windows refuses to re-register a changed
manifest with the same version in developer mode. Pinned widgets survive a redeploy.

> If a build ever fails with `PRI210 ... 0x800704c8` (file mapped by another process), rename the
> stale `resources.pri` in the `bin\...` folder and build again.

## Add the widget

**Widgets board:** press **Win+W** → **+** (Add widgets) → **Lock Monitor** → **System Monitor** → **Pin**.

**Lock screen:** **Settings → Personalization → Lock screen → Widgets → Add widget** → **System Monitor**.
Only widgets that support the *small* size can be added there. Lock with **Win+L** to check.
Organisations can disable lock-screen widgets with the *Disable Widgets On Lock Screen* group policy.

## How it works

| Metric | Source | Notes |
|---|---|---|
| CPU % | PDH `\Processor Information(_Total)\% Processor Utility` | Same counter as Task Manager; clamped to 100 % |
| RAM % | `GlobalMemoryStatusEx` | (total − available) / total |
| GPU % | PDH `\GPU Engine(*)\Utilization Percentage` | Summed per adapter and engine type, busiest engine wins (Task Manager's method). Adapter = hardware adapter with the most VRAM (DXGI) |

- **Sampling only while visible.** The host calls `Activate` when the board opens or the lock screen
  shows the widget and `Deactivate` when it hides it. One background loop samples every 2 s while
  at least one widget is active. With no active widget the loop waits without a timer and every
  sensor (PDH query) is closed: zero polling.
- **Lock screen:** observed behaviour (not documented by Microsoft): the lock screen activates the
  widget while it is shown and deactivates it on unlock, exactly like the board, and the values
  update live.
- **Isolation:** each sensor runs inside a `SensorRunner`. An exception turns only that sensor's
  values into `N/A`; after 3 failures it is closed and retried with exponential back-off.
- **Fewer host updates:** card data identical to the previous update is not sent again.

### Measured footprint

Provider and widget host processes, Native AOT build, RTX 4060 Laptop / Ryzen 7 7435HS:

| Situation | Provider CPU (one core) | Provider private memory | WidgetService CPU | WidgetBoard CPU |
|---|---|---|---|---|
| Board open, 1 small widget, 2 s interval (60 s) | **0.18 %** | 11.7 MB | 1.15 % | 0.62 % |
| No active widget (30 s) | **0.00 %** (0 ms) | 10.3 MB | 0.00 % | 0.00 % |

Sensor cost per tick (probe): GPU 2.4 ms, CPU 0.3 ms, RAM 0.02 ms.

| Build | Package size | Idle private memory before first sample | Working set |
|---|---|---|---|
| Debug, JIT, self-contained | 103 MB | 9.3 MB | 38.9 MB |
| Release, Native AOT | 3.9 MB | 4.9 MB | 16.0 MB |

## Troubleshooting

- Provider log (lifecycle events and sensor errors only, never per sample):
  `%LOCALAPPDATA%\Packages\LockMonitor.Dev_w3qjt9ra1beny\LocalCache\Local\LockMonitor\provider.log`.
  Each `Deactivate` line says how long the widget was active and how many updates it received.
- The provider process is `LockMonitor.Widget.exe`. The widget host starts it on demand through COM
  (`-RegisterProcessAsComServer`) and it exits 30 s after the last widget is removed.
- Launching the app from Start only shows a short "how to add the widget" message.
- Running `LockMonitor.Widget.exe -RegisterProcessAsComServer` directly (without package identity)
  fails with `REGDB_E_CLASSNOTREG`; the widget APIs require the packaged context.
- *Widgets board says the widget could not be added (Native AOT build only):* the provider must carry
  `[WinRT.GeneratedWinRTExposedType]`, otherwise CsWinRT generates no WinRT vtables outside WinUI/UWP
  projects and the host gets `E_NOINTERFACE` for `IWidgetProvider` (logged by the class factory).

## Project layout

```
src/LockMonitor.Core/            sensors, sampling and card rendering (no Windows App SDK dependency)
  Native/                        PDH, DXGI and kernel32 interop (source-generated, AOT safe)
  Sensors/                       one class per metric + SensorRunner isolation
  Sampling/                      SamplingScheduler (single loop) and SamplingPlan
  Rendering/                     template composition (TemplateStore) and card data (CardData)
src/LockMonitor.Widget/          packaged widget provider (MSIX)
  Program.cs                     entry point, COM class registration
  Com/ClassFactory.cs            source-generated IClassFactory
  Provider/                      IWidgetProvider, per-widget sessions, process lifetime
  Templates/small.json           Adaptive Card template for the small size (lock screen)
  Package.appxmanifest           COM server + com.microsoft.windows.widgets extension
tools/LockMonitor.Probe/         console tool: reads the sensors and reports their cost
tests/LockMonitor.Core.Tests/    unit tests
scripts/                         deploy-dev.ps1, measure-footprint.ps1
```
