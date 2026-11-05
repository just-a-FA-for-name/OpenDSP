$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('" max="+std::to_string(mx)+" v="+std::to_string(b.l);','" max="+std::to_string(mx)+" v="+std::to_string(b.l)+" coefs="+std::to_string(b.left[0].b0)+","+std::to_string(b.left[0].b1)+","+std::to_string(b.left[0].b2)+","+std::to_string(b.left[0].a1)+","+std::to_string(b.left[0].a2)+" nl="+std::to_string(b.nl)+" gain="+std::to_string(b.gain)+" lg="+std::to_string(b.lg)+" m="+std::to_string(b.mLL)+" w="+std::to_string(b.width);')
[IO.File]::WriteAllText($p,$a)
'patched'
