$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\DsdOutputChecks.cs'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('  Check(g[0]==0x00','  var g=new byte[24];DopFrame.Pack(0x8000,0x0001,0x05,g,0,3);'+[char]10+'  Check(g[0]==0x00')
[IO.File]::WriteAllText($p,$a)
'fixed'
