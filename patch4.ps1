$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\apo.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('banks[current].configure(next,rate);config=next;sequence=stamp;transition=0;','banks[current].configure(next,rate);config=next;sequence=stamp;transition=0;if(!next.active)old=-1;')
[IO.File]::WriteAllText($p,$a)
'patched'
