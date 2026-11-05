Add-Type -AssemblyName System.Drawing
$icon=[System.Drawing.Icon]::ExtractAssociatedIcon('D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.exe')
$bmp=New-Object System.Drawing.Bitmap(64,64)
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode='HighQualityBicubic'
$g.DrawImage($icon.ToBitmap(),0,0,64,64)
$bmp.Save('D:\AI workspace\OpenEQ\evidence\exe-icon.png')
$g.Dispose();$bmp.Dispose()
'saved exe-icon.png'
if(Test-Path 'D:\AI workspace\OpenEQ\evidence\repair-done.txt'){Get-Content 'D:\AI workspace\OpenEQ\evidence\repair-done.txt'}else{'repair: still waiting (UAC?)'}
