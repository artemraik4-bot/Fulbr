Add-Type -AssemblyName System.Drawing

$outDir = 'C:\Users\user\Fulbr\src\Fulbr.App\Assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Render-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 1024.0
    $g.ScaleTransform($scale, $scale)

    $bgRect = New-Object System.Drawing.RectangleF(40, 40, 944, 944)
    $bgPath = New-RoundedRectPath 40 40 944 944 220
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF(1024, 1024)),
        [System.Drawing.ColorTranslator]::FromHtml('#0B1220'),
        [System.Drawing.ColorTranslator]::FromHtml('#12243A'))
    $g.FillPath($bgBrush, $bgPath)

    $borderPen = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#22304A'), 2)
    $g.DrawPath($borderPen, $bgPath)

    $pts = @(
        (New-Object System.Drawing.PointF(300, 270)),
        (New-Object System.Drawing.PointF(700, 270)),
        (New-Object System.Drawing.PointF(620, 380)),
        (New-Object System.Drawing.PointF(440, 380)),
        (New-Object System.Drawing.PointF(440, 470)),
        (New-Object System.Drawing.PointF(620, 470)),
        (New-Object System.Drawing.PointF(540, 580)),
        (New-Object System.Drawing.PointF(440, 580)),
        (New-Object System.Drawing.PointF(440, 760)),
        (New-Object System.Drawing.PointF(300, 760))
    )
    $markPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $markPath.AddPolygon($pts)
    $markBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(300, 270)),
        (New-Object System.Drawing.PointF(700, 760)),
        [System.Drawing.ColorTranslator]::FromHtml('#5EEAD4'),
        [System.Drawing.ColorTranslator]::FromHtml('#38BDF8'))
    $g.FillPath($markBrush, $markPath)

    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(217, 94, 234, 212))
    $g.FillEllipse($dotBrush, 682, 666, 68, 68)

    $bgBrush.Dispose(); $bgPath.Dispose(); $borderPen.Dispose(); $markPath.Dispose(); $markBrush.Dispose(); $dotBrush.Dispose()
    $g.Dispose()
    return $bmp
}

# Master PNG (1024)
$master = Render-Logo 1024
$master.Save((Join-Path $outDir 'Fulbr-1024.png'), [System.Drawing.Imaging.ImageFormat]::Png)

# PNG sizes
foreach ($s in 16, 24, 32, 48, 64, 128, 256) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($master, 0, 0, $s, $s)
    $g.Dispose()
    $bmp.Save((Join-Path $outDir ("Fulbr-{0}.png" -f $s)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
$master.Dispose()

# Build a PNG-compressed ICO with multiple sizes.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $outDir ("Fulbr-{0}.png" -f $s)))
    $frames += ,@($s, $bytes)
}

$icoPath = Join-Path $outDir 'Fulbr.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type: icon
$bw.Write([UInt16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $s = [int]$frame[0]
    $data = [byte[]]$frame[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)        # color count
    $bw.Write([byte]0)        # reserved
    $bw.Write([UInt16]1)      # planes
    $bw.Write([UInt16]32)     # bit count
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($frame in $frames) {
    $bw.Write([byte[]]$frame[1])
}
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Get-ChildItem $outDir | Select-Object Name, Length | Format-Table -AutoSize
