<#
.SYNOPSIS
Draws assets/icons/shelf.png — the beige folder-box icon for the Shelf taskbar button.

.DESCRIPTION
Drawn rather than sourced so it can be regenerated and tweaked. The shape is a folder seen
slightly open: a taller back panel with a tab, and a lighter front panel in front of it, so the
gap between them reads as somewhere things get dropped.

Everything is sized off a 256px canvas with a margin, because Windows does not pad taskbar icons
and artwork running to the edge sits noticeably larger than its neighbours.

    pwsh tools/make-shelf-icon.ps1
#>

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'assets\icons\shelf.png'

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.InterpolationMode = 'HighQualityBicubic'
$g.Clear([System.Drawing.Color]::Transparent)

function RoundedRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# --- soft contact shadow, so the icon does not look pasted on ------------------------------------
$shadow = RoundedRect 30 196 196 30 15
$g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(46, 0, 0, 0))), $shadow)
$shadow.Dispose()

# --- back panel and its tab, in the darker tan ---------------------------------------------------
$backColour = [System.Drawing.Color]::FromArgb(255, 198, 150, 96)
$tab = RoundedRect 26 46 96 34 12
$g.FillPath((New-Object System.Drawing.SolidBrush($backColour)), $tab)
$tab.Dispose()

$back = RoundedRect 26 62 204 142 18
$backBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 62)), (New-Object System.Drawing.Point(0, 204)),
    [System.Drawing.Color]::FromArgb(255, 214, 166, 110), $backColour)
$g.FillPath($backBrush, $back)
$back.Dispose(); $backBrush.Dispose()

# --- front panel in the lighter beige, sitting proud of the back ---------------------------------
$front = RoundedRect 26 104 204 106 18
$frontBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Point(0, 104)), (New-Object System.Drawing.Point(0, 210)),
    [System.Drawing.Color]::FromArgb(255, 243, 222, 185),
    [System.Drawing.Color]::FromArgb(255, 226, 195, 146))
$g.FillPath($frontBrush, $front)

# A light top edge on the front panel is what separates it from the back at small sizes; without it
# the two tans merge into one flat blob by the time the icon is 16px.
$g.DrawPath((New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 255, 245, 224), 3)), $front)
$front.Dispose(); $frontBrush.Dispose()

$g.Dispose()
$bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

"wrote $target ($((Get-Item $target).Length) bytes)"
