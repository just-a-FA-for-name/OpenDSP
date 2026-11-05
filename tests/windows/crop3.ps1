Add-Type -AssemblyName System.Drawing
$src=[System.Drawing.Bitmap]::FromFile('D:\AI workspace\OpenEQ\evidence\screen.png')
$rect=New-Object System.Drawing.Rectangle(1500,370,900,90)
$crop=$src.Clone($rect,$src.PixelFormat)
$crop.Save('D:\AI workspace\OpenEQ\evidence\status-crop.png')
$src.Dispose();$crop.Dispose()
'ok'
