$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('if(block>12)for(int n=0;n<512;n++)','if(block>50)for(int n=0;n<512;n++)')
[IO.File]::WriteAllText($p,$a)
'patched'
