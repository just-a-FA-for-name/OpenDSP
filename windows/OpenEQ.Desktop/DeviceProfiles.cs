using System.Text.Json;
namespace OpenEQ;
public sealed class DeviceProfiles {
 readonly string file;readonly Dictionary<string,string> bindings=new();public bool Enabled{get;set;}=true;
 public DeviceProfiles(string directory){file=Path.Combine(directory,"device-profiles.json");try{if(File.Exists(file)){var data=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file));if(data!=null)bindings=data;bool changed=false;foreach(var key in bindings.Keys.ToArray()){var p=Preset.Parse(bindings[key]);if(UpgradeLegacyStarter(p)){bindings[key]=p.Encode();changed=true;}}if(changed){if(!File.Exists(file+".before-volume-fix"))File.Copy(file,file+".before-volume-fix");Write();}}}catch{}}
 public bool Has(string id)=>bindings.ContainsKey(id);
 public Preset? Load(string id){if(!Enabled||!bindings.TryGetValue(id,out var text))return null;return Preset.Parse(text);}
 public void Save(string id,Preset preset){if(!Enabled)return;bindings[id]=preset.Encode();Write();}
 void Write(){Directory.CreateDirectory(Path.GetDirectoryName(file)!);string temp=file+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(bindings,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,file,true);}
 public static bool UpgradeLegacyStarter(Preset p){double[] hz={60,150,400,1000,2500,6000,12000};if(p.Name!="平直 · 原声起点"||p.Preamp!=-6||p.Output.Gain!=-12||!p.Enabled||p.Bands.Count!=hz.Length||p.Output.Balance!=0||p.Output.LeftDelay!=0||p.Output.RightDelay!=0||p.Output.InvertLeft||p.Output.InvertRight||!p.Output.Protection||p.Output.Ceiling!=-1)return false;for(int n=0;n<hz.Length;n++){var b=p.Bands[n];if(b.Frequency!=hz[n]||b.Type!="peak"||b.Gain!=0||b.Q!=.707||b.Channel!="both"||!b.Enabled)return false;}p.Preamp=0;p.Output.Gain=0;return true;}
 public static List<Preset> Factory(){var flat=Preset.Flat();flat.Name="平直 · 原声起点";var warm=Preset.Flat();warm.Name="温暖 · 温和低频";warm.Preamp=-5;warm.Bands.Clear();warm.Bands.Add(new Band{Type="low_shelf",Frequency=120,Gain=2,Q=.707});warm.Bands.Add(new Band{Frequency=3000,Gain=-.8,Q=1});
  var clear=Preset.Flat();clear.Name="清晰 · 人声轻提";clear.Preamp=-5;clear.Bands.Clear();clear.Bands.Add(new Band{Frequency=250,Gain=-1,Q=.8});clear.Bands.Add(new Band{Frequency=2400,Gain=1.8,Q=.8});clear.Bands.Add(new Band{Type="high_shelf",Frequency=9000,Gain=.6,Q=.707});
  var soft=Preset.Flat();soft.Name="柔和 · 久听舒适";soft.Preamp=-4;soft.Bands.Clear();soft.Bands.Add(new Band{Frequency=3500,Gain=-1.5,Q=1});soft.Bands.Add(new Band{Type="high_shelf",Frequency=8000,Gain=-1.5,Q=.707});return new(){flat,warm,clear,soft};
 }
}
