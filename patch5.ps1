$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Apo\shared.h'
$a=[IO.File]::ReadAllText($p)
$a=$a.Replace(' if(!finite(c.compThreshold,-60,0)||!finite(c.compRatio,1,20)||!finite(c.compAttackMs,.1,200)||!finite(c.compReleaseMs,5,2000)||!finite(c.compKnee,0,24)||!finite(c.compMakeup,-12,24))return false;',' if((c.flags&16)&&(!finite(c.compThreshold,-60,0)||!finite(c.compRatio,1,20)||!finite(c.compAttackMs,.1,200)||!finite(c.compReleaseMs,5,2000)||!finite(c.compKnee,0,24)||!finite(c.compMakeup,-12,24)))return false;')
[IO.File]::WriteAllText($p,$a)
'patched'
