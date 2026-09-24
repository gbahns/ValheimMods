# Makes icon.png (256x256): the carved stone frame with Elder Futhark runes shared by the other
# DeathMonger mods, wrapped around three nodes - one overloaded and hot, two cool - with the work
# visibly moving off the hot one onto the others. That is the whole mod in one picture.
# Usage: powershell -ExecutionPolicy Bypass -File icon.ps1
param(
    [string]$Out = (Join-Path $PSScriptRoot "icon.png"),
    [int]$Band = 22        # frame width in icon pixels
)
Add-Type -AssemblyName System.Drawing

function C($a, $r, $gg, $b) { [System.Drawing.Color]::FromArgb([int]$a, [int]$r, [int]$gg, [int]$b) }

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
$rnd = New-Object System.Random 7
for ($i = 0; $i -lt 260; $i++) {
    $x = $rnd.Next(0, $size); $y = $rnd.Next(0, $size)
    if ($x -ge $Band -and $x -lt $size - $Band -and $y -ge $Band -and $y -lt $size - $Band) { continue }
    $shade = $rnd.Next(0, 2) -eq 0
    $b = New-Object System.Drawing.SolidBrush ($(if ($shade) { C 70 0 0 0 } else { C 40 255 220 180 }))
    $g.FillRectangle($b, $x, $y, $rnd.Next(1, 4), 1)
    $b.Dispose()
}

# ── the slate the nodes sit on ────────────────────────────────────────────────
$inner = $size - 2 * $Band
$slate = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point $Band, $Band), (New-Object System.Drawing.Point ($Band + $inner), ($Band + $inner)), (C 255 26 22 20), (C 255 14 12 12)
$g.FillRectangle($slate, $Band, $Band, $inner, $inner)
$slate.Dispose()

# ── the three machines ────────────────────────────────────────────────────────
# One carrying too much, two with room. Placed left-to-right so the movement reads
# in the direction the eye already travels.
$srcX = 84.0; $srcY = 128.0; $srcR = 34.0
$dstA = @(178.0, 82.0, 23.0)
$dstB = @(178.0, 174.0, 23.0)

function Disc($cx, $cy, $r, $fill, $glowCol, $edgeCol) {
    $glow = New-Object System.Drawing.Pen $glowCol, 7
    $g.DrawEllipse($glow, [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
    $glow.Dispose()
    $b = New-Object System.Drawing.SolidBrush $fill
    $g.FillEllipse($b, [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
    $b.Dispose()
    $e = New-Object System.Drawing.Pen $edgeCol, 2.0
    $g.DrawEllipse($e, [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r))
    $e.Dispose()
}

# The arrows go under the discs so they emerge from beneath rather than crossing them.
function Arrow($x1, $y1, $x2, $y2) {
    foreach ($pass in @(@(8.0, (C 70 255 120 40)), @(3.4, (C 255 255 190 95)))) {
        $pen = New-Object System.Drawing.Pen $pass[1], ([single]$pass[0])
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        $g.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2)
        $pen.Dispose()
    }
    # head, pointing along the line
    $dx = $x2 - $x1; $dy = $y2 - $y1
    $len = [math]::Sqrt($dx * $dx + $dy * $dy)
    if ($len -lt 1) { return }
    $ux = $dx / $len; $uy = $dy / $len
    $px = -$uy; $py = $ux
    $h = 15.0; $w = 9.0
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([single]$x2), ([single]$y2)),
        (New-Object System.Drawing.PointF ([single]($x2 - $h * $ux + $w * $px)), ([single]($y2 - $h * $uy + $w * $py))),
        (New-Object System.Drawing.PointF ([single]($x2 - $h * $ux - $w * $px)), ([single]($y2 - $h * $uy - $w * $py))))
    $fill = New-Object System.Drawing.SolidBrush (C 255 255 205 120)
    $g.FillPolygon($fill, $pts); $fill.Dispose()
}

Arrow ($srcX + 26) ($srcY - 16) ($dstA[0] - 30) ($dstA[1] + 14)
Arrow ($srcX + 26) ($srcY + 16) ($dstB[0] - 30) ($dstB[1] - 14)

# the overloaded one: the same red the other mods use for a reading that is out of range
Disc $srcX $srcY $srcR (C 255 214 72 52) (C 110 255 90 50) (C 245 255 225 170)
# a heavier ring on it, so it reads as carrying more rather than merely being a different colour
$ring = New-Object System.Drawing.Pen (C 150 255 235 190), 3
$g.DrawEllipse($ring, [single]($srcX - $srcR + 9), [single]($srcY - $srcR + 9), [single](2 * ($srcR - 9)), [single](2 * ($srcR - 9)))
$ring.Dispose()

Disc $dstA[0] $dstA[1] $dstA[2] (C 255 150 96 44) (C 90 255 140 40) (C 240 255 205 120)
Disc $dstB[0] $dstB[1] $dstB[2] (C 255 150 96 44) (C 90 255 140 40) (C 240 255 205 120)

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
# A different starting rune from the sibling mods, so the frames are a family rather than copies.
$k = 2
for ($i = 0; $i -lt $count; $i++) {
    $x = $start + $i * $step
    DrawRune $runes[$k % $runes.Count] $x $mid $rw $rh; $k++                                  # top
    DrawRune $runes[($k + 7) % $runes.Count] $x ($size - $Band + $mid) $rw $rh; $k++          # bottom
}
$k = 13
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
