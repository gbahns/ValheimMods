param([string]$Out = "C:\Users\greg\source\repos\ValheimMods\TheGreatestPortal\icon.png")
Add-Type -AssemblyName System.Drawing

$W = 256
$bmp = New-Object System.Drawing.Bitmap $W, $W
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.CompositingQuality = 'HighQuality'
$g.InterpolationMode = 'HighQualityBicubic'
$g.PixelOffsetMode = 'HighQuality'
$rnd = New-Object System.Random 11

function C($a, $r, $gg, $b) { [System.Drawing.Color]::FromArgb([int]$a, [int]$r, [int]$gg, [int]$b) }
function PF($x, $y) { New-Object System.Drawing.PointF ([single]$x), ([single]$y) }

# Radial glow: colours listed from the boundary (position 0) to the centre (position 1).
function Glow($cx, $cy, $rx, $ry, $colors, $positions) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse([single]($cx - $rx), [single]($cy - $ry), [single](2 * $rx), [single](2 * $ry))
    $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $pgb.CenterPoint = (PF $cx $cy)
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend
    $blend.Colors = [System.Drawing.Color[]]$colors
    $blend.Positions = [single[]]$positions
    $pgb.InterpolationColors = $blend
    $g.FillPath($pgb, $path)
    $pgb.Dispose(); $path.Dispose()
}

# A slightly curved streak from (x0,y0) outward along angle $ang for $len pixels.
function Streak($x0, $y0, $ang, $len, $width, $color, $bend) {
    $x1 = $x0 + [Math]::Cos($ang) * $len; $y1 = $y0 + [Math]::Sin($ang) * $len
    $mx = ($x0 + $x1) / 2 + [Math]::Cos($ang + [Math]::PI / 2) * $bend
    $my = ($y0 + $y1) / 2 + [Math]::Sin($ang + [Math]::PI / 2) * $bend
    $pen = New-Object System.Drawing.Pen $color, ([single]$width)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawCurve($pen, [System.Drawing.PointF[]]@((PF $x0 $y0), (PF $mx $my), (PF $x1 $y1)), [single]0.6)
    $pen.Dispose()
}

function Square($x, $y, $size, $color) {
    $b = New-Object System.Drawing.SolidBrush $color
    $g.FillRectangle($b, [single]$x, [single]$y, [single]$size, [single]$size)
    $b.Dispose()
}

# ── the ring's shape: an irregular nine-sided band ────────────────────────────
$cx = 128; $cy = 128
$n = 9
$outer = New-Object System.Collections.Generic.List[System.Drawing.PointF]
$inner = New-Object System.Collections.Generic.List[System.Drawing.PointF]
for ($i = 0; $i -lt $n; $i++) {
    $a = ($i * 360 / $n - 90 + ($rnd.NextDouble() - 0.5) * 14) * [Math]::PI / 180
    $ro = 86 + ($rnd.NextDouble() - 0.5) * 12
    $ri = $ro - 22 - ($rnd.NextDouble() * 6)
    $outer.Add((PF ($cx + $ro * [Math]::Cos($a)) ($cy + $ro * [Math]::Sin($a))))
    $inner.Add((PF ($cx + $ri * [Math]::Cos($a)) ($cy + $ri * [Math]::Sin($a))))
}
$ring = New-Object System.Drawing.Drawing2D.GraphicsPath ([System.Drawing.Drawing2D.FillMode]::Alternate)
$ring.AddPolygon($outer.ToArray())
$ring.AddPolygon($inner.ToArray())
$hole = New-Object System.Drawing.Drawing2D.GraphicsPath
$hole.AddPolygon($inner.ToArray())
$disc = New-Object System.Drawing.Drawing2D.GraphicsPath
$disc.AddPolygon($outer.ToArray())

# ── smoky, ember-lit background ───────────────────────────────────────────────
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $W), (C 255 98 38 18), (C 255 40 14 9)
$g.FillRectangle($bg, 0, 0, $W, $W)
$bg.Dispose()
# drifting haze blobs
for ($i = 0; $i -lt 12; $i++) {
    Glow ($rnd.Next(0, 256)) ($rnd.Next(0, 256)) ($rnd.Next(40, 100)) ($rnd.Next(30, 80)) @((C 0 220 100 40), (C 70 235 125 55)) @(0, 1)
}

