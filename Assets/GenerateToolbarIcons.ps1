Add-Type -AssemblyName System.Drawing

$size = 24
$outDir = "c:\Users\palac\OneDrive\Documents\AA-DEV\DesktopFinance\Assets\Toolbar"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Save-Icon($bmp, $name) {
    $path = Join-Path $outDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

$ink = [System.Drawing.Color]::FromArgb(255, 40, 60, 90)
$accent = [System.Drawing.Color]::FromArgb(255, 30, 130, 76)
$pen2 = New-Object System.Drawing.Pen -ArgumentList $ink, 1.6
$penAccent = New-Object System.Drawing.Pen -ArgumentList $accent, 1.6
$brushInk = New-Object System.Drawing.SolidBrush $ink
$brushAccent = New-Object System.Drawing.SolidBrush $accent
$brushWhite = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

# 1. Import QFX Data — downward arrow into tray
$r = New-Canvas; $bmp, $g = $r
$g.DrawLine($pen2, 12, 3, 12, 15)
$g.DrawLine($pen2, 7, 10, 12, 15)
$g.DrawLine($pen2, 17, 10, 12, 15)
$g.DrawRectangle($pen2, 3, 18, 18, 4)
Save-Icon $bmp "ImportQfx"
$g.Dispose(); $bmp.Dispose()

# 2. New Account — document with plus
$r = New-Canvas; $bmp, $g = $r
$g.DrawRectangle($pen2, 4, 3, 12, 18)
$g.DrawLine($penAccent, 17, 14, 17, 22)
$g.DrawLine($penAccent, 13, 18, 21, 18)
Save-Icon $bmp "NewAccount"
$g.Dispose(); $bmp.Dispose()

# 3. Edit Account — document with pencil
$r = New-Canvas; $bmp, $g = $r
$g.DrawRectangle($pen2, 3, 3, 12, 18)
$g.DrawLine($pen2, 5, 8, 12, 8)
$g.DrawLine($pen2, 5, 12, 12, 12)
$g.FillPolygon($brushAccent, @(
  (New-Object System.Drawing.Point 14,21),
  (New-Object System.Drawing.Point 13,17),
  (New-Object System.Drawing.Point 20,10),
  (New-Object System.Drawing.Point 22,12)
))
Save-Icon $bmp "EditAccount"
$g.Dispose(); $bmp.Dispose()

# 4. New Category — tag with plus
$r = New-Canvas; $bmp, $g = $r
$g.FillPolygon($brushInk, @(
  (New-Object System.Drawing.Point 3,4),
  (New-Object System.Drawing.Point 12,4),
  (New-Object System.Drawing.Point 19,11),
  (New-Object System.Drawing.Point 12,18),
  (New-Object System.Drawing.Point 3,18)
))
$g.FillEllipse($brushWhite, 6, 9, 4, 4)
$g.DrawLine($penAccent, 18, 15, 18, 22)
$g.DrawLine($penAccent, 14, 18.5, 22, 18.5)
Save-Icon $bmp "NewCategory"
$g.Dispose(); $bmp.Dispose()

# 5. Edit Category — tag with pencil
$r = New-Canvas; $bmp, $g = $r
$g.FillPolygon($brushInk, @(
  (New-Object System.Drawing.Point 3,4),
  (New-Object System.Drawing.Point 12,4),
  (New-Object System.Drawing.Point 19,11),
  (New-Object System.Drawing.Point 12,18),
  (New-Object System.Drawing.Point 3,18)
))
$g.FillEllipse($brushWhite, 6, 9, 4, 4)
$g.FillPolygon($brushAccent, @(
  (New-Object System.Drawing.Point 14,21),
  (New-Object System.Drawing.Point 13,17),
  (New-Object System.Drawing.Point 20,10),
  (New-Object System.Drawing.Point 22,12)
))
Save-Icon $bmp "EditCategory"
$g.Dispose(); $bmp.Dispose()

# 6. Enter Budget — bar chart with checkmark
$r = New-Canvas; $bmp, $g = $r
$g.FillRectangle($brushInk, 4, 14, 4, 8)
$g.FillRectangle($brushInk, 10, 9, 4, 13)
$g.FillRectangle($brushInk, 16, 4, 4, 18)
$g.DrawLine((New-Object System.Drawing.Pen -ArgumentList $accent, 2.2), 3, 22, 22, 22)
Save-Icon $bmp "EnterBudget"
$g.Dispose(); $bmp.Dispose()

# 7. Add Securities — stock line with plus
$r = New-Canvas; $bmp, $g = $r
$pts2 = @(
  (New-Object System.Drawing.Point 2,17),
  (New-Object System.Drawing.Point 7,12),
  (New-Object System.Drawing.Point 11,15),
  (New-Object System.Drawing.Point 16,7)
)
$g.DrawLines($pen2, $pts2)
$g.DrawLine($pen2, (New-Object System.Drawing.Point 16,7), (New-Object System.Drawing.Point 16,11))
$g.DrawLine($pen2, (New-Object System.Drawing.Point 12,7), (New-Object System.Drawing.Point 16,7))
$g.DrawLine($penAccent, 20, 14, 20, 22)
$g.DrawLine($penAccent, 16, 18, 24, 18)
Save-Icon $bmp "AddSecurities"
$g.Dispose(); $bmp.Dispose()

# 8. Add Quotes — dollar sign with plus
$r = New-Canvas; $bmp, $g = $r
$font = New-Object System.Drawing.Font "Segoe UI", 15, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString("$", $font, $brushInk, 10, 12, $sf)
$g.DrawLine($penAccent, 19, 14, 19, 22)
$g.DrawLine($penAccent, 15, 18, 23, 18)
Save-Icon $bmp "AddQuotes"
$g.Dispose(); $bmp.Dispose()

# 9. Income and Expenses — up/down arrows with bar
$r = New-Canvas; $bmp, $g = $r
$g.DrawLine($penAccent, 7, 3, 7, 13)
$g.DrawLine($penAccent, 3, 7, 7, 3)
$g.DrawLine($penAccent, 11, 7, 7, 3)
$g.DrawLine($pen2, 17, 21, 17, 11)
$g.DrawLine($pen2, 13, 17, 17, 21)
$g.DrawLine($pen2, 21, 17, 17, 21)
Save-Icon $bmp "IncomeExpenses"
$g.Dispose(); $bmp.Dispose()

# 10. Budget vs Actual — two bars side by side with divider
$r = New-Canvas; $bmp, $g = $r
$g.FillRectangle($brushInk, 4, 10, 5, 12)
$g.FillRectangle($brushAccent, 11, 5, 5, 17)
$g.DrawLine($pen2, 2, 22, 22, 22)
Save-Icon $bmp "BudgetActual"
$g.Dispose(); $bmp.Dispose()

# 11. Net Worth Statement — scale/balance icon
$r = New-Canvas; $bmp, $g = $r
$g.DrawLine($pen2, 12, 3, 12, 20)
$g.DrawLine($pen2, 4, 8, 20, 8)
$g.DrawEllipse($pen2, 2, 8, 6, 6)
$g.DrawEllipse($pen2, 16, 8, 6, 6)
$g.DrawLine($penAccent, 6, 20, 18, 20)
Save-Icon $bmp "NetWorthStatement"
$g.Dispose(); $bmp.Dispose()

# 12. Cash Flow Statement — wavy line with arrow
$r = New-Canvas; $bmp, $g = $r
$pts3 = @(
  (New-Object System.Drawing.Point 2,16),
  (New-Object System.Drawing.Point 7,10),
  (New-Object System.Drawing.Point 11,15),
  (New-Object System.Drawing.Point 16,6),
  (New-Object System.Drawing.Point 21,11)
)
$g.DrawLines($pen2, $pts3)
$g.DrawLine($penAccent, 21, 11, 21, 4)
$g.DrawLine($penAccent, 16, 6, 21, 4)
$g.DrawLine($penAccent, 23, 6, 21, 4)
Save-Icon $bmp "CashFlowStatement"
$g.Dispose(); $bmp.Dispose()

# 13. Investment Performance — rising bars with trend line
$r = New-Canvas; $bmp, $g = $r
$g.FillRectangle($brushInk, 3, 16, 4, 6)
$g.FillRectangle($brushInk, 10, 11, 4, 11)
$g.FillRectangle($brushInk, 17, 5, 4, 17)
$g.DrawLine($penAccent, 3, 15, 21, 3)
$g.DrawLine($penAccent, 21, 3, 16, 3)
$g.DrawLine($penAccent, 21, 3, 21, 8)
Save-Icon $bmp "InvestmentPerformance"
$g.Dispose(); $bmp.Dispose()

# 14. Drilldown Category — funnel/magnifier into pie
$r = New-Canvas; $bmp, $g = $r
$g.DrawPie($pen2, 3, 3, 14, 14, 0, 90)
$g.DrawPie($penAccent, 3, 3, 14, 14, 90, 130)
$g.DrawPie($pen2, 3, 3, 14, 14, 220, 140)
$g.DrawEllipse($pen2, 14, 14, 7, 7)
$g.DrawLine($pen2, 19, 19, 23, 23)
Save-Icon $bmp "DrilldownCategory"
$g.Dispose(); $bmp.Dispose()

Write-Output "Toolbar icons written to $outDir"
