# Turns a generated painting into the 256x256 package icon, the way HungryViking's was made:
# center-crop to a square, high-quality resize, and erase the image generator's watermark from
# the bottom-right corner by cloning the patch just above it.
#
# Usage: powershell -ExecutionPolicy Bypass -File icon-from-image.ps1 -Source .\RelaxedViking.png
#        add -KeepWatermark if the corner is clean, -Patch 40 to erase a bigger corner.
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$Out = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "icon.png"),
    [int]$Size = 256,
    [int]$Patch = 36,          # icon pixels erased from the bottom-right corner
    [switch]$KeepWatermark
)
Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
$side = [math]::Min($src.Width, $src.Height)
$crop = New-Object System.Drawing.Rectangle (([int](($src.Width - $side) / 2)), ([int](($src.Height - $side) / 2)), $side, $side)

$bmp = New-Object System.Drawing.Bitmap $Size, $Size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = 'HighQualityBicubic'
$g.SmoothingMode = 'HighQuality'
$g.PixelOffsetMode = 'HighQuality'
$g.CompositingQuality = 'HighQuality'
$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $Size, $Size), $crop, [System.Drawing.GraphicsUnit]::Pixel)

if (-not $KeepWatermark) {
    # Cover the corner with the strip directly above it, flipped, so the join is soft.
    $donor = $bmp.Clone((New-Object System.Drawing.Rectangle ($Size - $Patch), ($Size - 2 * $Patch), $Patch, $Patch), $bmp.PixelFormat)
    $donor.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipY)
    $g.DrawImage($donor, ($Size - $Patch), ($Size - $Patch), $Patch, $Patch)
    $donor.Dispose()
}

$g.Dispose()
$src.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"saved $Out ($Size x $Size)"
