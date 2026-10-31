<#
.SYNOPSIS
  Builds the Store upload package (.msixupload) for Partner Center.

.DESCRIPTION
  1. Reads the Partner Center identity from store\store-identity.json (refuses TODO placeholders
     unless -TestIdentity is given).
  2. Writes that identity and version into Package.appxmanifest for the duration of the build only;
     the original file is restored byte for byte afterwards, so the dev identity is untouched.
  3. Publishes Native AOT x64 and ARM64 MSIX packages and bundles them into an .msixupload.
     The Store signs the package; no certificate is needed for the upload.

  Before uploading, run the Windows App Certification Kit on the .msix files (see docs\STORE.md).

.PARAMETER Platforms     Architectures to include (default x64, ARM64).
.PARAMETER TestIdentity  Keep the development identity (LockMonitor.Dev) to test the packaging
                         itself; the result cannot be uploaded.
#>
[CmdletBinding()]
param(
    [string[]] $Platforms = @('x64', 'ARM64'),
    [switch] $TestIdentity
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\LockMonitor.Widget\LockMonitor.Widget.csproj'
$manifestPath = Join-Path $repo 'src\LockMonitor.Widget\Package.appxmanifest'
$identity = Get-Content (Join-Path $repo 'store\store-identity.json') -Raw | ConvertFrom-Json
$outDir = Join-Path $repo 'artifacts\store'

if (-not $TestIdentity) {
    $todo = 'IdentityName', 'Publisher', 'PublisherDisplayName' | Where-Object { $identity.$_ -match 'TODO' }
    if ($todo) { throw "Fill in store\store-identity.json first (still TODO: $($todo -join ', '))." }
}
if ($identity.Version -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "Store package versions must end in .0 (got $($identity.Version))."
}

# The ILCompiler's vcvarsall lookup calls vswhere.exe through PATH.
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"

$original = [IO.File]::ReadAllBytes($manifestPath)
try {
    [xml]$manifest = [Text.Encoding]::UTF8.GetString($original)
    $manifest.Package.Identity.Version = $identity.Version
    if (-not $TestIdentity) {
        $manifest.Package.Identity.Name = $identity.IdentityName
        $manifest.Package.Identity.Publisher = $identity.Publisher
        $manifest.Package.Properties.PublisherDisplayName = $identity.PublisherDisplayName
        $manifest.Package.Properties.DisplayName = $identity.DisplayName
    }
    $manifest.Save($manifestPath)

    Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($platform in $Platforms) {
        $rid = if ($platform -eq 'x64') { 'win-x64' } else { 'win-arm64' }
        Write-Host "Packaging $platform (Native AOT)..."
        dotnet publish $project -c Release -p:Platform=$platform -r $rid -p:PublishAot=true `
            -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=StoreUpload `
            -p:AppxBundle=Never -p:AppxPackageSigningEnabled=false `
            -p:AppxPackageDir="$outDir\$platform\" --nologo -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Packaging $platform failed ($LASTEXITCODE)" }
    }
}
finally {
    [IO.File]::WriteAllBytes($manifestPath, $original)
}

Write-Host ''
Write-Host 'Packages:'
Get-ChildItem $outDir -Recurse -Include *.msix, *.msixupload, *.msixbundle |
    ForEach-Object { '  {0}  ({1:N1} MB)' -f $_.FullName.Substring($repo.Length + 1), ($_.Length / 1MB) }
