using System.Globalization;
using System.Text.RegularExpressions;
namespace OpenEQ;
public static class PresetImport {
 public static Preset EqualizerApo(string text){var result=Preset.Flat();result.Bands.Clear();result.Name="导入 EQ 参数";foreach(var raw in text.Split('\n')){string line=raw.Split('#')[0].Trim();if(line.Length==0)continue;
  var pre=Regex.Match(line,@"^Preamp:\s*([-+\d.]+)\s*dB\s*$",RegexOptions.IgnoreCase);if(pre.Success){result.Preamp=double.Parse(pre.Groups[1].Value,CultureInfo.InvariantCulture);continue;}
  var filter=Regex.Match(line,@"^Filter\s*\d*:\s*(ON|OFF)\s+(PK|LS|HS|LP|HP|NO|AP)\s+Fc\s+([-+\d.]+)\s+Hz(?:\s+Gain\s+([-+\d.]+)\s+dB)?\s+Q\s+([-+\d.]+)\s*$",RegexOptions.IgnoreCase);
  if(!filter.Success)throw new ArgumentException("不支持的 EQ APO 指令，未作近似转换："+line);
  string kind=filter.Groups[2].Value.ToUpperInvariant();result.Bands.Add(new Band{Type=kind switch{"PK"=>"peak","LS"=>"low_shelf","HS"=>"high_shelf","LP"=>"low_pass","HP"=>"high_pass","NO"=>"notch",_=>"all_pass"},Frequency=double.Parse(filter.Groups[3].Value,CultureInfo.InvariantCulture),Gain=filter.Groups[4].Success?double.Parse(filter.Groups[4].Value,CultureInfo.InvariantCulture):0,Q=double.Parse(filter.Groups[5].Value,CultureInfo.InvariantCulture),Enabled=filter.Groups[1].Value.Equals("ON",StringComparison.OrdinalIgnoreCase)});
 }result.Validate();if(result.Bands.Count==0)throw new ArgumentException("没有可导入的滤波器");return result;}
 public static Preset Graphic31(){var p=Preset.Flat();p.Name="31 段图示均衡";p.Bands.Clear();foreach(double hz in new double[]{20,25,31.5,40,50,63,80,100,125,160,200,250,315,400,500,630,800,1000,1250,1600,2000,2500,3150,4000,5000,6300,8000,10000,12500,16000,20000})p.Bands.Add(new Band{Frequency=hz,Q=4.318});return p;}
 public static double Headroom(Preset p,int rate=48000){var clone=p.Copy();clone.Enabled=true;clone.Preamp=0;double highest=0;for(int n=0;n<=1500;n++){double f=20*Math.Pow(1000,n/1500.0);highest=Math.Max(highest,Math.Max(clone.Response(f,true,rate),clone.Response(f,false,rate)));}return -Math.Min(60,Math.Ceiling((highest+1)*10)/10);}
}
