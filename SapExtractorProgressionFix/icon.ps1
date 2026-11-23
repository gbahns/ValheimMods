# Makes icon.png (256x256): the carved stone frame with Elder Futhark runes shared by the other
# DeathMonger mods, around the sap extractor (extractor.png, a cut-out with a transparent
# background) lit from behind by a sap-colored glow.
# Usage: powershell -ExecutionPolicy Bypass -File icon.ps1
param(
    [string]$Out = (Join-Path $PSScriptRoot "icon.png"),
    [int]$Band = 22        # frame width in icon pixels
)
Add-Type -AssemblyName System.Drawing

function C($a, $r, $gg, $b) { [System.Drawing.Color]::FromArgb([int]$a, [int]$r, [int]$gg, [int]$b) }
function P($x, $y) { New-Object System.Drawing.PointF ([single]$x), ([single]$y) }

$size = 256
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
$rnd = New-Object System.Random 11
for ($i = 0; $i -lt 260; $i++) {
    $x = $rnd.Next(0, $size); $y = $rnd.Next(0, $size)
    if ($x -ge $Band -and $x -lt $size - $Band -and $y -ge $Band -and $y -lt $size - $Band) { continue }
    $shade = $rnd.Next(0, 2) -eq 0
    $b = New-Object System.Drawing.SolidBrush ($(if ($shade) { C 70 0 0 0 } else { C 40 255 220 180 }))
    $g.FillRectangle($b, $x, $y, $rnd.Next(1, 4), 1)
    $b.Dispose()
}

# ── the slate inside ──────────────────────────────────────────────────────────
$inner = $size - 2 * $Band
$slate = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point $Band, $Band), (New-Object System.Drawing.Point ($Band + $inner), ($Band + $inner)), (C 255 26 22 20), (C 255 14 12 12)
$g.FillRectangle($slate, $Band, $Band, $inner, $inner)
$slate.Dispose()

