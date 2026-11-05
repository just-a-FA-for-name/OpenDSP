$p='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\RackParity.cs'
$b=[IO.File]::ReadAllText($p)
$old='double sum=0,mx=0;for(int i=0;i<n;i++){sum+=Math.Abs(proc.Left)+Math.Abs(proc.Right);}for(int i=0;i<n;i++){var parts=lines[i].Split(" ");double lv=Math.Abs(double.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture)),rv=Math.Abs(double.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture));mx=Math.Max(mx,Math.Max(lv,rv));}Console.WriteLine($"CSP parity n={n} sum={sum:R} max={mx:R}");'
$new='double sum=0,mx=0;for(int i=0;i<n;i++){var parts=lines[i].Split(" ");double lv=Math.Abs(double.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture)),rv=Math.Abs(double.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture));sum+=lv+rv;mx=Math.Max(mx,Math.Max(lv,rv));}Console.WriteLine($"CSP parity n={n} sum={sum:R} max={mx:R}");'
$b=$b.Replace($old,$new)
[IO.File]::WriteAllText($p,$b)
'fixed'
