using System.Globalization;
using System.Reflection;
using System.Text.Json;
namespace OpenEQ;
/// <summary>Sourced acoustic targets and explicitly relative listening preferences.</summary>
public static class TargetCurves {
 public sealed record Curve(string Name,string Description,double[,] Points,string Id="",string Kind="relative",string Rig="none",string Application="preference",string Source="OpenDSP 原创听感偏好"){
  public bool RequiresMeasurement=>Kind=="acoustic";
  public double Response(double hz){
   var p=Points;int n=p.GetLength(0);if(n<2)return 0;
   if(hz<=p[0,0])return p[0,1];if(hz>=p[n-1,0])return p[n-1,1];
   int low=0,high=n-1;while(high-low>1){int mid=(low+high)/2;if(p[mid,0]>hz)high=mid;else low=mid;}
   double fraction=Math.Log(hz/p[low,0])/Math.Log(p[high,0]/p[low,0]);return p[low,1]+fraction*(p[high,1]-p[low,1]);
  }
 }
 static double[,] P(params double[] flat){var m=new double[flat.Length/2,2];for(int i=0;i<flat.Length/2;i++){m[i,0]=flat[i*2];m[i,1]=flat[i*2+1];}return m;}
 static readonly Curve[] Preferences={
  new("平直","参考基准",P(20,0,1000,0,20000,0)),
  new("V 型","低频高频齐抬，动感取向",P(20,6,50,5,100,3,200,1,500,0,1000,0,2000,0,4000,1,8000,3,12000,4.5,16000,4,20000,2)),
  new("温暖","低频饱满、高频柔化",P(20,4,50,3.5,100,2,200,1,500,0,1000,0,2000,0,4000,-1,8000,-2.5,12000,-3,16000,-4,20000,-5)),
  new("明亮","中高频通透",P(20,0,50,0,100,1,200,1,500,0,1000,0,2000,1,3000,2,5000,2,8000,3,12000,3,16000,2,20000,0)),
  new("低音量补偿","小音量聆听时的等响度补偿",P(20,7,50,5,100,2.5,200,1,1000,0,5000,.5,8000,1.5,12000,3,16000,4,20000,3)),
  new("人声/播客","清晰度取向，中频突出",P(20,-3,100,-2,250,-1,500,1,1000,2,2000,2,4000,1,8000,-1,12000,-2,20000,-3)),
  new("录音棚平直+高频延展","监听参考",P(20,-2,40,-1,100,0,1000,0,5000,0,10000,1,16000,1,20000,0)),
  new("ACG 甜润女声","实体感、贴近感与柔和齿音",P(20,0,100,-.8,250,-.6,700,.5,1200,1,2000,1.8,3000,.7,4200,-1,6500,-3,8500,-2.5,12000,-1,20000,-.5)),
  new("ACG 清亮女声","咬字清楚，保留温和高频",P(20,0,150,-1,500,-.3,1200,.6,2500,2,4000,.2,6500,-1.5,9000,-1,16000,0,20000,0)),
  new("ACG 柔顺女声","减少硬边，保留中频厚度",P(20,0,150,-.5,700,.6,1800,1,3500,-.5,5500,-2.5,8000,-3,12000,-2,20000,-1)),
  new("ACG 燃系摇滚","鼓点和中频力度，收敛尖锐高频",P(20,.8,60,1,180,-.3,400,-.5,1500,1,2800,1.5,4500,-.5,7000,-2,12000,-1,20000,-.5)),
  new("OST 配乐","低频厚度和较平缓的中高频",P(20,.8,80,.5,300,-.6,1000,.3,2300,.5,4500,-.5,7000,-1.5,12000,-.3,20000,0)),
  new("电子 / 深低频","强调下潜，减少中低频堆积",P(20,3,50,2.5,100,1,250,-1,600,-.5,2000,.5,5000,-.5,8000,-1,20000,0)),
  new("原声 / 器乐","轻收低中频，保留乐器中频",P(20,0,200,-.5,500,0,1300,.6,2800,.5,5000,-.3,9000,-.5,20000,0)),
  new("轻微 U 型","较克制的两端取向",P(20,2,100,1,400,-.5,1200,-.5,3000,0,8000,1,14000,1,20000,0)),
  new("低频减量","保留下潜，收中低频量感",P(20,0,50,-.2,100,-1.5,200,-1.5,400,-.5,1000,0,20000,0)),
  new("齿音柔化","宽带处理5–10kHz刺激",P(20,0,2000,0,4000,-.5,5500,-2,7000,-3,9500,-1.5,14000,-.5,20000,0)),
  new("暗暖耐听","降低整体高频亮度",P(20,.5,150,.2,600,.3,1800,.5,3500,-1,5500,-2,8000,-3,12000,-3.5,20000,-4)),
  new("低频轻补","不额外提升中高频",P(20,2,50,1.8,100,1,250,.2,500,0,20000,0)),
  new("人声前移","适度增加1–3kHz存在感",P(20,0,300,-.4,700,.5,1300,1.2,2200,1.6,3500,.5,5000,-.5,9000,-.5,20000,0)),
 };
 public static readonly Curve[] BuiltIn=LoadCatalog();
 static Curve[] LoadCatalog(){
  var assembly=typeof(TargetCurves).Assembly;
  string Read(string filename){using var stream=assembly.GetManifestResourceStream("OpenDSP.Targets."+filename)??throw new InvalidOperationException("Missing target resource: "+filename);using var reader=new StreamReader(stream);return reader.ReadToEnd();}
  using var catalog=JsonDocument.Parse(Read("catalog.json"));
  var curves=new List<Curve>();
  foreach(var entry in catalog.RootElement.GetProperty("entries").EnumerateArray()){
   string rig=entry.GetProperty("rig").GetString()!,name=entry.GetProperty("name").GetString()!;
   curves.Add(new(name,$"{rig} · AutoEq来源数据 · 需同体系原始频响",ToArray(ParseMeasured(Read(entry.GetProperty("file").GetString()!))),
    entry.GetProperty("id").GetString()!,"acoustic",rig,entry.GetProperty("application").GetString()!,entry.GetProperty("source").GetString()!));
  }
  curves.AddRange(Preferences.Select((curve,i)=>curve with{Id="preference-"+i}));
  return curves.ToArray();
 }
 public static Curve Find(string id)=>BuiltIn.FirstOrDefault(c=>c.Id==id||c.Name==id)??throw new ArgumentException("Unknown target: "+id);
 public static List<CurvePoint> ParseMeasured(string text){
  if(text.Length>524288)throw new ArgumentException("频响文件过大");
  var list=new List<CurvePoint>();
  foreach(string raw in text.Split('\n')){
   string line=raw.Trim().TrimStart('\uFEFF');if(line.Length==0||line.StartsWith('#')||line.StartsWith(';'))continue;
   var parts=line.Split(new[]{',',';','\t',' '},StringSplitOptions.RemoveEmptyEntries);
   if(list.Count==0&&parts.Length>=2&&(parts[0].Equals("frequency",StringComparison.OrdinalIgnoreCase)||parts[0].Equals("freq",StringComparison.OrdinalIgnoreCase)||parts[0].Equals("hz",StringComparison.OrdinalIgnoreCase)))continue;
   if(parts.Length<2||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double hz)||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double db))throw new ArgumentException("频响格式应为 frequency,db，每行一个频点");
   list.Add(new(hz,db));
  }
  list=list.OrderBy(p=>p.Hz).ToList();ValidatePoints(list);return list;
 }
 public static void ValidatePoints(IReadOnlyList<CurvePoint> points){
  if(points.Count<3||points.Count>8192)throw new ArgumentException("频响需要3至8192个频点");
  double previous=0;
  foreach(var point in points){Preset.Range(point.Hz,10,40000,"频响频率");Preset.Range(point.Db,-160,160,"频响电平");if(point.Hz<=previous)throw new ArgumentException("频响频率须严格递增，不允许重复频点");previous=point.Hz;}
 }
 public static double[,] ToArray(List<CurvePoint> points){ValidatePoints(points);var data=new double[points.Count,2];for(int i=0;i<points.Count;i++){data[i,0]=points[i].Hz;data[i,1]=points[i].Db;}return data;}
 public static double Interpolate(IReadOnlyList<CurvePoint> points,double hz){
  if(hz<=points[0].Hz)return points[0].Db;if(hz>=points[^1].Hz)return points[^1].Db;
  int low=0,high=points.Count-1;while(high-low>1){int mid=(low+high)/2;if(points[mid].Hz>hz)high=mid;else low=mid;}
  return points[low].Db+(points[high].Db-points[low].Db)*Math.Log(hz/points[low].Hz)/Math.Log(points[high].Hz/points[low].Hz);
 }
 public static List<Band> Generate(Curve target,List<CurvePoint>? measured,int bandCount=20,int sampleRate=48000,string? rig=null)=>
  TargetFitting.Fit(target,measured,sampleRate,new(Bands:bandCount,MaxHz:Math.Min(target.RequiresMeasurement?8000:20000,sampleRate*.49)),rig).Bands;
}
public sealed record CurvePoint(double Hz,double Db);