# ── the extractor ─────────────────────────────────────────────────────────────
$src = [System.Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot "extractor.png"))
# Crop to what is actually drawn, so the piece fills the slate whatever margin the cut-out has.
$minX = $src.Width; $minY = $src.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $src.Height; $y++) {
    for ($x = 0; $x -lt $src.Width; $x++) {
        if ($src.GetPixel($x, $y).A -gt 24) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
if ($maxX -lt 0) { throw "extractor.png has no opaque pixels" }
$sw = $maxX - $minX + 1; $sh = $maxY - $minY + 1
$fit = $inner - 20
$scale = [math]::Min($fit / $sw, $fit / $sh)
$dw = $sw * $scale; $dh = $sh * $scale
$dx = ($size - $dw) / 2; $dy = ($size - $dh) / 2

# The glow: sap light behind the flask, fading into the slate.
$gp = New-Object System.Drawing.Drawing2D.GraphicsPath
$gr = 96
$gp.AddEllipse([single]($size / 2 - $gr), [single]($size / 2 - $gr), [single](2 * $gr), [single](2 * $gr))
$pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $gp
$pgb.CenterColor = C 150 255 170 60
$pgb.SurroundColors = [System.Drawing.Color[]]@((C 0 255 140 40))
$g.FillPath($pgb, $gp); $pgb.Dispose(); $gp.Dispose()

$srcRect = New-Object System.Drawing.Rectangle $minX, $minY, $sw, $sh
# A soft shadow down and to the right, then the piece itself.
$ia = New-Object System.Drawing.Imaging.ImageAttributes
$cm = New-Object System.Drawing.Imaging.ColorMatrix
$cm.Matrix00 = 0; $cm.Matrix11 = 0; $cm.Matrix22 = 0; $cm.Matrix33 = 0.45
$ia.SetColorMatrix($cm)
$g.DrawImage($src, (New-Object System.Drawing.Rectangle ([int]($dx + 4)), ([int]($dy + 5)), ([int]$dw), ([int]$dh)), $minX, $minY, $sw, $sh, [System.Drawing.GraphicsUnit]::Pixel, $ia)
$ia.Dispose()
$g.DrawImage($src, (New-Object System.Drawing.RectangleF ([single]$dx), ([single]$dy), ([single]$dw), ([single]$dh)), $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$src.Dispose()

# ── bevels ────────────────────────────────────────────────────────────────────
$outerDark = New-Object System.Drawing.Pen (C 255 18 12 10), 3
$g.DrawRectangle($outerDark, 1, 1, $size - 3, $size - 3); $outerDark.Dispose()
$outerLight = New-Object System.Drawing.Pen (C 120 190 150 100), 1
$g.DrawRectangle($outerLight, 3, 3, $size - 7, $size - 7); $outerLight.Dispose()
$innerGold = New-Object System.Drawing.Pen (C 230 200 150 80), 2
$g.DrawRectangle($innerGold, $Band - 2, $Band - 2, $inner + 3, $inner + 3); $innerGold.Dispose()
$innerDark = New-Object System.Drawing.Pen (C 200 20 12 8), 1
$g.DrawRectangle($innerDark, $Band - 4, $Band - 4, $inner + 7, $inner + 7); $innerDark.Dispose()

# ── runes: Elder Futhark as line segments in a 1x1 box (y down) ───────────────
$runes = @(
    @(@(0.2,0,0.2,1), @(0.2,0.2,0.8,0.02), @(0.2,0.5,0.8,0.32)),
    @(@(0.15,1,0.15,0), @(0.15,0,0.85,0.35), @(0.85,0.35,0.85,1)),
    @(@(0.2,0,0.2,1), @(0.2,0.25,0.8,0.5), @(0.8,0.5,0.2,0.75)),
    @(@(0.2,0,0.2,1), @(0.2,0.1,0.8,0.35), @(0.2,0.45,0.8,0.7)),
    @(@(0.2,0,0.2,1), @(0.2,0,0.8,0.25), @(0.8,0.25,0.2,0.5), @(0.2,0.5,0.8,1)),
    @(@(0.7,0,0.2,0.5), @(0.2,0.5,0.7,1)),
    @(@(0.1,0.1,0.9,0.9), @(0.9,0.1,0.1,0.9)),
    @(@(0.2,0,0.2,1), @(0.2,0,0.8,0.25), @(0.8,0.25,0.2,0.5)),
    @(@(0.2,0,0.2,1), @(0.8,0,0.8,1), @(0.2,0.35,0.8,0.65)),
    @(@(0.5,0,0.5,1), @(0.2,0.35,0.8,0.65)),
    @(@(0.5,0,0.5,1)),
    @(@(0.3,0.15,0.6,0.4), @(0.6,0.4,0.3,0.65), @(0.7,0.85,0.4,0.6), @(0.4,0.6,0.7,0.35)),
    @(@(0.5,0,0.5,1), @(0.5,0,0.8,0.2), @(0.5,1,0.2,0.8)),
    @(@(0.5,0.35,0.5,1), @(0.5,0.35,0.15,0), @(0.5,0.35,0.85,0)),
    @(@(0.7,0,0.3,0.4), @(0.3,0.4,0.7,0.6), @(0.7,0.6,0.3,1)),
    @(@(0.5,0,0.5,1), @(0.5,0,0.2,0.3), @(0.5,0,0.8,0.3)),
    @(@(0.2,0,0.2,1), @(0.2,0,0.75,0.25), @(0.75,0.25,0.2,0.5), @(0.2,0.5,0.75,0.75), @(0.75,0.75,0.2,1)),
    @(@(0.15,0,0.15,1), @(0.85,0,0.85,1), @(0.15,0,0.85,0.5), @(0.85,0,0.15,0.5)),
    @(@(0.3,0,0.3,1), @(0.3,0,0.75,0.35)),
    @(@(0.15,0,0.15,1), @(0.85,0,0.85,1), @(0.15,0,0.85,1), @(0.85,0,0.15,1))
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
$run = $size - 2 * $Band - 8
$count = [math]::Floor($run / $step)
$start = $Band + 4 + ($run - ($count - 1) * $step - $rw) / 2
$mid = ($Band - $rh) / 2
$k = 3
for ($i = 0; $i -lt $count; $i++) {
    $x = $start + $i * $step
    DrawRune $runes[$k % $runes.Count] $x $mid $rw $rh; $k++
    DrawRune $runes[($k + 7) % $runes.Count] $x ($size - $Band + $mid) $rw $rh; $k++
}
$k = 13
for ($i = 0; $i -lt $count; $i++) {
    $y = $start + $i * $step
    DrawRune $runes[$k % $runes.Count] (($Band - $rw) / 2) $y $rw $rh; $k++
    DrawRune $runes[($k + 11) % $runes.Count] ($size - $Band + ($Band - $rw) / 2) $y $rw $rh; $k++
}

# ── corner knots ──────────────────────────────────────────────────────────────
foreach ($kx in @(($Band / 2), ($size - $Band / 2))) {
    foreach ($ky in @(($Band / 2), ($size - $Band / 2))) {
        $pts = [System.Drawing.PointF[]]@((P $kx ($ky - 7)), (P ($kx + 7) $ky), (P $kx ($ky + 7)), (P ($kx - 7) $ky))
        $kglow = New-Object System.Drawing.Pen (C 90 255 140 40), 4
        $g.DrawPolygon($kglow, $pts); $kglow.Dispose()
        $kfill = New-Object System.Drawing.SolidBrush (C 255 150 96 44)
        $g.FillPolygon($kfill, $pts); $kfill.Dispose()
        $kedge = New-Object System.Drawing.Pen (C 240 255 205 120), 1.4
        $g.DrawPolygon($kedge, $pts); $kedge.Dispose()
        $dot = New-Object System.Drawing.SolidBrush (C 255 255 235 170)
        $g.FillEllipse($dot, [single]($kx - 2), [single]($ky - 2), [single]4, [single]4); $dot.Dispose()
    }
}

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"saved $Out"
