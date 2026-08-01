<#
.SYNOPSIS
Builds assets/MajikUtils.ico (the exe icon) from assets/icon-source.png.

.DESCRIPTION
Trims the source to its artwork, squares it, and writes a multi-size .ico.

Sizes up to 64px are written as BMP/DIB frames and the large ones as PNG. That split is
deliberate: PNG-compressed frames are only understood from Vista onward, and while Windows 11
reads them at every size, plenty of shell surfaces and third-party tools still expect the classic
DIB encoding for the small ones. The large frames have to be PNG, since a 256px DIB frame is a
quarter of a megabyte.

Run after changing the source art:
    pwsh tools/make-icon.ps1
#>

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'assets\icon-source.png'
$target = Join-Path $root 'assets\MajikUtils.ico'

if (-not (Test-Path $source)) { throw "Missing source art: $source" }

# --- trim to the artwork, then square it around its centre --------------------------------------
$bmp = [System.Drawing.Bitmap]::FromFile($source)
$data = $bmp.LockBits((New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)),
    [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $bmp.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$bmp.UnlockBits($data)

$minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $bmp.Height; $y++) {
    $row = $y * $data.Stride
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        if ($bytes[$row + $x * 4 + 3] -gt 8) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}

$w = $maxX - $minX + 1; $h = $maxY - $minY + 1
$side = [Math]::Max($w, $h)
$srcRect = New-Object System.Drawing.RectangleF(
    ($minX + $w / 2 - $side / 2), ($minY + $h / 2 - $side / 2), $side, $side)
"artwork ${w}x${h}, squared to $side"

function Render([int]$size) {
    $out = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.SmoothingMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($bmp, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)),
        $srcRect.X, $srcRect.Y, $srcRect.Width, $srcRect.Height, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $out
}

function PngFrame([System.Drawing.Bitmap]$b) {
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function DibFrame([System.Drawing.Bitmap]$b) {
    # BITMAPINFOHEADER, then bottom-up BGRA rows, then a 1bpp AND mask left all-zero (the alpha
    # channel already carries transparency, but the mask must still be present and sized).
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$b.Width); $bw.Write([int]($b.Height * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($b.Width * $b.Height * 4))
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)

    for ($y = $b.Height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $b.Width; $x++) {
            $c = $b.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }

    # Explicit 3-argument Write throughout: PowerShell resolves the single-argument overload of
    # BinaryWriter.Write against a byte[] to Write(byte) and silently emits one byte.
    $maskRow = [int][Math]::Floor((($b.Width + 31) / 32)) * 4
    $mask = New-Object byte[] ($maskRow * $b.Height)
    $bw.Write($mask, 0, $mask.Length)
    $bw.Flush()
    return $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($size in $sizes) {
    $image = Render $size
    $payload = if ($size -le 64) { DibFrame $image } else { PngFrame $image }
    $frames += [pscustomobject]@{ Size = $size; Data = $payload }
    $image.Dispose()
}

$fs = [System.IO.File]::Create($target)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([int16]0); $bw.Write([int16]1); $bw.Write([int16]$frames.Count)

$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $bw.Write([byte]$(if ($f.Size -ge 256) { 0 } else { $f.Size }))
    $bw.Write([byte]$(if ($f.Size -ge 256) { 0 } else { $f.Size }))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]$f.Data.Length); $bw.Write([int]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write($f.Data, 0, $f.Data.Length) }
$bw.Flush(); $fs.Close()
$bmp.Dispose()

"wrote $target ($((Get-Item $target).Length) bytes, $($frames.Count) frames: $($sizes -join ', '))"
