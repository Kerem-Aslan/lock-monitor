<#
.SYNOPSIS
  Generates the package icons and the widget picker screenshots.

.DESCRIPTION
  Draws the Lock Monitor icon (three usage bars on an accent tile) at every size an MSIX package
  uses: scale-100/200/400 variants of the tile logos, targetsize variants of the app icon (plain
  and altform-unplated for the taskbar), the Store logo, the widget icon and the 300x304 widget
  picker screenshots (dark and light). Resource qualifiers are resolved by resources.pri, so the
  unqualified base names stay referenced from Package.appxmanifest.

  These are clean placeholders in the final sizes; a designer can replace the PNGs one-to-one.

.PARAMETER Root  The widget project folder (default: src\LockMonitor.Widget).
#>
[CmdletBinding()]
param([string] $Root = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\LockMonitor.Widget'))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = [Math]::Max(1, 2 * $r)
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Icon: rounded accent tile with three white usage bars. -Unplated draws the bars only (taskbar).
function Save-Icon([int]$w, [int]$h, [string]$path, [double]$fill = 0.8, [switch]$Unplated) {
    $bmp = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [Math]::Min($w, $h) * $fill
    $x = ($w - $s) / 2; $y = ($h - $s) / 2
    $barColor = [System.Drawing.Color]::White
    if ($Unplated) {
        $barColor = [System.Drawing.Color]::FromArgb(255, 0, 120, 212)
    } else {
        $rect = [System.Drawing.RectangleF]::new($x, $y, $s, $s)
        $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect,
            [System.Drawing.Color]::FromArgb(255, 0, 120, 212), [System.Drawing.Color]::FromArgb(255, 0, 90, 158), 90)
        $g.FillPath($brush, (New-RoundedPath $x $y $s $s ($s * 0.22)))
    }
    $bars = [System.Drawing.SolidBrush]::new($barColor)
    $barW = $s * 0.16; $gap = $s * 0.08; $base = $y + $s * 0.78
    $heights = 0.30, 0.48, 0.38
    $left = $x + ($s - (3 * $barW + 2 * $gap)) / 2
    for ($i = 0; $i -lt 3; $i++) {
        $bh = $s * $heights[$i]
        $g.FillPath($bars, (New-RoundedPath ($left + $i * ($barW + $gap)) ($base - $bh) $barW $bh ($barW * 0.3)))
    }
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

# Widget picker screenshot: 300x304 with transparent rounded corners (Learn: widgets-picker-integration).
function Save-Screenshot([string]$path, [bool]$dark) {
    $w = 300; $h = 304
    $bmp = [System.Drawing.Bitmap]::new($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $bg  = if ($dark) { [System.Drawing.Color]::FromArgb(255, 44, 44, 44) } else { [System.Drawing.Color]::FromArgb(255, 249, 249, 249) }
    $fg  = if ($dark) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(255, 27, 27, 27) }
    $sub = if ($dark) { [System.Drawing.Color]::FromArgb(255, 200, 200, 200) } else { [System.Drawing.Color]::FromArgb(255, 96, 96, 96) }
    $red = if ($dark) { [System.Drawing.Color]::FromArgb(255, 255, 153, 164) } else { [System.Drawing.Color]::FromArgb(255, 196, 43, 28) }
    $g.FillPath([System.Drawing.SolidBrush]::new($bg), (New-RoundedPath 0 0 ($w - 1) ($h - 1) 8))
    $px = [System.Drawing.GraphicsUnit]::Pixel
    $g.DrawString('System Monitor', [System.Drawing.Font]::new('Segoe UI Semibold', 12, [System.Drawing.FontStyle]::Regular, $px), [System.Drawing.SolidBrush]::new($sub), 16, 14)
    $label = [System.Drawing.Font]::new('Segoe UI', 20, [System.Drawing.FontStyle]::Regular, $px)
    $value = [System.Drawing.Font]::new('Segoe UI Semibold', 40, [System.Drawing.FontStyle]::Regular, $px)
    $rows = @(@('CPU', '23%', $false), @('RAM', '47%', $false), @('GPU', '91%', $true))
    $right = [System.Drawing.StringFormat]::new(); $right.Alignment = 'Far'
    for ($i = 0; $i -lt 3; $i++) {
        $ry = 52 + $i * 78
        $g.DrawString($rows[$i][0], $label, [System.Drawing.SolidBrush]::new($sub), 16, $ry + 18)
        $c = if ($rows[$i][2]) { $red } else { $fg }
        $g.DrawString($rows[$i][1], $value, [System.Drawing.SolidBrush]::new($c), [System.Drawing.RectangleF]::new(100, $ry, 184, 60), $right)
    }
    $g.Dispose(); $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

$assets = Join-Path $Root 'Assets'
$provider = Join-Path $Root 'ProviderAssets'
New-Item -ItemType Directory -Force $assets, $provider | Out-Null
Get-ChildItem $assets -Filter *.png | Remove-Item

foreach ($scale in 100, 200, 400) {
    $f = $scale / 100
    Save-Icon ([int](44 * $f)) ([int](44 * $f)) (Join-Path $assets "Square44x44Logo.scale-$scale.png") 0.92
    Save-Icon ([int](150 * $f)) ([int](150 * $f)) (Join-Path $assets "Square150x150Logo.scale-$scale.png") 0.6
    Save-Icon ([int](310 * $f)) ([int](150 * $f)) (Join-Path $assets "Wide310x150Logo.scale-$scale.png") 0.6
    Save-Icon ([int](50 * $f)) ([int](50 * $f)) (Join-Path $assets "StoreLogo.scale-$scale.png") 0.92
}
foreach ($size in 16, 24, 32, 48, 256) {
    Save-Icon $size $size (Join-Path $assets "Square44x44Logo.targetsize-$size.png") 1.0
    Save-Icon $size $size (Join-Path $assets "Square44x44Logo.targetsize-${size}_altform-unplated.png") 1.0 -Unplated
}

# The widget host reads these by path (no resource qualifiers).
Save-Icon 128 128 (Join-Path $provider 'Widget_Icon.png') 0.92
Save-Screenshot (Join-Path $provider 'Widget_Screenshot.png') $true
Save-Screenshot (Join-Path $provider 'Widget_Screenshot_Light.png') $false

Get-ChildItem $assets, $provider -Filter *.png | Sort-Object FullName | ForEach-Object {
    $img = [System.Drawing.Image]::FromFile($_.FullName)
    '{0,-58} {1,4}x{2,-4}' -f $_.Name, $img.Width, $img.Height
    $img.Dispose()
}
