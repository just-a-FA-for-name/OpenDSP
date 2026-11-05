namespace OpenEQ;
public static class SpatialScenes {
 public sealed record Scene(string Id,string Name,string Description);
 public static readonly Scene[] All={
  new("natural","自然交叉馈送","缓和硬左右分离，中央单声道信息保持。"),
  new("wide","宽阔伴奏","低频略收中，中高频侧向扩展；注意录音中的反相信息。"),
  new("vocal","中央女声","轻提中央密度，侧向略退并柔化高频。"),
  new("bass-center","低频收中","低频侧向渐减，中高频轻扩展；分频为平滑过渡。"),
  new("mono","单声道对照","100%混合时折叠到中央，检查立体声依赖。")
 };
 public static SpatialConfig Create(string id)=>id switch{
  "natural"=>new(){Enabled=true,Crossfeed=.2,CrossfeedHz=700,CrossfeedDelay=.25},
  "wide"=>new(){Enabled=true,LowWidth=.65,MidWidth=1.45,HighWidth=1.65,SideBands=new(){new(){Type="high_shelf",Frequency=8000,Gain=-.7}}},
  "vocal"=>new(){Enabled=true,MidGain=1.5,SideGain=-1,LowWidth=.8,MidWidth=1.05,HighWidth=1.2,
   MidBands=new(){new(){Frequency=1700,Gain=.8,Q=.9}},SideBands=new(){new(){Frequency=2500,Gain=-.7,Q=1.1},new(){Type="high_shelf",Frequency=9000,Gain=-1}}},
  "bass-center"=>new(){Enabled=true,LowWidth=0,MidWidth=1.2,HighWidth=1.3,LowCrossover=180},
  "mono"=>new(){Enabled=true,Mono=true},
  _=>throw new ArgumentException("Unknown spatial scene")
 };
}
