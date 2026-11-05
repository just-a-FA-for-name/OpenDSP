$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('L"--parity"){std::wstring dir','L"--parity"){std::cout<<"parity-start\n";std::wstring dir')
$a=$a.Replace('fout<<buf;}return 0;}','fout<<buf;}std::cout<<"parity-done\n";return 0;}')
[IO.File]::WriteAllText($p,$a)
'patched'
