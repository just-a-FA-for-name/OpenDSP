$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$block='int wmain(int argc,wchar_t** argv){if(argc>1&&std::wstring(argv[1])==L"--parity"){std::wstring dir=argc>2?argv[2]:L".";Config c{};c.active=c.enabled=1;c.count=3;c.flags=31;c.preamp=-3;c.output=-2;c.balance=.2;c.delayL=2;c.delayR=3;c.ceiling=-1;c.compThreshold=-18;c.compRatio=4;c.compAttackMs=10;c.compReleaseMs=120;c.compKnee=6;c.compMakeup=2;c.mLL=1.1;c.mLR=.2;c.mRL=-.1;c.mRR=.9;c.width=1.3;c.filters[0]={0,1,1,0,1000,-6,2};c.filters[1]={2,1,0,0,8000,4,.707};c.filters[2]={1,1,0,0,60,3,.707};Bank b;b.allocate(48000);b.configure(c,48000);std::ifstream fin(dir+L"\input.txt");std::ofstream fout(dir+L"\apo_out.txt");std::string line;double l,r;char buf[64];while(std::getline(fin,line)){auto sp=line.find(32);l=std::stod(line.substr(0,sp));r=std::stod(line.substr(sp+1));b.process(l,r);sprintf_s(buf,"%.17g %.17g\n",b.l,b.r);fout<<buf;}return 0;}'
$a=$a.Replace('int wmain(int argc,wchar_t** argv){',$block)
[IO.File]::WriteAllText($p,$a)
'patched'
