# Regenerates assets\app.ico (the same blue arrow as the tray icon) at several sizes.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File assets\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 48, 64, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded blue square
    $r = $s * 0.22; $d = $r * 2; $w = $s - 1
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0.5, 0.5, $d, $d, 180, 90)
    $path.AddArc($w - $d + 0.5, 0.5, $d, $d, 270, 90)
    $path.AddArc($w - $d + 0.5, $w - $d + 0.5, $d, $d, 0, 90)
    $path.AddArc(0.5, $w - $d + 0.5, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(26, 115, 232))), $path)

    # White double-headed arrow
    $mid = $s / 2; $left = $s * 0.18; $right = $s * 0.82; $head = $s * 0.22
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, $s * 0.11))
    $g.DrawLine($pen, $left + $head * 0.6, $mid, $right - $head * 0.6, $mid)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPolygon($white, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $left, $mid),
        (New-Object System.Drawing.PointF ($left + $head), ($mid - $head)),
        (New-Object System.Drawing.PointF ($left + $head), ($mid + $head))))
    $g.FillPolygon($white, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $right, $mid),
        (New-Object System.Drawing.PointF ($right - $head), ($mid - $head)),
        (New-Object System.Drawing.PointF ($right - $head), ($mid + $head))))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO file: header, one directory entry per size, then the PNG images.
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'app.ico'), $out.ToArray())
"Wrote $(Join-Path $PSScriptRoot 'app.ico')"
