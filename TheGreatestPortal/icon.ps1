# Makes icon.png (256x256) from icon-source.png, an in-game screenshot of a portal, inside a
# carved stone frame with Elder Futhark runes along the edges.
# Usage: powershell -ExecutionPolicy Bypass -File icon.ps1 [-CropX 80 -CropY 55 -CropSize 820]
param(
    [string]$Source = (Join-Path $PSScriptRoot "icon-source.png"),
    [string]$Out = (Join-Path $PSScriptRoot "icon.png"),
    [int]$CropX = 80,      # top-left of the square crop, in source pixels
    [int]$CropY = 55,
    [int]$CropSize = 820,
    [int]$Band = 22        # frame width in icon pixels
)
Add-Type -AssemblyName System.Drawing

function C($a, $r, $gg, $b) { [System.Drawing.Color]::FromArgb([int]$a, [int]$r, [int]$gg, [int]$b) }

$size = 256
$src = [System.Drawing.Image]::FromFile($Source)
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = 'HighQualityBicubic'
$g.SmoothingMode = 'AntiAlias'
$g.PixelOffsetMode = 'HighQuality'
$g.CompositingQuality = 'HighQuality'

# ── the frame's stone ─────────────────────────────────────────────────────────
$stone = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point $size, $size), (C 255 62 46 38), (C 255 34 24 20)
$g.FillRectangle($stone, 0, 0, $size, $size)
$stone.Dispose()
# a little grain
$rnd = New-Object System.Random 3
for ($i = 0; $i -lt 260; $i++) {
    $x = $rnd.Next(0, $size); $y = $rnd.Next(0, $size)
    if ($x -ge $Band -and $x -lt $size - $Band -and $y -ge $Band -and $y -lt $size - $Band) { continue }
    $shade = $rnd.Next(0, 2) -eq 0
    $b = New-Object System.Drawing.SolidBrush ($(if ($shade) { C 70 0 0 0 } else { C 40 255 220 180 }))
    $g.FillRectangle($b, $x, $y, $rnd.Next(1, 4), 1)
    $b.Dispose()
}

# ── the portal, inside the frame ──────────────────────────────────────────────
$inner = $size - 2 * $Band
$dest = New-Object System.Drawing.Rectangle $Band, $Band, $inner, $inner
$g.DrawImage($src, $dest, $CropX, $CropY, $CropSize, $CropSize, [System.Drawing.GraphicsUnit]::Pixel)
# the game's crosshair sits just right of centre in the source; paint a neighbouring patch over it
$scale = $inner / $CropSize
$patchDst = New-Object System.Drawing.RectangleF ([single]($Band + (668 - $CropX) * $scale)), ([single]($Band + (585 - $CropY) * $scale)), ([single](34 * $scale)), ([single](34 * $scale))
$patchSrc = New-Object System.Drawing.RectangleF ([single]640), ([single]585), ([single]34), ([single]34)
$g.DrawImage($src, $patchDst, $patchSrc, [System.Drawing.GraphicsUnit]::Pixel)
$src.Dispose()

# ── bevels ────────────────────────────────────────────────────────────────────
$outerDark = New-Object System.Drawing.Pen (C 255 18 12 10), 3
$g.DrawRectangle($outerDark, 1, 1, $size - 3, $size - 3)
$outerDark.Dispose()
$outerLight = New-Object System.Drawing.Pen (C 120 190 150 100), 1
$g.DrawRectangle($outerLight, 3, 3, $size - 7, $size - 7)
$outerLight.Dispose()
$innerGold = New-Object System.Drawing.Pen (C 230 200 150 80), 2
$g.DrawRectangle($innerGold, $Band - 2, $Band - 2, $inner + 3, $inner + 3)
$innerGold.Dispose()
$innerDark = New-Object System.Drawing.Pen (C 200 20 12 8), 1
$g.DrawRectangle($innerDark, $Band - 4, $Band - 4, $inner + 7, $inner + 7)
$innerDark.Dispose()

