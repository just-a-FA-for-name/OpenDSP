$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('std::ifstream fin(dir+L"/input.txt");std::string line;','std::ifstream fin(dir+L"/input.txt");std::ofstream fout(dir+L"/apo_out.txt");std::string line;')
$a=$a.Replace('sum+=abs(b.l)+abs(b.r);mx=std::max(mx,std::max(abs(b.l),abs(b.r)));n++;}','sum+=abs(b.l)+abs(b.r);mx=std::max(mx,std::max(abs(b.l),abs(b.r)));n++;char ob[64];sprintf_s(ob,"%.17g %.17g\n",b.l,b.r);fout<<ob;}')
[IO.File]::WriteAllText($p,$a)
'patched'
