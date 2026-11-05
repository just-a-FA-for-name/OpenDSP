$p1='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\shared.h'
$a=[IO.File]::ReadAllText($p1)
$a=$a.Replace('case 2:b0=A*((A+1)+(A-1)*c+t);b1=-2*A*((A-1)+(A+1)*c);b2=A*((A+1)-(A-1)*c-t);a0=(A+1)-(A-1)*c+t;a1=2*((A-1)+(A+1)*c);a2=(A+1)-(A-1)*c-t;break;','case 2:b0=A*((A+1)+(A-1)*c+t);b1=-2*A*((A-1)+(A+1)*c);b2=A*((A+1)-(A-1)*c-t);a0=(A+1)-(A-1)*c+t;a1=2*((A-1)-(A+1)*c);a2=(A+1)-(A-1)*c-t;break;')
[IO.File]::WriteAllText($p1,$a)
$p2='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\Dsp.cs'
$b=[IO.File]::ReadAllText($p2)
$b=$b.Replace('case "high_shelf":b0=A*((A+1)+(A-1)*c+t);b1=-2*A*((A-1)+(A+1)*c);b2=A*((A+1)-(A-1)*c-t);a0=(A+1)-(A-1)*c+t;a1=2*((A-1)+(A+1)*c);a2=(A+1)-(A-1)*c-t;break;','case "high_shelf":b0=A*((A+1)+(A-1)*c+t);b1=-2*A*((A-1)+(A+1)*c);b2=A*((A+1)-(A-1)*c-t);a0=(A+1)-(A-1)*c+t;a1=2*((A-1)-(A+1)*c);a2=(A+1)-(A-1)*c-t;break;')
[IO.File]::WriteAllText($p2,$b)
'patched both'
