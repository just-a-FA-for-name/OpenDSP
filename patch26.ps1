$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\NativeApoChecks.cs'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('view.ReadDouble(1368),view.ReadDouble(1376),view.ReadDouble(1384),view.ReadDouble(1392)','view.ReadDouble(1456),view.ReadDouble(1464),view.ReadDouble(1472),view.ReadDouble(1480)')
[IO.File]::WriteAllText($p,$a)
'offsets fixed'
