# Lock Monitor

A Windows 11 system-monitor widget for the **Widgets board** (Win+W) and the **lock screen**.
It runs as a packaged (MSIX) out-of-process COM widget provider built with C#/.NET 10 and the
Windows App SDK, and needs no administrator rights or kernel driver.

> Status: **phase 1 — skeleton.** The small widget shows fixed values (CPU 23 %, RAM 47 %, GPU 91 %)
> so registration, rendering, theming and the >85 % highlight can be checked end to end.

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

## Build and deploy (development)

```powershell
# from the repository root, in PowerShell 7 (pwsh)
pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1          # Debug | x64
pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1 -Aot     # Release, Native AOT (smallest footprint)
pwsh -ExecutionPolicy Bypass -File scripts\deploy-dev.ps1 -Clean   # unregister first (removes pinned widgets)
```

Each deploy stamps a time-based build/revision number into the *layout* manifest, because Windows
refuses to re-register a changed manifest with the same version in developer mode.

### Measured footprint (phase 1, idle provider with one pinned widget)

| Build | Package size | Private memory | Working set | Start-up CPU |
|---|---|---|---|---|
| Debug, JIT, self-contained | 103 MB | 9.3 MB | 38.9 MB | — |
| Release, Native AOT | 3.9 MB | 4.9 MB | 16.0 MB | 31 ms |

The script builds the project, mirrors the output into `artifacts\dev-layout` and registers it with
`Add-AppxPackage -Register`. Registering from a separate folder keeps the build output free of file
locks the widget host holds on `resources.pri`.

> If a build ever fails with `PRI210 ... 0x800704c8` (file mapped by another process), rename the
> stale `resources.pri` in the `bin\...` folder and build again.

## Add the widget

**Widgets board:** press **Win+W** → **+** (Add widgets) → **Lock Monitor** → **System Monitor** → **Pin**.

**Lock screen:** **Settings → Personalization → Lock screen → Widgets → Add widget** → **System Monitor**.
Only widgets that support the *small* size can be added there. Lock with **Win+L** to check.
Organisations can disable lock-screen widgets with the *Disable Widgets On Lock Screen* group policy.

## Troubleshooting

- Provider log (lifecycle events only):
  `%LOCALAPPDATA%\Packages\LockMonitor.Dev_w3qjt9ra1beny\LocalCache\Local\LockMonitor\provider.log`
- The provider process is `LockMonitor.Widget.exe`. The widget host starts it on demand through COM
  (`-RegisterProcessAsComServer`) and it exits 30 s after the last widget is removed.
- Launching the app from Start only shows a short "how to add the widget" message.
- Running `LockMonitor.Widget.exe -RegisterProcessAsComServer` directly (without package identity)
  fails with `REGDB_E_CLASSNOTREG`; the widget APIs require the packaged context.

## Project layout

```
src/LockMonitor.Widget/
  Program.cs                 entry point, COM class registration
  Com/ClassFactory.cs        source-generated IClassFactory (Native AOT friendly)
  Provider/                  IWidgetProvider implementation, per-widget sessions, process lifetime
  Rendering/                 template composition and card data
  Templates/small.json       Adaptive Card template for the small size (lock screen)
  Package.appxmanifest       COM server + com.microsoft.windows.widgets extension
scripts/deploy-dev.ps1       build + register for development
```
