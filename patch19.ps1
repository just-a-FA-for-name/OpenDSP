$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('c.flags=31;','c.flags=29;')
[IO.File]::WriteAllText($p,$a)
'patched'
