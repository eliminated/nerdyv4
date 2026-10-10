# Generates the Nerdy app icon ("Plan specs": nerd glasses with a clock lens and a check lens on an indigo tile)
# as a multi-size .ico plus PNG assets. Small sizes use a simplified drawing so they stay crisp.
# Usage: pwsh tools/make-icon.ps1
param([string]$OutDir = "$PSScriptRoot/../src/Nerdy.App/Assets")

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Hex([string]$h, [int]$a = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($h)
    [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B)
}

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

function New-Pen([System.Drawing.Color]$color, [single]$width) {
    $pen = New-Object System.Drawing.Pen $color, $width
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $pen
}

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $k = $size / 100.0                      # design space is 100 x 100
    $small = $size -le 24
    $detail = $size -ge 48
    function S([double]$v) { [single]($v * $k) }
    function W([double]$v, [double]$minPx) { [single][Math]::Max($v * $k, $minPx) }

    # Tile: indigo diagonal gradient with a soft top sheen.
    $inset = if ($small) { 0 } else { S 1 }
    $tile = New-RoundRect $inset $inset ($size - 1 - 2 * $inset) ($size - 1 - 2 * $inset) (S 23)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Hex '#7474EC'), (Hex '#4646C0'), 50.0
    $g.FillPath($grad, $tile)
    if ($detail) {
        $g.SetClip($tile)
        $sheenRect = New-Object System.Drawing.RectangleF 0, 0, $size, (S 52)
        $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush $sheenRect, (Hex '#FFFFFF' 40), (Hex '#FFFFFF' 0), 90.0
        $sheen.WrapMode = 'TileFlipX'
        $g.FillRectangle($sheen, 0, 0, $size, (S 51))
        $g.ResetClip()
        $g.DrawPath((New-Pen (Hex '#FFFFFF' 40) (W 1 1)), $tile)
    }

    $navy = Hex '#1B1B3A'
    $lensR = if ($small) { 19 } else { 17 }
    $lx = if ($small) { 29 } else { 31 }
    $rx = 100 - $lx
    $cy = 54
    $rim = W 6 1.6

    # Soft shadow under the glasses.
    if ($detail) {
        $shadow = New-Object System.Drawing.SolidBrush (Hex '#14143A' 60)
        foreach ($cx in $lx, $rx) { $g.FillEllipse($shadow, (S ($cx - $lensR)), (S ($cy - $lensR + 3)), (S (2 * $lensR)), (S (2 * $lensR))) }
    }

    # Temple arms and bridge.
    if ($detail) {
        $arm = New-Pen $navy (W 5 1.5)
        $g.DrawLine($arm, (S ($lx - $lensR - 1)), (S ($cy - 6)), (S 7), (S ($cy - 13)))
        $g.DrawLine($arm, (S ($rx + $lensR + 1)), (S ($cy - 6)), (S 93), (S ($cy - 13)))
    }
    $bridge = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bridge.AddBezier((S ($lx + $lensR - 2)), (S ($cy - 4)), (S 46), (S ($cy - 13)), (S 54), (S ($cy - 13)), (S ($rx - $lensR + 2)), (S ($cy - 4)))
    $g.DrawPath((New-Pen $navy (W 5 1.4)), $bridge)

    # Lenses.
    $white = New-Object System.Drawing.SolidBrush (Hex '#FFFFFF')
    $rimPen = New-Pen $navy $rim
    foreach ($cx in $lx, $rx) {
        $g.FillEllipse($white, (S ($cx - $lensR)), (S ($cy - $lensR)), (S (2 * $lensR)), (S (2 * $lensR)))
        $g.DrawEllipse($rimPen, (S ($cx - $lensR)), (S ($cy - $lensR)), (S (2 * $lensR)), (S (2 * $lensR)))
    }

    # Left lens: clock.
    $hand = New-Pen (Hex '#3B3BA8') (W 4 1.2)
    if ($detail) {
        $tick = New-Object System.Drawing.SolidBrush (Hex '#9C9CD8')
        foreach ($a in 0, 90, 180, 270) {
            $t = $a * [Math]::PI / 180
            $px = $lx + [Math]::Sin($t) * ($lensR - 6.5); $py = $cy - [Math]::Cos($t) * ($lensR - 6.5)
            $g.FillEllipse($tick, (S ($px - 1.5)), (S ($py - 1.5)), (S 3), (S 3))
        }
    }
    $g.DrawLine($hand, (S $lx), (S $cy), (S $lx), (S ($cy - ($lensR - 7))))
    $g.DrawLine($hand, (S $lx), (S $cy), (S ($lx + ($lensR - 9))), (S ($cy + 3)))
    if ($detail) { $g.FillEllipse((New-Object System.Drawing.SolidBrush (Hex '#3B3BA8')), (S ($lx - 2.5)), (S ($cy - 2.5)), (S 5), (S 5)) }

    # Right lens: check mark.
    $check = New-Pen (Hex '#1F9D74') (W 5.5 1.6)
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF (S ($rx - 8)), (S ($cy + 0.5))),
        (New-Object System.Drawing.PointF (S ($rx - 2.5)), (S ($cy + 6))),
        (New-Object System.Drawing.PointF (S ($rx + 8.5)), (S ($cy - 6.5)))
    )
    $g.DrawLines($check, $pts)

    $g.Dispose()
    $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) { $bmp.Save("$OutDir/NerdyLogo.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($s -eq 64) { $bmp.Save("$OutDir/NerdyLogo64.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($s -eq 32) { $bmp.Save("$OutDir/NerdyLogo32.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO container with PNG-compressed entries.
$fs = [System.IO.File]::Create("$OutDir/Nerdy.ico")
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Dispose(); $fs.Dispose()
Write-Output "Wrote $OutDir/Nerdy.ico ($($sizes -join ', ') px)"
