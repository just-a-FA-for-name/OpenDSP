$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\shared.h'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace('mLL=c.mLL;mLR=c.mLR;mRL=c.mRL;mRR=c.mRR;width=c.width;','mLL=c.mLL;mLR=c.mLR;mRL=c.mRL;mRR=c.mRR;width=c.width;if(mLL==0&&mLR==0&&mRL==0&&mRR==0&&width==0){mLL=mRR=1;width=1;}')
[IO.File]::WriteAllText($p,$a)
'patched'
