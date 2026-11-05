$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('check(std::isfinite(b.l)&&abs(b.l)<10,"Native filter impulse unstable");','{auto msg=std::string("Native filter impulse unstable ")+std::to_string(rate)+"/"+std::to_string(type);check(std::isfinite(b.l)&&abs(b.l)<10,msg.c_str());}')
$a=$a.Replace('check(energy>0,"Native filter has no response");','{auto msg2=std::string("Native filter has no response ")+std::to_string(rate)+"/"+std::to_string(type);check(energy>0,msg2.c_str());}')
$a=$a.Replace('check(b.l==b.r,"Native stereo mismatch");','{auto msg3=std::string("Native stereo mismatch ")+std::to_string(rate)+"/"+std::to_string(type);check(b.l==b.r,msg3.c_str());}')
[IO.File]::WriteAllText($p,$a)
'patched'
