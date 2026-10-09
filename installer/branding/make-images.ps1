# Regenerates the Inno Setup wizard images (24-bit BMP) from installer\branding\icon.png.
# Large: left panel of the Welcome/Finish pages. Small: header of the inner pages. 1x and 2x for high-DPI.
Add-Type -AssemblyName System.Drawing
$dir = $PSScriptRoot
$icon = [System.Drawing.Image]::FromFile((Join-Path $dir "icon.png"))
function New-Canvas([int]$w, [int]$h) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = "AntiAlias"; $g.InterpolationMode = "HighQualityBicubic"; $g.TextRenderingHint = "AntiAliasGridFit"
  $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
  $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(7, 21, 32)), ([System.Drawing.Color]::FromArgb(14, 39, 59)), 90
  $g.FillRectangle($brush, $rect)
  return @($bmp, $g)
}
function Save-Large([int]$scale, [string]$name) {
  $w = 164 * $scale; $h = 314 * $scale
  $bmp, $g = New-Canvas $w $h
  $size = 92 * $scale
  $g.DrawImage($icon, [int](($w - $size) / 2), [int](70 * $scale), $size, $size)
  $center = New-Object System.Drawing.StringFormat; $center.Alignment = "Center"
  $title = New-Object System.Drawing.Font "Segoe UI Semibold", (15 * $scale), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
  $sub = New-Object System.Drawing.Font "Segoe UI", (9.5 * $scale), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
  $g.DrawString("PROGNODE", $title, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 237, 244))), (New-Object System.Drawing.RectangleF 0, (178 * $scale), $w, (24 * $scale)), $center)
  $g.DrawString("Industrial Monitoring", $sub, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(22, 207, 187))), (New-Object System.Drawing.RectangleF 0, (202 * $scale), $w, (18 * $scale)), $center)
  $g.Dispose(); $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Bmp); $bmp.Dispose()
}
function Save-Small([int]$scale, [string]$name) {
  $w = 55 * $scale
  $bmp, $g = New-Canvas $w $w
  $pad = 7 * $scale
  $g.DrawImage($icon, $pad, $pad, $w - 2 * $pad, $w - 2 * $pad)
  $g.Dispose(); $bmp.Save((Join-Path $dir $name), [System.Drawing.Imaging.ImageFormat]::Bmp); $bmp.Dispose()
}
Save-Large 1 "wizard-large.bmp"; Save-Large 2 "wizard-large-200.bmp"
Save-Small 1 "wizard-small.bmp"; Save-Small 2 "wizard-small-200.bmp"
$icon.Dispose()
