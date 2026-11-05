$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\apo.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('else{*match=other;other->AddRef();return S_FALSE;}','else{*match=other;other->AddRef();return S_OK;}')
[IO.File]::WriteAllText($p,$a)
'patched'
