# Lock Monitor

A Windows 11 system-monitor widget for the **Widgets board** (Win+W) and the **lock screen**.
It runs as a packaged (MSIX) out-of-process COM widget provider built with C#/.NET 10 (Native AOT)
and the Windows App SDK, and needs no administrator rights or kernel driver.

| Size | Shows |
|---|---|
| **Small** (lock screen) | CPU %, RAM %, GPU % |
| **Medium** | + GPU temperature, VRAM, network ↓/↑, disk fullness of the chosen drives |
| **Large** | + usage bars, GPU name, RAM in GB, system temperature (ACPI), battery, uptime, top 3 apps by CPU |

Values above 85 % are highlighted (colour and weight, so it also works on a monochrome lock
screen). Light and dark themes come from the host's semantic colours. Tapping the card on the
Widgets board opens Task Manager.

**Settings** (widget menu → *Customize widget*), per pinned widget: update interval 1/2/5/10 s
(default 2 s, at least 5 s while battery saver is on), drives to show (default: system drive),
GPU temperature on/off (off by default on PCs with two GPUs, see below) and, on such PCs, which GPU.

## Requirements

| Tool | Version used | Notes |
|---|---|---|
| Windows 11 | 22H2 (22621) or later | Lock-screen widgets need a build with the lock-screen widgets feature |
| Developer Mode | On | Settings > System > For developers. Needed to register a loose (unsigned) package |
| .NET SDK | 10.0.x (LTS) | `dotnet --list-sdks` |
| Windows SDK | 10.0.26100 | Installed with Visual Studio |
| Visual Studio 2026 (optional) | 18.x with *.NET desktop development* + *WinUI application development* | Only needed for F5 debugging; the scripts work without it |
| MSVC linker (Native AOT) | VS *Desktop development with C++* workload | `-Aot` deploys and Store packages link with `link.exe` |
| Node.js (optional) | 20+ | Only for `tools/template-check` |
| NuGet packages | Microsoft.WindowsAppSDK.Widgets + .Runtime 2.6.4, Windows projection 10.0.26100.87 | Pinned in `Directory.Packages.props` / `Directory.Build.props` |
| Windows App Runtime | 2.6.x | Installed by the Store as a dependency |

## Build, test and deploy

```powershell
# from the repository root, in PowerShell 7 (pwsh)
dotnet test --project tests\LockMonitor.Core.Tests                 # unit tests (xUnit v3)
dotnet run --project tools\LockMonitor.Probe -- --seconds 20       # read all sensors without the widget host

pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1          # Debug | x64, register for development
pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1 -Aot     # Release, Native AOT (what ships)
pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1 -Clean   # unregister first (removes pinned widgets)

pwsh -File scripts\measure-footprint.ps1 -Seconds 60              # CPU/memory of provider + widget host
pwsh -ExecutionPolicy Bypass -File scripts\package-store.ps1       # Store upload package, see docs\STORE.md
```

**Template check** (expands the real templates with the official Adaptive Cards templating engine,
for every size and for board and lock screen):

```powershell
dotnet run --project tools\LockMonitor.Probe -c Release -- --seconds 3 --write-cards artifacts\cards
cd tools\template-check; npm ci; node check.mjs ..\..\artifacts\cards
```

`deploy-dev.ps1` builds, mirrors the output into `artifacts\dev-layout` and registers it with
`Add-AppxPackage -Register` (a separate folder avoids file locks the widget host holds on
`resources.pri`). Each deploy stamps a time-based version into the layout manifest, because
Windows refuses to re-register a changed manifest with the same version. Pinned widgets survive.

## Add the widget

**Widgets board:** **Win+W** → **+** (Add widgets) → **Lock Monitor** → **System Monitor** → **Pin**.

**Lock screen:** **Settings → Personalization → Lock screen → Widgets → Add widget** → **System Monitor**.
Only widgets with a *small* size can be added there. Lock with **Win+L** to check. Organisations
can turn lock-screen widgets off with the *Disable Widgets On Lock Screen* group policy.

## Data sources

All verified without administrator rights on Windows 11 26H2.

