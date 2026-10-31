<#
.SYNOPSIS
  Builds Lock Monitor and registers it as a development (loose-file) package.

.DESCRIPTION
  Builds the provider, mirrors the output into artifacts\dev-layout and registers that folder
  with Add-AppxPackage -Register (requires Developer Mode). Registering from a separate layout
  folder keeps the build output free of file locks held by the widget host.

  Pinned widgets survive a redeploy as long as -Clean is not used.

.PARAMETER Configuration  Debug (default) or Release.
.PARAMETER Platform       x64 (default) or ARM64.
.PARAMETER Clean          Unregister the package first (removes pinned widgets).
.PARAMETER Aot            Deploy a Native AOT build (forces Release). Needs the MSVC linker
                          ("Desktop development with C++" workload).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [ValidateSet('x64', 'ARM64')] [string] $Platform = 'x64',
    [switch] $Clean,
    [switch] $Aot
)
if ($Aot) { $Configuration = 'Release' }

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\LockMonitor.Widget\LockMonitor.Widget.csproj'
$rid = if ($Platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
$output = Join-Path $repo "src\LockMonitor.Widget\bin\$Platform\$Configuration\net10.0-windows10.0.26100.0\$rid"
$layout = Join-Path $repo 'artifacts\dev-layout'
$packageName = 'LockMonitor.Dev'

function Stop-Provider {
    Get-Process LockMonitor.Widget -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

if ($Clean) {
    Write-Host "Unregistering $packageName (pinned widgets will be removed)..."
    Get-AppxPackage -Name $packageName | Remove-AppxPackage
}

Write-Host "Building $Configuration|$Platform..."
dotnet build $project -c $Configuration -p:Platform=$Platform --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

if ($Aot) {
    # The ILCompiler's vcvarsall lookup calls vswhere.exe through PATH.
    $env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
    $publish = Join-Path $repo 'artifacts\aot-publish'
    Write-Host "Publishing Native AOT to $publish..."
    dotnet publish $project -c Release -p:Platform=$Platform -r $rid -p:PublishAot=true `
        -p:GenerateAppxPackageOnBuild=false --nologo -v:minimal -o $publish
    if ($LASTEXITCODE -ne 0) { throw "AOT publish failed ($LASTEXITCODE)" }
    # Publish does not emit the generated AppxManifest.xml; take it from the Release build.
    Copy-Item (Join-Path $output 'AppxManifest.xml') $publish -Force
    $output = $publish
}

Stop-Provider
Write-Host "Copying layout to $layout..."
New-Item -ItemType Directory -Force $layout | Out-Null
robocopy $output $layout /MIR /XF *.locked-old *.pdb /NFL /NDL /NJH /NJS /NP /R:2 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE). A file may be locked; retry with -Clean." }

# Windows refuses to re-register a changed manifest with the same version in developer mode,
# so the dev layout gets a time-based build/revision (the source manifest is left untouched).
$manifestPath = Join-Path $layout 'AppxManifest.xml'
[xml]$manifest = Get-Content $manifestPath -Raw
$now = Get-Date
$base = [version]$manifest.Package.Identity.Version
$devVersion = '{0}.{1}.{2}.{3}' -f $base.Major, $base.Minor, (($now.Year - 2000) * 400 + $now.DayOfYear), [int]($now.TimeOfDay.TotalSeconds / 2)
$manifest.Package.Identity.Version = $devVersion
$manifest.Save($manifestPath)

Write-Host "Registering $packageName $devVersion..."
Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown -ForceUpdateFromAnyVersion

$pkg = Get-AppxPackage -Name $packageName
Write-Host "Registered $($pkg.PackageFamilyName) $($pkg.Version) from $($pkg.InstallLocation)"
Write-Host "Provider log: $env:LOCALAPPDATA\Packages\$($pkg.PackageFamilyName)\LocalCache\Local\LockMonitor\provider.log"
exit 0
