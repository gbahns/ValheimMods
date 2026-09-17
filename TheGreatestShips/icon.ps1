# Makes icon.png (256x256): a painted longship -- blue sail, red-striped hull -- at dusk, inside
# the same carved stone frame with Elder Futhark runes that TheGreatestPortal's icon uses.
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
$rnd = New-Object System.Random 3
for ($i = 0; $i -lt 260; $i++) {
    $x = $rnd.Next(0, $size); $y = $rnd.Next(0, $size)
    if ($x -ge $Band -and $x -lt $size - $Band -and $y -ge $Band -and $y -lt $size - $Band) { continue }
    $shade = $rnd.Next(0, 2) -eq 0
    $b = New-Object System.Drawing.SolidBrush ($(if ($shade) { C 70 0 0 0 } else { C 40 255 220 180 }))
    $g.FillRectangle($b, $x, $y, $rnd.Next(1, 4), 1)
    $b.Dispose()
}

# ── the ship, inside the frame ────────────────────────────────────────────────
$inner = $size - 2 * $Band
$g.SetClip((New-Object System.Drawing.Rectangle $Band, $Band, $inner, $inner))
function P([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]($Band + $x * $inner)), ([single]($Band + $y * $inner)) }

# dusk sky and sea
$sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 0), (P 0 0.62), (C 255 38 52 88), (C 255 214 150 96)
$g.FillRectangle($sky, $Band, $Band, $inner, [int]($inner * 0.62) + 1); $sky.Dispose()
$sun = New-Object System.Drawing.SolidBrush (C 200 255 214 140)
$g.FillEllipse($sun, [single]($Band + 0.68 * $inner), [single]($Band + 0.44 * $inner), [single](0.2 * $inner), [single](0.2 * $inner)); $sun.Dispose()
$sea = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0 0.6), (P 0 1), (C 255 36 70 92), (C 255 12 26 40)
$g.FillRectangle($sea, $Band, [int]($Band + $inner * 0.6), $inner, [int]($inner * 0.4) + 1); $sea.Dispose()
$glint = New-Object System.Drawing.Pen (C 110 255 210 150), 1.5
foreach ($w in @(@(0.62,0.66,0.9), @(0.7,0.72,0.86), @(0.08,0.74,0.3), @(0.55,0.82,0.8), @(0.12,0.9,0.4))) {
    $g.DrawLine($glint, (P $w[0] $w[1]), (P $w[2] $w[1]))
}
$glint.Dispose()

# mast and yard
$wood = New-Object System.Drawing.Pen (C 255 70 46 28), 4
$g.DrawLine($wood, (P 0.5 0.1), (P 0.5 0.66))
$wood.Width = 3
$g.DrawLine($wood, (P 0.26 0.16), (P 0.74 0.16))
$wood.Dispose()

# sail: blue with a lighter center stripe, bellied at the foot
$sailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$sailPath.AddLine((P 0.27 0.17), (P 0.73 0.17))
$sailPath.AddBezier((P 0.73 0.17), (P 0.76 0.35), (P 0.74 0.5), (P 0.71 0.56))
$sailPath.AddBezier((P 0.71 0.56), (P 0.6 0.6), (P 0.4 0.6), (P 0.29 0.56))
$sailPath.AddBezier((P 0.29 0.56), (P 0.26 0.5), (P 0.24 0.35), (P 0.27 0.17))
$sailBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (P 0.25 0), (P 0.75 0), (C 255 60 90 170), (C 255 110 150 225)
$g.FillPath($sailBrush, $sailPath); $sailBrush.Dispose()
$g.SetClip($sailPath, [System.Drawing.Drawing2D.CombineMode]::Intersect)
$stripe = New-Object System.Drawing.SolidBrush (C 150 215 230 255)
$g.FillRectangle($stripe, [single]($Band + 0.44 * $inner), [single]$Band, [single](0.12 * $inner), [single]$inner); $stripe.Dispose()
$g.SetClip((New-Object System.Drawing.Rectangle $Band, $Band, $inner, $inner))
$sailEdge = New-Object System.Drawing.Pen (C 255 30 44 90), 1.6
$g.DrawPath($sailEdge, $sailPath); $sailEdge.Dispose(); $sailPath.Dispose()

# hull: a long curve with a dragon-neck prow and a curled stern
$hull = New-Object System.Drawing.Drawing2D.GraphicsPath
$hull.AddBezier((P 0.04 0.52), (P 0.1 0.6), (P 0.1 0.66), (P 0.2 0.7))
$hull.AddBezier((P 0.2 0.7), (P 0.4 0.78), (P 0.6 0.78), (P 0.8 0.7))
$hull.AddBezier((P 0.8 0.7), (P 0.9 0.66), (P 0.92 0.56), (P 0.9 0.44))
$hull.AddBezier((P 0.9 0.44), (P 0.94 0.4), (P 0.98 0.42), (P 0.96 0.47))
$hull.AddBezier((P 0.96 0.47), (P 0.93 0.5), (P 0.93 0.58), (P 0.84 0.63))
$hull.AddLine((P 0.84 0.63), (P 0.14 0.63))
$hull.AddBezier((P 0.14 0.63), (P 0.08 0.6), (P 0.06 0.56), (P 0.06 0.5))
$hull.AddBezier((P 0.06 0.5), (P 0.03 0.47), (P 0.02 0.5), (P 0.04 0.52))
$hull.CloseFigure()
$hullBrush = New-Object System.Drawing.SolidBrush (C 255 120 78 46)
$g.FillPath($hullBrush, $hull); $hullBrush.Dispose()
# painted red strakes
$g.SetClip($hull, [System.Drawing.Drawing2D.CombineMode]::Intersect)
$red = New-Object System.Drawing.SolidBrush (C 255 178 62 50)
foreach ($y in @(0.635, 0.675, 0.715, 0.755)) {
    $g.FillRectangle($red, [single]$Band, [single]($Band + $y * $inner), [single]$inner, [single](0.02 * $inner))
}
$red.Dispose()
$g.SetClip((New-Object System.Drawing.Rectangle $Band, $Band, $inner, $inner))
$hullEdge = New-Object System.Drawing.Pen (C 255 40 24 14), 1.8
$g.DrawPath($hullEdge, $hull); $hullEdge.Dispose(); $hull.Dispose()
# shields along the rail
foreach ($i in 0..5) {
    $x = 0.2 + $i * 0.11
    $shield = New-Object System.Drawing.SolidBrush ($(if ($i % 2) { C 255 214 190 120 } else { C 255 70 104 178 }))
    $g.FillEllipse($shield, [single]($Band + ($x - 0.035) * $inner), [single]($Band + 0.595 * $inner), [single](0.07 * $inner), [single](0.07 * $inner))
    $shield.Dispose()
    $boss = New-Object System.Drawing.SolidBrush (C 255 60 44 30)
    $g.FillEllipse($boss, [single]($Band + ($x - 0.01) * $inner), [single]($Band + 0.62 * $inner), [single](0.02 * $inner), [single](0.02 * $inner))
    $boss.Dispose()
}
# wake
$foam = New-Object System.Drawing.Pen (C 170 230 240 250), 2
$g.DrawBezier($foam, (P 0.9 0.72), (P 0.8 0.8), (P 0.6 0.82), (P 0.42 0.84))
$g.DrawBezier($foam, (P 0.12 0.72), (P 0.08 0.76), (P 0.04 0.78), (P 0.0 0.79))
$foam.Dispose()
$g.ResetClip()

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
