$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('Config p{};p.active=p.enabled=1;p.count=1;p.ceiling=-1;','Config p{};p.active=p.enabled=1;p.count=1;p.ceiling=-1;p.mLL=p.mRR=1;p.width=1;p.compRatio=1;p.compAttackMs=10;p.compReleaseMs=120;')
[IO.File]::WriteAllText($p,$a)
'patched'
