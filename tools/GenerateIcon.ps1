param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\seraphyx.ico'),
    [string]$PreviewPath = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

function New-Color([int]$A, [int]$R, [int]$G, [int]$B) {
    return [System.Drawing.Color]::FromArgb($A, $R, $G, $B)
}

function New-Point([float]$X, [float]$Y) {
    return [System.Drawing.PointF]::new($X, $Y)
}

function Draw-DiamondStar([float]$X, [float]$Y, [float]$Radius, [System.Drawing.Brush]$Brush) {
    $points = [System.Drawing.PointF[]]@(
        (New-Point $X ($Y - $Radius)),
        (New-Point ($X + $Radius * 0.24) ($Y - $Radius * 0.24)),
        (New-Point ($X + $Radius) $Y),
        (New-Point ($X + $Radius * 0.24) ($Y + $Radius * 0.24)),
        (New-Point $X ($Y + $Radius)),
        (New-Point ($X - $Radius * 0.24) ($Y + $Radius * 0.24)),
        (New-Point ($X - $Radius) $Y),
        (New-Point ($X - $Radius * 0.24) ($Y - $Radius * 0.24))
    )
    $graphics.FillPolygon($Brush, $points)
}

try {
    $canvas = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
    $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $canvas,
        (New-Color 255 24 16 47),
        (New-Color 255 8 12 29),
        45.0
    )
    $graphics.FillRectangle($background, $canvas)
    $background.Dispose()

    $outer = [System.Drawing.PointF[]]@(
        (New-Point 52 10), (New-Point 204 10), (New-Point 246 52), (New-Point 246 204),
        (New-Point 204 246), (New-Point 52 246), (New-Point 10 204), (New-Point 10 52)
    )
    $inner = [System.Drawing.PointF[]]@(
        (New-Point 58 20), (New-Point 198 20), (New-Point 236 58), (New-Point 236 198),
        (New-Point 198 236), (New-Point 58 236), (New-Point 20 198), (New-Point 20 58)
    )
    $graphics.FillPolygon([System.Drawing.SolidBrush]::new((New-Color 255 14 14 36)), $outer)
    $graphics.DrawPolygon([System.Drawing.Pen]::new((New-Color 255 158 111 246), 5.0), $outer)
    $graphics.DrawPolygon([System.Drawing.Pen]::new((New-Color 170 92 220 255), 1.5), $inner)

    $softGlow = [System.Drawing.SolidBrush]::new((New-Color 28 154 109 255))
    $graphics.FillEllipse($softGlow, 59, 50, 138, 138)
    $softGlow.Dispose()
    $haloPen = [System.Drawing.Pen]::new((New-Color 195 194 149 255), 4.0)
    $graphics.DrawEllipse($haloPen, 65, 45, 126, 126)
    $haloPen.Dispose()

    $wingBrush = [System.Drawing.SolidBrush]::new((New-Color 210 190 173 255))
    $leftWing = [System.Drawing.PointF[]]@(
        (New-Point 112 111), (New-Point 80 83), (New-Point 87 111), (New-Point 56 96),
        (New-Point 77 126), (New-Point 50 124), (New-Point 76 146), (New-Point 64 166),
        (New-Point 105 144)
    )
    $rightWing = [System.Drawing.PointF[]]@(
        (New-Point 144 111), (New-Point 176 83), (New-Point 169 111), (New-Point 200 96),
        (New-Point 179 126), (New-Point 206 124), (New-Point 180 146), (New-Point 192 166),
        (New-Point 151 144)
    )
    $graphics.FillPolygon($wingBrush, $leftWing)
    $graphics.FillPolygon($wingBrush, $rightWing)
    $wingBrush.Dispose()

    $crystal = [System.Drawing.PointF[]]@(
        (New-Point 128 61), (New-Point 170 108), (New-Point 153 171),
        (New-Point 128 202), (New-Point 103 171), (New-Point 86 108)
    )
    $crystalBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.Rectangle]::new(86, 61, 84, 141),
        (New-Color 255 231 221 255),
        (New-Color 255 117 63 231),
        35.0
    )
    $graphics.FillPolygon($crystalBrush, $crystal)
    $crystalBrush.Dispose()

    $leftFacet = [System.Drawing.PointF[]]@(
        (New-Point 128 67), (New-Point 128 189), (New-Point 98 166), (New-Point 91 109)
    )
    $rightFacet = [System.Drawing.PointF[]]@(
        (New-Point 128 67), (New-Point 128 189), (New-Point 158 166), (New-Point 165 109)
    )
    $graphics.FillPolygon([System.Drawing.SolidBrush]::new((New-Color 175 255 255 255)), $leftFacet)
    $graphics.FillPolygon([System.Drawing.SolidBrush]::new((New-Color 95 98 211 255)), $rightFacet)

    $starBrush = [System.Drawing.SolidBrush]::new((New-Color 255 241 252 255))
    Draw-DiamondStar 128 50 15 $starBrush
    Draw-DiamondStar 56 63 6 $starBrush
    Draw-DiamondStar 200 186 6 $starBrush
    $starBrush.Dispose()

    $pngStream = [System.IO.MemoryStream]::new()
    $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    if ($PreviewPath) {
        $previewFile = [System.IO.Path]::GetFullPath($PreviewPath)
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($previewFile)) | Out-Null
        $bitmap.Save($previewFile, [System.Drawing.Imaging.ImageFormat]::Png)
    }

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $parentFolder = [System.IO.Path]::GetDirectoryName($resolvedOutput)
    [System.IO.Directory]::CreateDirectory($parentFolder) | Out-Null
    $fileStream = [System.IO.File]::Create($resolvedOutput)
    $writer = [System.IO.BinaryWriter]::new($fileStream)
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]1)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$pngBytes.Length)
    $writer.Write([UInt32]22)
    $writer.Write($pngBytes)
    $writer.Flush()
    $writer.Dispose()
    $fileStream.Dispose()
    $pngStream.Dispose()
}
finally {
    $graphics.Dispose()
    $bitmap.Dispose()
}

