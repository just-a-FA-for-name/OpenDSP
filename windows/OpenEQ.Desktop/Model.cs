using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenEQ;
public sealed class Band {
 [JsonPropertyName("type")] public string Type {get;set;}="peak";
 [JsonPropertyName("frequency_hz")] public double Frequency {get;set;}=1000;
 [JsonPropertyName("gain_db")] public double Gain {get;set;}
 [JsonPropertyName("q")] public double Q {get;set;}=.707;
 [JsonPropertyName("channel")] public string Channel {get;set;}="both";
 [JsonPropertyName("enabled")] public bool Enabled {get;set;}=true;
}
public sealed class CompConfig {
 [JsonPropertyName("enabled")] public bool Enabled{get;set;}
 [JsonPropertyName("threshold_db")] public double ThresholdDb{get;set;}=-18;
 [JsonPropertyName("ratio")] public double Ratio{get;set;}=4;
 [JsonPropertyName("attack_ms")] public double AttackMs{get;set;}=10;
 [JsonPropertyName("release_ms")] public double ReleaseMs{get;set;}=120;
 [JsonPropertyName("knee_db")] public double KneeDb{get;set;}=6;
 [JsonPropertyName("makeup_db")] public double MakeupDb{get;set;}
}
public sealed class MatrixConfig {
 [JsonPropertyName("ll")] public double Ll{get;set;}=1;
 [JsonPropertyName("lr")] public double Lr{get;set;}
 [JsonPropertyName("rl")] public double Rl{get;set;}
 [JsonPropertyName("rr")] public double Rr{get;set;}=1;
 [JsonPropertyName("width")] public double Width{get;set;}=1;
}
public sealed class SpatialConfig {
 [JsonPropertyName("enabled")] public bool Enabled{get;set;}
 [JsonPropertyName("mix")] public double Mix{get;set;}=1;
 [JsonPropertyName("mid_gain_db")] public double MidGain{get;set;}
 [JsonPropertyName("side_gain_db")] public double SideGain{get;set;}
 [JsonPropertyName("mid_filters")] public List<Band> MidBands{get;set;}=new();
 [JsonPropertyName("side_filters")] public List<Band> SideBands{get;set;}=new();
 [JsonPropertyName("low_width")] public double LowWidth{get;set;}=1;
 [JsonPropertyName("mid_width")] public double MidWidth{get;set;}=1;
 [JsonPropertyName("high_width")] public double HighWidth{get;set;}=1;
 [JsonPropertyName("low_crossover_hz")] public double LowCrossover{get;set;}=180;
 [JsonPropertyName("high_crossover_hz")] public double HighCrossover{get;set;}=3500;
 [JsonPropertyName("crossfeed")] public double Crossfeed{get;set;}
 [JsonPropertyName("crossfeed_hz")] public double CrossfeedHz{get;set;}=700;
 [JsonPropertyName("crossfeed_delay_ms")] public double CrossfeedDelay{get;set;}=.25;
 [JsonPropertyName("mono")] public bool Mono{get;set;}
 public void Validate(){
  Preset.Range(Mix,0,1,"空间混合");Preset.Range(MidGain,-12,6,"中央增益");Preset.Range(SideGain,-18,6,"侧向增益");
  foreach(double width in new[]{LowWidth,MidWidth,HighWidth})Preset.Range(width,0,2,"分区宽度");
  Preset.Range(LowCrossover,50,1000,"低分频点");Preset.Range(HighCrossover,500,10000,"高分频点");
  if(HighCrossover<LowCrossover*2)throw new ArgumentException("高分频点至少为低分频点的两倍");
  Preset.Range(Crossfeed,0,.45,"交叉馈送");Preset.Range(CrossfeedHz,100,1500,"交叉馈送截止");Preset.Range(CrossfeedDelay,0,.8,"交叉馈送延时");
  foreach(var bands in new[]{MidBands,SideBands}){
   Preset.ValidateBands(bands,16);
   if(bands.Any(b=>b.Channel!="both"))throw new ArgumentException("M/S滤波器已由所在组确定通道，channel须为both");
  }
 }
}
public sealed class OutputConfig {
 [JsonPropertyName("gain_db")] public double Gain {get;set;}=0;
 [JsonPropertyName("balance")] public double Balance {get;set;}
 [JsonPropertyName("left_delay_ms")] public double LeftDelay {get;set;}
 [JsonPropertyName("right_delay_ms")] public double RightDelay {get;set;}
 [JsonPropertyName("invert_left")] public bool InvertLeft {get;set;}
 [JsonPropertyName("invert_right")] public bool InvertRight {get;set;}
 [JsonPropertyName("peak_protection")] public bool Protection {get;set;}=true;
 [JsonPropertyName("ceiling_db")] public double Ceiling {get;set;}=-1;
 [JsonPropertyName("compressor")] public CompConfig Compressor{get;set;}=new();
 [JsonPropertyName("matrix")] public MatrixConfig Matrix{get;set;}=new();
 [JsonPropertyName("dc_block")] public bool DcBlock{get;set;}
 [JsonPropertyName("spatial")] public SpatialConfig Spatial{get;set;}=new();
}
public sealed class Preset {
 [JsonPropertyName("format")] public string Format {get;set;}="org.openeq.preset";
 [JsonPropertyName("version")] public int Version {get;set;}=1;
 [JsonPropertyName("name")] public string Name {get;set;}="默认";
 [JsonPropertyName("preamp_db")] public double Preamp {get;set;}=0;
 [JsonPropertyName("enabled")] public bool Enabled {get;set;}=true;
 [JsonPropertyName("device_hint")] public string DeviceHint {get;set;}="";
 [JsonPropertyName("output")] public OutputConfig Output {get;set;}=new();
 [JsonPropertyName("filters")] public List<Band> Bands {get;set;}=new();
 public static readonly string[] Types={"peak","low_shelf","high_shelf","low_pass","high_pass","notch","all_pass"};
 public static readonly string[] Labels={"峰值","低架","高架","低通 12 dB/oct","高通 12 dB/oct","陷波","全通"};
 public static Preset Flat(){var p=new Preset();foreach(var f in new double[]{60,150,400,1000,2500,6000,12000})p.Bands.Add(new Band{Frequency=f});return p;}
 public static Preset Blank(){return new Preset{Name="平直",Bands=new List<Band>()};}
 public static void Range(double value,double min,double max,string label){if(!double.IsFinite(value)||value<min||value>max)throw new ArgumentException($"{label} 范围 {min}–{max}");}
 public void Validate(){
  if(Format!="org.openeq.preset"||Version!=1)throw new ArgumentException("不支持的预设格式 / 版本");
  if(Name==null||Name.Length>100||Bands==null||Bands.Count>32||Output==null)throw new ArgumentException("无效名称、滤波器数量或输出设置");
  Range(Preamp,-60,0,"前级");Range(Output.Gain,-60,0,"输出增益");Range(Output.Balance,-1,1,"平衡");Range(Output.LeftDelay,0,100,"左延时");Range(Output.RightDelay,0,100,"右延时");Range(Output.Ceiling,-12,0,"峰值上限");
  var comp=Output.Compressor??new CompConfig();var matrix=Output.Matrix??new MatrixConfig();
  Range(comp.ThresholdDb,-60,0,"压缩阈值");Range(comp.Ratio,1,20,"压缩比");Range(comp.AttackMs,.1,200,"启动时间");Range(comp.ReleaseMs,5,2000,"释放时间");Range(comp.KneeDb,0,24,"拐点");Range(comp.MakeupDb,-12,24,"补偿");
  foreach(var g in new[]{matrix.Ll,matrix.Lr,matrix.Rl,matrix.Rr})Range(g,-2,2,"矩阵增益");Range(matrix.Width,0,2,"立体声宽度");
  ValidateBands(Bands,32);(Output.Spatial??new SpatialConfig()).Validate();
 }
 public static void ValidateBands(List<Band>? bands,int maximum){
  if(bands==null||bands.Count>maximum)throw new ArgumentException($"滤波器最多{maximum}段");
  foreach(var b in bands){if(b==null||!Types.Contains(b.Type)||!new[]{"both","left","right"}.Contains(b.Channel))throw new ArgumentException("不支持的滤波器或声道");Range(b.Frequency,20,20000,"频率");Range(b.Gain,-24,24,"增益");Range(b.Q,.1,30,"Q");}
 }
 public string Encode()=>JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
 public static Preset Parse(string text){if(text.Length>262144)throw new ArgumentException("预设文件过大");using var doc=JsonDocument.Parse(text);var r=doc.RootElement;foreach(var k in new[]{"format","version","name","preamp_db","enabled","filters"})if(!r.TryGetProperty(k,out _))throw new ArgumentException("配置缺少 "+k);
  foreach(var b in r.GetProperty("filters").EnumerateArray())foreach(var key in new[]{"type","frequency_hz","gain_db","q","channel","enabled"})if(!b.TryGetProperty(key,out _))throw new ArgumentException("滤波器缺少 "+key);
  if(r.TryGetProperty("output",out var o))foreach(var key in new[]{"gain_db","balance","left_delay_ms","right_delay_ms","invert_left","invert_right","peak_protection","ceiling_db"})if(!o.TryGetProperty(key,out _))throw new ArgumentException("输出配置缺少 "+key);
  var p=JsonSerializer.Deserialize<Preset>(text)??throw new ArgumentException("空配置");p.Validate();return p;}
 public Preset Copy()=>Parse(Encode());
 public double Response(double f,bool left,int rate=48000){if(!Enabled)return 0;double db=Preamp;foreach(var b in Bands)if(b.Enabled&&(b.Channel=="both"||b.Channel==(left?"left":"right")))db+=20*Math.Log10(Math.Max(1e-12,new Biquad(b,rate).Magnitude(f,rate)));return db;}
}
