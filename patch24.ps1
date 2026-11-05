$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\NativeApoChecks.cs'
$a=[IO.File]::ReadAllText($p)
$old='double[] before=Snapshot();long frames=apo.Frames;Thread.Sleep(1700);double[] after=Snapshot();double left=10*Math.Log10((after[2]-before[2])/(after[0]-before[0])),right=10*Math.Log10((after[3]-before[3])/(after[1]-before[1]));if(!double.IsFinite(left)||Math.Abs(left+12.1)>.08||!double.IsFinite(right)||Math.Abs(right)>.02)throw new Exception($"Native AudioDG EQ response mismatch L={left} R={right}");'
$new='double[] warm=null;var sw=System.Diagnostics.Stopwatch.StartNew();while(sw.ElapsedMilliseconds<10000){var s=Snapshot();if(warm!=null&&s[0]>warm[0])break;warm=s;Thread.Sleep(200);}double[] before=Snapshot();long frames=apo.Frames;Thread.Sleep(1500);double[] after=Snapshot();double inL=after[0]-before[0],inR=after[1]-before[1];if(inL<=0||inR<=0)throw new Exception("外部音源未推进输入计数器（冷启动超时）inL="+inL+" inR="+inR);double left=10*Math.Log10((after[2]-before[2])/inL),right=10*Math.Log10((after[3]-before[3])/inR);if(!double.IsFinite(left)||Math.Abs(left+12.1)>.08||!double.IsFinite(right)||Math.Abs(right)>.02)throw new Exception($"Native AudioDG EQ response mismatch L={left} R={right}");'
$a=$a.Replace($old,$new)
[IO.File]::WriteAllText($p,$a)
'harness fixed'
