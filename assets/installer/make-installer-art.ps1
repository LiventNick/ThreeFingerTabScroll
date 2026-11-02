# Regenerates the install wizard artwork in this folder:
#   wizard.bmp / wizard-2x.bmp              - side panel on the Welcome and Finish pages
#   wizard-small.bmp / wizard-small-2x.bmp  - small icon at the top right of the other pages
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File assets\installer\make-installer-art.ps1
Add-Type -AssemblyName System.Drawing

$blue = [System.Drawing.Color]::FromArgb(26, 115, 232)
$white = [System.Drawing.Color]::White

# Draws the app's mark (rounded square with a double arrow) at (x, y) with the given size.
function Draw-Mark([System.Drawing.Graphics]$g, [float]$x, [float]$y, [float]$s, [System.Drawing.Color]$back, [System.Drawing.Color]$fore) {
    $r = $s * 0.22; $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $s - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $s - $d, $y + $s - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $back), $path)

    $mid = $y + $s / 2; $left = $x + $s * 0.18; $right = $x + $s * 0.82; $head = $s * 0.22
    $pen = New-Object System.Drawing.Pen $fore, ($s * 0.11)
    $g.DrawLine($pen, $left + $head * 0.6, $mid, $right - $head * 0.6, $mid)
    $brush = New-Object System.Drawing.SolidBrush $fore
    $g.FillPolygon($brush, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $left, $mid),
        (New-Object System.Drawing.PointF ($left + $head), ($mid - $head)),
        (New-Object System.Drawing.PointF ($left + $head), ($mid + $head))))
    $g.FillPolygon($brush, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF $right, $mid),
        (New-Object System.Drawing.PointF ($right - $head), ($mid - $head)),
        (New-Object System.Drawing.PointF ($right - $head), ($mid + $head))))
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    return $bmp, $g
}

function Save-Bmp([System.Drawing.Bitmap]$bmp, [string]$name) {
    $file = Join-Path $PSScriptRoot $name
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    "Wrote $file"
}

# Side panel: blue with the mark and the app name. Base size 164x314, plus a 2x version for high-DPI screens.
foreach ($scale in 1, 2) {
    $bmp, $g = New-Canvas (164 * $scale) (314 * $scale)
    $g.Clear($blue)
    $mark = 80 * $scale
    Draw-Mark $g ((164 * $scale - $mark) / 2) (80 * $scale) $mark $white $blue
    $font = New-Object System.Drawing.Font 'Segoe UI Semibold', (14 * $scale), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = 'Center'
    $g.DrawString("Three-Finger`nTab Scroll", $font, (New-Object System.Drawing.SolidBrush $white),
        (New-Object System.Drawing.RectangleF 0, (180 * $scale), (164 * $scale), (60 * $scale)), $format)
    $g.Dispose()
    Save-Bmp $bmp ($(if ($scale -eq 1) { 'wizard.bmp' } else { 'wizard-2x.bmp' }))
}

# Small icon: the mark on white. Base size 55x55, plus 2x.
foreach ($scale in 1, 2) {
    $size = 55 * $scale
    $bmp, $g = New-Canvas $size $size
    $g.Clear($white)
    Draw-Mark $g (4 * $scale) (4 * $scale) (47 * $scale) $blue $white
    $g.Dispose()
    Save-Bmp $bmp ($(if ($scale -eq 1) { 'wizard-small.bmp' } else { 'wizard-small-2x.bmp' }))
}
