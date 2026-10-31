<#
.SYNOPSIS
  Composes 1920x1080 Store listing screenshots from widget captures.

.DESCRIPTION
  Partner Center needs desktop screenshots of at least 1366x768. This places the widget captures
  from the screenshots folder (scaled 2x, high-quality bicubic) on a dark background together with
  the product name and the main features. Output: store\screenshots\*.png.
  Native full-resolution captures are sharper; replace these when you have them.
#>
[CmdletBinding()]
param(
    [string] $Medium = 'screenshots\med-widgetpanel.png',
    [string] $Large = 'screenshots\large-widgetpanel.png'
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repo 'store\screenshots'
New-Item -ItemType Directory -Force $out | Out-Null

function New-Canvas {
    $bmp = [System.Drawing.Bitmap]::new(1920, 1080)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'
    $rect = [System.Drawing.Rectangle]::new(0, 0, 1920, 1080)
    $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect,
        [System.Drawing.Color]::FromArgb(255, 18, 24, 38), [System.Drawing.Color]::FromArgb(255, 8, 52, 92), 35)
    $g.FillRectangle($bg, $rect)
    return $bmp, $g
}

function Draw-Widget($g, [string]$path, [int]$x, [int]$y, [double]$scale) {
    $img = [System.Drawing.Image]::FromFile((Join-Path $repo $path))
    $w = [int]($img.Width * $scale); $h = [int]($img.Height * $scale)
    # soft shadow
    $shadow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(70, 0, 0, 0))
    $g.FillRectangle($shadow, $x + 10, $y + 14, $w, $h)
    $g.DrawImage($img, $x, $y, $w, $h)
    $img.Dispose()
    return $h
}

function Draw-Text($g, [string]$text, [float]$size, [string]$style, [System.Drawing.Color]$color, [float]$x, [float]$y, [float]$width = 820) {
    $font = [System.Drawing.Font]::new('Segoe UI', $size, [System.Drawing.FontStyle]::$style, [System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString($text, $font, [System.Drawing.SolidBrush]::new($color), [System.Drawing.RectangleF]::new($x, $y, $width, 1000))
    return $g.MeasureString($text, $font, [int]$width).Height
}

$white = [System.Drawing.Color]::White
$muted = [System.Drawing.Color]::FromArgb(255, 190, 205, 225)

# 1: headline left, medium + large widgets right (both fully inside the frame)
$bmp, $g = New-Canvas
$y = 330
$y += Draw-Text $g 'Widget Monitor' 64 'Bold' $white 100 $y 620
$y += Draw-Text $g 'CPU, memory and GPU at a glance on the Widgets board and the lock screen.' 34 'Regular' $muted 100 ($y + 10) 600
Draw-Text $g 'No admin rights · no drivers · no data collection' 24 'Regular' $muted 100 ($y + 50) 640 | Out-Null
Draw-Widget $g $Medium 745 243 2.0 | Out-Null    # 590 x 594
Draw-Widget $g $Large 1365 81 1.75 | Out-Null    # 527 x 803, ends at 1892
$bmp.Save((Join-Path $out '01-widget-monitor.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

# 2: large widget with the feature list
$bmp, $g = New-Canvas
Draw-Widget $g $Large 200 81 2.0 | Out-Null       # 602 x 918
$y = 200
$y += Draw-Text $g 'Everything on one card' 64 'Bold' $white 960 $y 860
$features = @(
    'CPU, RAM and GPU usage, highlighted above 85 %',
    'GPU temperature and video memory',
    'Network speed and drive fullness',
    'Battery, uptime and the busiest apps (large size)',
    'Small size for the lock screen',
    'Updates every 1-10 s, only while the widget is visible'
)
$y += 30
foreach ($f in $features) {
    $y += 18 + (Draw-Text $g ("•  " + $f) 34 'Regular' $muted 960 $y 860)
}
$bmp.Save((Join-Path $out '02-everything-on-one-card.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Get-ChildItem $out -Filter *.png | ForEach-Object {
    $i = [System.Drawing.Image]::FromFile($_.FullName); '{0}  {1}x{2}  {3:N0} KB' -f $_.Name, $i.Width, $i.Height, ($_.Length / 1KB); $i.Dispose()
}
