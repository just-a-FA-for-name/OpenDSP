using System.Globalization;
namespace OpenEQ;
// 逐样本对照台：同一确定性输入分别喂给 C# StereoProcessor（--rack-parity）
// 与 C++ Bank（OpenEQ.Apo.Checks.exe --parity），输出 17 位精度，供外部求最大偏差。
internal static class RackParity {
 public static int Run(string dir){
  const int rate=48000;const int n=24000;
  Directory.CreateDirectory(dir);
  var input=new double[n*2];
  for(int i=0;i<n;i++){input[i*2]=.4*Math.Sin(2*Math.PI*.0137*i)+.25*Math.Sin(2*Math.PI*.11*i+.7);input[i*2+1]=.35*Math.Sin(2*Math.PI*.0211*i+1.3)+.2*Math.Sin(2*Math.PI*.083*i);}
  File.WriteAllLines(Path.Combine(dir,"input.txt"),Enumerable.Range(0,n).Select(i=>input[i*2].ToString("R",CultureInfo.InvariantCulture)+" "+input[i*2+1].ToString("R",CultureInfo.InvariantCulture)));
  var p=new Preset{Enabled=true,Preamp=-3};
  p.Output.Gain=-2;p.Output.Balance=.2;p.Output.LeftDelay=2;p.Output.RightDelay=3;p.Output.InvertLeft=false;p.Output.InvertRight=true;p.Output.Protection=true;p.Output.Ceiling=-1;p.Output.DcBlock=true;
  p.Output.Matrix=new MatrixConfig{Ll=1.1,Lr=.2,Rl=-.1,Rr=.9,Width=1.3};
  p.Output.Compressor=new CompConfig{Enabled=true,ThresholdDb=-18,Ratio=4,AttackMs=10,ReleaseMs=120,KneeDb=6,MakeupDb=2};
  p.Bands.Clear();
  p.Bands.Add(new Band{Type="peak",Enabled=true,Channel="left",Frequency=1000,Gain=-6,Q=2});
  p.Bands.Add(new Band{Type="high_shelf",Enabled=true,Channel="both",Frequency=8000,Gain=4,Q=.707});
  p.Bands.Add(new Band{Type="low_shelf",Enabled=true,Channel="both",Frequency=60,Gain=3,Q=.707});
  var proc=new StereoProcessor(rate);proc.Configure(p);
  var lines=new string[n];
  for(int i=0;i<n;i++){proc.Process(input[i*2],input[i*2+1]);lines[i]=proc.Left.ToString("R",CultureInfo.InvariantCulture)+" "+proc.Right.ToString("R",CultureInfo.InvariantCulture);}
  File.WriteAllLines(Path.Combine(dir,"managed_out.txt"),lines);double sum=0,mx=0;for(int i=0;i<n;i++){var parts=lines[i].Split(" ");double lv=Math.Abs(double.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture)),rv=Math.Abs(double.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture));sum+=lv+rv;mx=Math.Max(mx,Math.Max(lv,rv));}Console.WriteLine($"CSP parity n={n} sum={sum:R} max={mx:R}");
  return 0;
 }
}