| Metric | Source | Notes |
|---|---|---|
| CPU % | PDH `\Processor Information(_Total)\% Processor Utility` | Task Manager's counter; clamped to 100 % |
| RAM | `GlobalMemoryStatusEx` | |
| GPU % | PDH `\GPU Engine(*)\Utilization Percentage` | Summed per adapter and engine type, busiest engine wins (Task Manager's method). Default adapter: most dedicated VRAM (DXGI) |
| VRAM | PDH `\GPU Adapter Memory(*)\Dedicated Usage` (shared usage for integrated GPUs) + DXGI totals | matched `nvidia-smi` within 1 % |
| GPU temperature | `D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA)`, NVML fallback for NVIDIA | WDDM 2.4+ driver must report it; 42.5 °C vs `nvidia-smi` 42.7 °C |
| Network ↓/↑ | `GetIfTable2` byte counters, deltas | physical, connected interfaces only (no filter/loopback/tunnel/Hyper-V) |
| Disk | `DriveInfo` for fixed drives, every 30 s | |
| System temp (ACPI) | PDH `\Thermal Zone Information(*)\High Precision Temperature` | **Not the CPU core temperature** — see below |
| Battery | `Windows.System.Power.PowerManager` change events | hidden without a battery |
| Uptime | `GetTickCount64` | like Task Manager (Fast Startup "Shut down" does not reset it) |
| Top 3 apps | one `NtQuerySystemInformation(SystemProcessInformation)` call | grouped by app, refreshed every 5 s (~10 ms per call on ~600 processes) |

**CPU temperature.** There is no documented way to read the CPU core/package temperature without a
kernel driver or administrator rights (`MSAcpi_ThermalZoneTemperature` needs admin; LibreHardwareMonitor
and similar tools use a driver). The thermal-zone counter is readable but is a firmware-defined ACPI
zone; on many desktops it is missing or a fixed placeholder. The widget therefore labels it
"System temp (ACPI)" and hides it when it does not change for 5 minutes.

**Hybrid graphics.** Querying a sleeping discrete GPU's temperature can keep it awake and drain the
battery. On PCs with two hardware GPUs the GPU-temperature setting is off by default, and while it
is off no temperature query is made at all.

## How it works

- **Sampling only while visible.** The host calls `Activate` when the board opens or the lock screen
  shows the widget and `Deactivate` when it hides it (observed: the lock screen behaves exactly
  like the board). One background loop samples while at least one widget is active; with none it
  waits without a timer and every sensor handle is closed — zero polling.
- **Only what is shown is measured.** Sensors run for the largest active size; GPU temperature only
  for adapters an active widget shows it for.
- **Isolation.** Each sensor runs in a `SensorRunner`; an exception turns only its values into `N/A`
  and repeated failures back off exponentially.
- **Templates.** One Adaptive Card template per size, merged into one card with
  `$when: ${$host.widgetSize==...}`; a board variant with the Task Manager tap action and a lock
  screen variant without it (`$host.isUserContextAuthenticated`). Cards render only when their own
  data is bound, because the host briefly pairs templates and data while customizing.
- **Customization flow** (observed, not documented): `OnCustomizationRequested`, possibly a
  `Deactivate` of the board card, then an `Activate` of the settings flyout. Closing the flyout
  with X sends no action — only a `Deactivate` while the flyout shows — which the provider treats as
  "cancel".
- **Multiple widgets** are tracked by widget id; settings live in each widget's `CustomState`.

### Measured footprint

Native AOT build, Ryzen 7 7435HS / RTX 4060 Laptop:

| Situation | Provider CPU (one core) | Provider private memory | WidgetService | WidgetBoard |
|---|---|---|---|---|
| Board open, 1 small widget, 2 s (60 s) | **0.18 %** | 11.7 MB | 1.15 % | 0.62 % |
| No visible widget (30 s) | **0.00 %** (0 ms) | 10.3 MB | 0.00 % | 0.00 % |

Sensor cost per sample: small card ~2.8 ms, large card ~7 ms (top processes every 5 s).
Package: 2.0 MB `.msix` (x64).

## Troubleshooting

- Provider log (lifecycle events and sensor errors only, never per sample):
  `%LOCALAPPDATA%\Packages\LockMonitor.Dev_w3qjt9ra1beny\LocalCache\Local\LockMonitor\provider.log`.
  Each `Deactivate` line says how long the widget was active and how many updates it received.
- The provider process is `LockMonitor.Widget.exe`. The host starts it on demand through COM
  (`-RegisterProcessAsComServer`); it exits 30 s after the last widget is removed.
- Launching the app from Start only shows a short "how to add the widget" message.
- `REGDB_E_CLASSNOTREG` when running the exe directly: the widget APIs need package identity.
- *"Couldn't add widget" (Native AOT only):* the provider class must carry
  `[WinRT.GeneratedWinRTExposedType]`, otherwise CsWinRT emits no WinRT vtables outside WinUI/UWP
  projects and the host gets `E_NOINTERFACE` (logged by the class factory).
- Build fails with `PRI210 ... 0x800704c8`: rename the stale `resources.pri` in `bin\...` and rebuild.

## Project layout

```
src/LockMonitor.Core/            sensors, sampling, rendering (no Windows App SDK dependency)
  Native/                        PDH, DXGI, D3DKMT, NVML, IP Helper, ntdll interop (AOT safe)
  Sensors/                       one class per metric + SensorRunner isolation + catalogs
  Sampling/                      SamplingScheduler (single loop), SamplingPlan, SystemSnapshot
  Rendering/                     TemplateStore, CardData, SettingsCard, WidgetSettings, Units
src/LockMonitor.Widget/          packaged widget provider (MSIX)
  Program.cs, Com/, Provider/    COM server, IWidgetProvider, sessions, lifetime
  Templates/                     small/medium/large/settings Adaptive Card templates
  Package.appxmanifest           COM server + com.microsoft.windows.widgets extension
tools/LockMonitor.Probe/         reads the sensors, reports cost, exports cards
tools/template-check/            checks templates with adaptivecards-templating
tests/LockMonitor.Core.Tests/    unit tests
scripts/                         deploy-dev, package-store, make-assets, measure-footprint
store/store-identity.json        Partner Center identity (TODO placeholders)
docs/STORE.md, PRIVACY.md        Store submission checklist and privacy policy
```
