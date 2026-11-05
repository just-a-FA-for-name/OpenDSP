$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\checks.cpp'
$a=[IO.File]::ReadAllText($p)
$bs=[char]92
$new='int wmain(int argc,wchar_t** argv){if(argc>1&&std::wstring(argv[1])==L"--parity"){std::wstring dir=argc>2?argv[2]:L".";Config c{};c.active=c.enabled=1;c.count=3;c.flags=31;c.preamp=-3;c.output=-2;c.balance=.2;c.delayL=2;c.delayR=3;c.ceiling=-1;c.compThreshold=-18;c.compRatio=4;c.compAttackMs=10;c.compReleaseMs=120;c.compKnee=6;c.compMakeup=2;c.mLL=1.1;c.mLR=.2;c.mRL=-.1;c.mRR=.9;c.width=1.3;c.filters[0]={0,1,1,0,1000,-6,2};c.filters[1]={2,1,0,0,8000,4,.707};c.filters[2]={1,1,0,0,60,3,.707};Bank b;b.allocate(48000);b.configure(c,48000);std::ifstream fin(dir+L"/input.txt");std::string line;double l,r,sum=0,mx=0,n=0;while(std::getline(fin,line)){auto sp=line.find(32);l=std::stod(line.substr(0,sp));r=std::stod(line.substr(sp+1));b.process(l,r);sum+=abs(b.l)+abs(b.r);mx=std::max(mx,std::max(abs(b.l),abs(b.r)));n++;}printf("APO parity n=%.0f sum=%.17g max=%.17g'+$bs+'n",n,sum,mx);return 0;}int cases=0;'
$a=[regex]::Replace($a,[regex]::Escape('int wmain(int argc,wchar_t** argv){')+'.*?int cases=0;',$new,1)
[IO.File]::WriteAllText($p,$a)
'rewritten'
