$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$old='std::ifstream fin(dir+L"\input.txt");std::ofstream fout(dir+L"\apo_out.txt");std::string line;double l,r;char buf[64];while(std::getline(fin,line)){auto sp=line.find(32);l=std::stod(line.substr(0,sp));r=std::stod(line.substr(sp+1));b.process(l,r);sprintf_s(buf,"%.17g %.17g\n",b.l,b.r);fout<<buf;}std::cout<<"parity-done\n";return 0;}'
$new='std::ifstream fin(dir+L"\input.txt");std::string line;double l,r,sum=0,mx=0,n=0;while(std::getline(fin,line)){auto sp=line.find(32);l=std::stod(line.substr(0,sp));r=std::stod(line.substr(sp+1));b.process(l,r);sum+=abs(b.l)+abs(b.r);mx=std::max(mx,std::max(abs(b.l),abs(b.r)));n++;}printf("APO parity n=%.0f sum=%.17g max=%.17g\n",n,sum,mx);return 0;}'
$a=$a.Replace($old,$new)
[IO.File]::WriteAllText($p,$a)
$p2='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\RackParity.cs'
$b=[IO.File]::ReadAllText($p2)
$b=$b.Replace('File.WriteAllLines(Path.Combine(dir,"managed_out.txt"),lines);','File.WriteAllLines(Path.Combine(dir,"managed_out.txt"),lines);double sum=0,mx=0;for(int i=0;i<n;i++){sum+=Math.Abs(proc.Left)+Math.Abs(proc.Right);}for(int i=0;i<n;i++){var parts=lines[i].Split(" ");double lv=Math.Abs(double.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture)),rv=Math.Abs(double.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture));mx=Math.Max(mx,Math.Max(lv,rv));}Console.WriteLine($"CSP parity n={n} sum={sum:R} max={mx:R}");')
[IO.File]::WriteAllText($p2,$b)
'patched both'