# the big glow: strongest on the band itself (radius ~64-88 of a 150 glow), fading both ways
Glow $cx $cy 150 150 @((C 40 255 120 30), (C 120 255 140 45), (C 225 255 175 70), (C 245 255 200 105), (C 170 245 130 55), (C 50 170 55 30), (C 0 90 30 30)) @(0, 0.25, 0.4, 0.5, 0.6, 0.72, 1)

# ── the dark interior ─────────────────────────────────────────────────────────
$g.SetClip($hole)
$dark = New-Object System.Drawing.SolidBrush (C 255 42 24 30)
$g.FillRectangle($dark, 0, 0, $W, $W)
$dark.Dispose()
# smoke: soft dark red and violet clouds
for ($i = 0; $i -lt 14; $i++) {
    $col = if ($i % 3 -eq 0) { C 110 70 30 60 } elseif ($i % 3 -eq 1) { C 100 90 40 34 } else { C 90 50 24 44 }
    Glow ($cx + $rnd.Next(-50, 51)) ($cy + $rnd.Next(-50, 51)) ($rnd.Next(18, 48)) ($rnd.Next(14, 40)) @((C 0 0 0 0), $col) @(0, 1)
}
# fire light bleeding in from the band
Glow $cx $cy 72 72 @((C 190 255 150 50), (C 110 255 120 40), (C 0 200 80 30), (C 0 0 0 0)) @(0, 0.18, 0.45, 1)
# inward streaks of haze
for ($i = 0; $i -lt 34; $i++) {
    $a = $rnd.NextDouble() * 2 * [Math]::PI
    $r0 = 66
    Streak ($cx + [Math]::Cos($a) * $r0) ($cy + [Math]::Sin($a) * $r0) ($a + [Math]::PI) (6 + $rnd.NextDouble() * 26) (2 + $rnd.NextDouble() * 7) (C (30 + $rnd.Next(0, 70)) 255 (120 + $rnd.Next(0, 80)) (40 + $rnd.Next(0, 50))) (($rnd.NextDouble() - 0.5) * 8)
}
$g.ResetClip()

# ── flame haze radiating outward: broad soft tongues, then a few sharper ones ──
for ($i = 0; $i -lt 60; $i++) {
    $a = $rnd.NextDouble() * 2 * [Math]::PI
    $r0 = 80 + $rnd.NextDouble() * 10
    $len = 20 + [Math]::Pow($rnd.NextDouble(), 1.5) * 80
    $w = 5 + $rnd.NextDouble() * 12
    $al = 18 + [int](($rnd.NextDouble()) * 45)
    $col = switch ($rnd.Next(0, 3)) { 0 { C $al 255 210 110 } 1 { C $al 255 150 60 } default { C $al 255 235 190 } }
    Streak ($cx + [Math]::Cos($a) * $r0) ($cy + [Math]::Sin($a) * $r0) $a $len $w $col (($rnd.NextDouble() - 0.5) * 14)
}
for ($i = 0; $i -lt 30; $i++) {
    $a = $rnd.NextDouble() * 2 * [Math]::PI
    $r0 = 84 + $rnd.NextDouble() * 8
    $len = 14 + [Math]::Pow($rnd.NextDouble(), 2) * 60
    $w = 2 + $rnd.NextDouble() * 4
    $al = 45 + [int](($rnd.NextDouble()) * 90)
    $col = switch ($rnd.Next(0, 2)) { 0 { C $al 255 225 130 } default { C $al 255 245 210 } }
    Streak ($cx + [Math]::Cos($a) * $r0) ($cy + [Math]::Sin($a) * $r0) $a $len $w $col (($rnd.NextDouble() - 0.5) * 10)
}
# thin bright arcs, like the sparks streaking off the ring
for ($i = 0; $i -lt 10; $i++) {
    $a = $rnd.NextDouble() * 2 * [Math]::PI
    $r0 = 78 + $rnd.NextDouble() * 24
    Streak ($cx + [Math]::Cos($a) * $r0) ($cy + [Math]::Sin($a) * $r0) ($a + ($rnd.NextDouble() - 0.5) * 0.8) (30 + $rnd.NextDouble() * 70) 1.3 (C 210 255 235 150) (($rnd.NextDouble() - 0.5) * 24)
}

