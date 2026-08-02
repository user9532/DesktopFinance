Add-Type -AssemblyName System.Drawing

function New-DIcon {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded-square dark red badge background
    $badgeColor = [System.Drawing.Color]::FromArgb(255, 178, 30, 30)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $radius = [Math]::Max(2, [int]($size * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush $badgeColor
    $g.FillPath($brush, $path)

    # Subtle darker-red border for definition at small sizes
    $borderColor = [System.Drawing.Color]::FromArgb(255, 122, 18, 18)
    $pen = New-Object System.Drawing.Pen -ArgumentList $borderColor, ([float]([Math]::Max(1, $size * 0.02)))
    $g.DrawPath($pen, $path)

    # Bold uppercase "D" in white, centered
    $fontSize = [float]($size * 0.62)
    $font = New-Object System.Drawing.Font "Segoe UI", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString("D", $font, $textBrush, [float]($size / 2), [float]($size / 2) + ($size * 0.02), $sf)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$bitmaps = @()
foreach ($s in $sizes) { $bitmaps += New-DIcon -size $s }

$outPath = "c:\Users\palac\OneDrive\Documents\AA-DEV\DesktopFinance\Assets\AppIcon.ico"
$fs = New-Object System.IO.FileStream $outPath, ([System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs

# ICO header
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type = icon
$bw.Write([UInt16]$bitmaps.Count)

$imageDataList = @()
foreach ($bmp in $bitmaps) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageDataList += ,$ms.ToArray()
}

$offset = 6 + (16 * $bitmaps.Count)
for ($i = 0; $i -lt $bitmaps.Count; $i++) {
    $bmp = $bitmaps[$i]
    $data = $imageDataList[$i]
    $w = 0
    $h = 0
    if ($bmp.Width -lt 256) { $w = $bmp.Width }
    if ($bmp.Height -lt 256) { $h = $bmp.Height }
    $bw.Write([Byte]$w)
    $bw.Write([Byte]$h)
    $bw.Write([Byte]0)     # color palette
    $bw.Write([Byte]0)     # reserved
    $bw.Write([UInt16]1)   # color planes
    $bw.Write([UInt16]32)  # bits per pixel
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($data in $imageDataList) {
    $bw.Write($data)
}

$bw.Flush()
$bw.Close()
$fs.Close()

foreach ($bmp in $bitmaps) { $bmp.Dispose() }

Write-Output "Icon written to $outPath"
