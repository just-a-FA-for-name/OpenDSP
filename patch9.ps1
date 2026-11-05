$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('double energy=0;for(int n=0;n<rate;n++){b.process(n==0?1:0,n==0?1:0);','double energy=0,mx=0;for(int n=0;n<rate;n++){b.process(n==0?1:0,n==0?1:0);mx=std::max(mx,abs(b.l));')
$a=$a.Replace('auto msg=std::string("Native filter impulse unstable ")+std::to_string(rate)+"/"+std::to_string(type);','auto msg=std::string("Native filter impulse unstable ")+std::to_string(rate)+"/"+std::to_string(type)+" max="+std::to_string(mx)+" v="+std::to_string(b.l);')
[IO.File]::WriteAllText($p,$a)
'patched'
