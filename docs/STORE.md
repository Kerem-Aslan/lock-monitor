# Microsoft Store submission

Checklist and texts for publishing Widget Monitor through Partner Center.

## 1. Reserve the app and fill in the identity

1. Partner Center → **Apps and games** → **New product** → **MSIX or PWA app** → reserve the name
   (e.g. *Widget Monitor*).
2. **Product management → Product identity** shows the values the package must use. Copy them
   into [`store/store-identity.json`](../store/store-identity.json):

   | JSON field | Partner Center value | Manifest |
   |---|---|---|
   | `IdentityName` | Package/Identity/Name | `Identity@Name` |
   | `Publisher` | Package/Identity/Publisher (`CN=...`) | `Identity@Publisher` |
   | `PublisherDisplayName` | Package/Properties/PublisherDisplayName | `Properties/PublisherDisplayName` |
   | `DisplayName` | the reserved app name | `Properties/DisplayName` |
   | `Version` | your release number, must end in `.0` | `Identity@Version` |

   The development package keeps the `LockMonitor.Dev` identity in `Package.appxmanifest`; the
   Store values are applied only while `package-store.ps1` builds.

## 2. Build the upload package

```powershell
pwsh -ExecutionPolicy Bypass -File scripts\package-store.ps1              # x64 + ARM64, Native AOT
pwsh -ExecutionPolicy Bypass -File scripts\package-store.ps1 -TestIdentity -Platforms x64   # dry run
```

Output in `artifacts\store\<platform>\`: one `.msixupload` per architecture (package + symbols).
Upload both on the **Packages** page. Measured x64 size: 2.0 MB `.msix` (Native AOT executable).

The package depends on the **Windows App Runtime 2.x** framework (`Microsoft.WindowsAppRuntime.2`,
min 2.6.4). The Store installs it automatically; nothing has to be bundled.

## 3. Test before uploading

- **Windows App Certification Kit** (part of the Windows SDK; run as administrator):
  `appcert.exe test -appxpackagepath artifacts\store\x64\...\LockMonitor.Widget_1.0.0.0_x64.msix -reportoutputpath artifacts\wack.xml`
- Install the test package on a clean machine (sideloading needs Developer Mode or the test
  certificate), add the widget to the Widgets board and the lock screen, resize it, open the
  settings card, and check light/dark themes.
- `dotnet test --project tests\LockMonitor.Core.Tests` and the template check
  (`tools\template-check`) must pass.

## 4. Restricted capability justification (`runFullTrust`)

Paste into **Submission options → Restricted capabilities**:

> Widget Monitor is a Windows widget provider. Widgets for desktop apps are implemented, per the
> Windows App SDK documentation ("Implement a widget provider in a win32 app"), as an
> out-of-process COM server in a packaged Win32 app, which requires runFullTrust. The provider
> reads system performance information that is only available to full-trust desktop processes:
> Performance Data Helper counters (CPU, GPU engines, GPU memory, thermal zones), D3DKMT adapter
> performance data (GPU temperature), GetIfTable2 (network throughput) and
> NtQuerySystemInformation (per-process CPU time for the "top processes" list). The app does not
> require administrator rights, installs no drivers or services, has no networking code and does
> not collect or transmit any data.

## 5. Store listing

| Field | Value |
|---|---|
| Category | Utilities & tools |
| Privacy policy URL | Host [`PRIVACY.md`](../PRIVACY.md) at a public URL (the repository is private), e.g. a GitHub Pages page or a public gist |
| Age rating | Complete the IARC questionnaire: no user interaction, no data sharing → 3+/Everyone |
| Search terms | system monitor, widget, CPU, GPU, RAM, temperature, lock screen |

**Short description**

> CPU, memory and GPU at a glance on the Widgets board and the lock screen.

**Description**

> Widget Monitor adds a System Monitor widget to Windows 11. Pin it to the Widgets board (Win+W) or
> add it to your lock screen to see how busy your PC is at a glance.
>
> • Small (lock screen): CPU, memory and GPU usage
> • Medium: plus GPU temperature, video memory, network speed and disk space
> • Large: plus memory details, system temperature, battery, uptime and the busiest apps
> • Values above 85 % are highlighted; light and dark themes
> • Update every 1, 2, 5 or 10 seconds; slows down automatically in battery saver
> • Choose which drives and which graphics card to show
> • Uses no resources while the widget is not visible
> • No administrator rights, no drivers, no network access, no data collection
>
> Add to the lock screen: Settings → Personalization → Lock screen → Widgets → Add widget.
> GPU temperature needs a WDDM 2.4+ driver that reports it (most discrete GPUs). "System temp
> (ACPI)" is the firmware's thermal zone, not the CPU core temperature, and is hidden on PCs that
> do not report a live value.

**Screenshots** (Partner Center requires at least one; PNG, 1366×768 or larger): take them with
the board open in dark and light theme, showing the small, medium and large sizes and the lock
screen. Recommended: 4 screenshots at 1920×1080.

**Widgets collection:** after the app is live, Microsoft can feature it in the Widgets picker's
Store collection; request inclusion with the form linked from the Learn page *Implement a widget
provider in a win32 app (C#)*.

## Package assets

Generated by `scripts\make-assets.ps1` (placeholders in final sizes; replace 1:1 with designed art):

| File | Sizes | Used for |
|---|---|---|
| `Assets\Square44x44Logo.scale-*` | 44, 88, 176 | app list, Start |
| `Assets\Square44x44Logo.targetsize-*` (+ `_altform-unplated`) | 16, 24, 32, 48, 256 | taskbar, Start, Settings |
| `Assets\Square150x150Logo.scale-*` | 150, 300, 600 | Start tile |
| `Assets\Wide310x150Logo.scale-*` | 310×150, 620×300, 1240×600 | wide tile |
| `Assets\StoreLogo.scale-*` | 50, 100, 200 | package logo |
| `ProviderAssets\Widget_Icon.png` | 128 | widget picker icon, provider icon |
| `ProviderAssets\Widget_Screenshot.png` / `_Light.png` | 300×304 | widget picker preview (dark/light) |