# ── runes: Elder Futhark as line segments in a 1x1 box (y down) ───────────────
$runes = @(
    @(@(0.2,0,0.2,1), @(0.2,0.2,0.8,0.02), @(0.2,0.5,0.8,0.32)),                      # Fehu
    @(@(0.15,1,0.15,0), @(0.15,0,0.85,0.35), @(0.85,0.35,0.85,1)),                    # Uruz
    @(@(0.2,0,0.2,1), @(0.2,0.25,0.8,0.5), @(0.8,0.5,0.2,0.75)),                      # Thurisaz
    @(@(0.2,0,0.2,1), @(0.2,0.1,0.8,0.35), @(0.2,0.45,0.8,0.7)),                      # Ansuz
    @(@(0.2,0,0.2,1), @(0.2,0,0.8,0.25), @(0.8,0.25,0.2,0.5), @(0.2,0.5,0.8,1)),      # Raido
    @(@(0.7,0,0.2,0.5), @(0.2,0.5,0.7,1)),                                            # Kenaz
    @(@(0.1,0.1,0.9,0.9), @(0.9,0.1,0.1,0.9)),                                        # Gebo
    @(@(0.2,0,0.2,1), @(0.2,0,0.8,0.25), @(0.8,0.25,0.2,0.5)),                        # Wunjo
    @(@(0.2,0,0.2,1), @(0.8,0,0.8,1), @(0.2,0.35,0.8,0.65)),                          # Hagalaz
    @(@(0.5,0,0.5,1), @(0.2,0.35,0.8,0.65)),                                          # Nauthiz
    @(@(0.5,0,0.5,1)),                                                                # Isa
    @(@(0.3,0.15,0.6,0.4), @(0.6,0.4,0.3,0.65), @(0.7,0.85,0.4,0.6), @(0.4,0.6,0.7,0.35)), # Jera
    @(@(0.5,0,0.5,1), @(0.5,0,0.8,0.2), @(0.5,1,0.2,0.8)),                            # Eihwaz
    @(@(0.5,0.35,0.5,1), @(0.5,0.35,0.15,0), @(0.5,0.35,0.85,0)),                     # Algiz
    @(@(0.7,0,0.3,0.4), @(0.3,0.4,0.7,0.6), @(0.7,0.6,0.3,1)),                        # Sowilo
    @(@(0.5,0,0.5,1), @(0.5,0,0.2,0.3), @(0.5,0,0.8,0.3)),                            # Tiwaz
    @(@(0.2,0,0.2,1), @(0.2,0,0.75,0.25), @(0.75,0.25,0.2,0.5), @(0.2,0.5,0.75,0.75), @(0.75,0.75,0.2,1)), # Berkano
    @(@(0.15,0,0.15,1), @(0.85,0,0.85,1), @(0.15,0,0.85,0.5), @(0.85,0,0.15,0.5)),    # Mannaz
    @(@(0.3,0,0.3,1), @(0.3,0,0.75,0.35)),                                            # Laguz
    @(@(0.15,0,0.15,1), @(0.85,0,0.85,1), @(0.15,0,0.85,1), @(0.85,0,0.15,1))         # Dagaz
)

function DrawRune($rune, $x, $y, $w, $h) {
    foreach ($pass in @(@(3.6, (C 90 255 140 40)), @(1.6, (C 240 255 205 120)))) {
        $pen = New-Object System.Drawing.Pen $pass[1], ([single]$pass[0])
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        foreach ($s in $rune) {
            $g.DrawLine($pen, [single]($x + $s[0] * $w), [single]($y + $s[1] * $h), [single]($x + $s[2] * $w), [single]($y + $s[3] * $h))
        }
        $pen.Dispose()
    }
}

$rw = 8; $rh = 12
$step = 15
$run = $size - 2 * $Band - 8          # room between the corner marks
$count = [math]::Floor($run / $step)
$start = $Band + 4 + ($run - ($count - 1) * $step - $rw) / 2
$mid = ($Band - $rh) / 2
$k = 0
for ($i = 0; $i -lt $count; $i++) {
    $x = $start + $i * $step
    DrawRune $runes[$k % $runes.Count] $x $mid $rw $rh; $k++                                  # top
    DrawRune $runes[($k + 7) % $runes.Count] $x ($size - $Band + $mid) $rw $rh; $k++          # bottom
}
$k = 3
for ($i = 0; $i -lt $count; $i++) {
    $y = $start + $i * $step
    DrawRune $runes[$k % $runes.Count] (($Band - $rw) / 2) $y $rw $rh; $k++                  # left
    DrawRune $runes[($k + 11) % $runes.Count] ($size - $Band + ($Band - $rw) / 2) $y $rw $rh; $k++  # right
}

# ── corner knots ──────────────────────────────────────────────────────────────
foreach ($cx in @(($Band / 2), ($size - $Band / 2))) {
    foreach ($cy in @(($Band / 2), ($size - $Band / 2))) {
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ([single]$cx), ([single]($cy - 7))),
            (New-Object System.Drawing.PointF ([single]($cx + 7)), ([single]$cy)),
            (New-Object System.Drawing.PointF ([single]$cx), ([single]($cy + 7))),
            (New-Object System.Drawing.PointF ([single]($cx - 7)), ([single]$cy)))
        $glow = New-Object System.Drawing.Pen (C 90 255 140 40), 4
        $g.DrawPolygon($glow, $pts); $glow.Dispose()
        $fill = New-Object System.Drawing.SolidBrush (C 255 150 96 44)
        $g.FillPolygon($fill, $pts); $fill.Dispose()
        $edge = New-Object System.Drawing.Pen (C 240 255 205 120), 1.4
        $g.DrawPolygon($edge, $pts); $edge.Dispose()
        $dot = New-Object System.Drawing.SolidBrush (C 255 255 235 170)
        $g.FillEllipse($dot, [single]($cx - 2), [single]($cy - 2), [single]4, [single]4); $dot.Dispose()
    }
}

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"saved $Out"