# ── the band of glowing lava stone, block by block ────────────────────────────
$base = New-Object System.Drawing.SolidBrush (C 255 150 62 26)
$g.FillPath($base, $ring)
$base.Dispose()
$cell = 7
$palette = @(
    @(0.34, (C 255 232 128 40)),   # orange
    @(0.26, (C 255 248 182 72)),   # gold
    @(0.10, (C 255 255 228 160)),  # near white
    @(0.18, (C 255 168 70 26)),    # ember
    @(0.12, (C 255 112 44 20))     # dark lava
)
# the blocks tile the band edge to edge (clipped to it), so it reads as one glowing wall
$g.SetClip($ring)
for ($y = $cy - 100; $y -lt $cy + 100; $y += $cell) {
    for ($x = $cx - 100; $x -lt $cx + 100; $x += $cell) {
        $px = $x + $cell / 2; $py = $y + $cell / 2
        if (-not $disc.IsVisible([single]$px, [single]$py)) { continue }
        if ($hole.IsVisible([single]$px, [single]$py) -and $hole.IsVisible([single]($px - $cell), [single]$py) -and $hole.IsVisible([single]($px + $cell), [single]$py) -and $hole.IsVisible([single]$px, [single]($py - $cell)) -and $hole.IsVisible([single]$px, [single]($py + $cell))) { continue }
        $pick = $rnd.NextDouble(); $acc = 0; $col = $palette[0][1]
        foreach ($p in $palette) { $acc += $p[0]; if ($pick -le $acc) { $col = $p[1]; break } }
        Square $x $y $cell $col
    }
}
# molten light over the blocks: brightest along both edges of the band
Glow $cx $cy 100 100 @((C 0 255 150 40), (C 120 255 200 100), (C 170 255 220 130), (C 60 255 160 50), (C 150 255 190 90), (C 0 255 140 40), (C 0 255 140 40)) @(0, 0.1, 0.16, 0.24, 0.34, 0.5, 1)
$g.ResetClip()
# a thin bright lip on the outer edge
$lip = New-Object System.Drawing.Pen (C 150 255 235 170), ([single]2.5)
$g.DrawPolygon($lip, $outer.ToArray())
$lip.Dispose()

# ── floating sparks: square, like the game's particles ────────────────────────
for ($i = 0; $i -lt 48; $i++) {
    $a = $rnd.NextDouble() * 2 * [Math]::PI
    $r = [Math]::Sqrt($rnd.NextDouble()) * 60
    $x = $cx + [Math]::Cos($a) * $r; $y = $cy + [Math]::Sin($a) * $r
    $s = 2 + [Math]::Pow($rnd.NextDouble(), 2) * 5
    $al = if ($s -gt 4.5) { 90 + $rnd.Next(0, 70) } else { 130 + $rnd.Next(0, 126) }
    $col = switch ($rnd.Next(0, 6)) { 0 { C $al 255 170 80 } 1 { C $al 255 235 140 } 2 { C $al 255 235 140 } default { C $al 255 250 235 } }
    Square ($x - $s / 2) ($y - $s / 2) $s $col
}
for ($i = 0; $i -lt 30; $i++) {
    $x = $rnd.Next(4, 252); $y = $rnd.Next(4, 252)
    if ($hole.IsVisible([single]$x, [single]$y)) { continue }
    $s = 2 + $rnd.NextDouble() * 5
    Square $x $y $s (C (100 + $rnd.Next(0, 150)) 255 (200 + $rnd.Next(0, 55)) (120 + $rnd.Next(0, 100)))
}

# ── scorched rock at the foot of the portal ───────────────────────────────────
$rock = New-Object System.Drawing.SolidBrush (C 255 46 22 16)
$g.FillPolygon($rock, [System.Drawing.PointF[]]@((PF 118 256), (PF 150 226), (PF 214 214), (PF 256 202), (PF 256 256)))
$g.FillPolygon($rock, [System.Drawing.PointF[]]@((PF 0 256), (PF 0 238), (PF 70 244), (PF 124 256)))
$rock.Dispose()
Glow 200 226 70 26 @((C 0 255 120 40), (C 110 255 150 60)) @(0, 1)

# ── frame border ──────────────────────────────────────────────────────────────
$border = New-Object System.Drawing.Pen (C 255 30 12 10), ([single]10)
$g.DrawRectangle($border, 0, 0, $W, $W)
$border.Dispose()
$innerLine = New-Object System.Drawing.Pen (C 140 220 110 50), ([single]1.5)
$g.DrawRectangle($innerLine, 5, 5, $W - 11, $W - 11)
$innerLine.Dispose()

$ring.Dispose(); $hole.Dispose(); $disc.Dispose()
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"saved $Out"
