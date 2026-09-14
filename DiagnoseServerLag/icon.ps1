# Makes icon.png (256x256): a carved stone frame with Elder Futhark runes, matching the other
# DeathMonger mods, wrapped around a latency trace with one spike in it - quiet, one bad second,
# quiet again, which is what the mod exists to explain.
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
# a little grain
$rnd = New-Object System.Random 7
for ($i = 0; $i -lt 260; $i++) {
    $x = $rnd.Next(0, $size); $y = $rnd.Next(0, $size)
    if ($x -ge $Band -and $x -lt $size - $Band -and $y -ge $Band -and $y -lt $size - $Band) { continue }
    $shade = $rnd.Next(0, 2) -eq 0
    $b = New-Object System.Drawing.SolidBrush ($(if ($shade) { C 70 0 0 0 } else { C 40 255 220 180 }))
    $g.FillRectangle($b, $x, $y, $rnd.Next(1, 4), 1)
    $b.Dispose()
}

# -- the latency trace, inside the frame --------------------------------------
$inner = $size - 2 * $Band
$slate = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point $Band, $Band), (New-Object System.Drawing.Point ($Band + $inner), ($Band + $inner)), (C 255 26 22 20), (C 255 14 12 12)
$g.FillRectangle($slate, $Band, $Band, $inner, $inner)
$slate.Dispose()

# faint grid, so the trace reads as a measurement rather than a decoration
$grid = New-Object System.Drawing.Pen (C 34 255 220 180), 1
for ($i = 1; $i -lt 4; $i++) {
    $gy = $Band + $inner * $i / 4.0
    $g.DrawLine($grid, [single]($Band + 10), [single]$gy, [single]($Band + $inner - 10), [single]$gy)
}
$grid.Dispose()

# The trace: quiet, one violent spike, then quiet again. Values are fractions of the
# inner box, y measured downward, so a small y is a tall spike.
$trace = @(
    @(0.00, 0.70), @(0.07, 0.68), @(0.14, 0.73), @(0.21, 0.67), @(0.28, 0.72), @(0.35, 0.69),
    @(0.42, 0.44), @(0.47, 0.12), @(0.53, 0.62), @(0.58, 0.34), @(0.64, 0.66),
    @(0.71, 0.70), @(0.78, 0.67), @(0.86, 0.72), @(0.93, 0.68), @(1.00, 0.70)
)
$padX = 14; $padY = 26
$x0 = $Band + $padX; $runW = $inner - 2 * $padX
$y0 = $Band + $padY; $runH = $inner - 2 * $padY

$pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
foreach ($t in $trace) {
    $pts.Add((New-Object System.Drawing.PointF ([single]($x0 + $t[0] * $runW)), ([single]($y0 + $t[1] * $runH))))
}
$arr = $pts.ToArray()

# two passes: a wide warm glow under a bright thin line, matching the runes
foreach ($pass in @(@(7.0, (C 70 255 120 40)), @(3.0, (C 255 255 190 95)))) {
    $pen = New-Object System.Drawing.Pen $pass[1], ([single]$pass[0])
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, $arr)
    $pen.Dispose()
}

# the apex of the spike, marked with the same diamond as the frame's corner knots
$peak = $arr[7]
$dpts = [System.Drawing.PointF[]]@(
    (New-Object System.Drawing.PointF ([single]$peak.X), ([single]($peak.Y - 11))),
    (New-Object System.Drawing.PointF ([single]($peak.X + 11)), ([single]$peak.Y)),
    (New-Object System.Drawing.PointF ([single]$peak.X), ([single]($peak.Y + 11))),
    (New-Object System.Drawing.PointF ([single]($peak.X - 11)), ([single]$peak.Y)))
$glow = New-Object System.Drawing.Pen (C 90 255 90 50), 5
$g.DrawPolygon($glow, $dpts); $glow.Dispose()
$fill = New-Object System.Drawing.SolidBrush (C 255 214 72 52)
$g.FillPolygon($fill, $dpts); $fill.Dispose()
$edge = New-Object System.Drawing.Pen (C 245 255 225 170), 1.6
$g.DrawPolygon($edge, $dpts); $edge.Dispose()

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
$k = 5
for ($i = 0; $i -lt $count; $i++) {
    $x = $start + $i * $step
    DrawRune $runes[$k % $runes.Count] $x $mid $rw $rh; $k++                                  # top
    DrawRune $runes[($k + 7) % $runes.Count] $x ($size - $Band + $mid) $rw $rh; $k++          # bottom
}
$k = 9
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
