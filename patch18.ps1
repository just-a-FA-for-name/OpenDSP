$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\shared.h'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('target=active?pow(10,((c.enabled?c.preamp:0)+c.output)/20):1;gain=target;','target=active?pow(10,((c.enabled?c.preamp:0)+c.output)/20):1;gain=0;')
[IO.File]::WriteAllText($p,$a)
'patched'
