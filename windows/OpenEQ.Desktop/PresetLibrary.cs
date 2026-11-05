using System.Text.Json;
namespace OpenEQ;
public static class PresetLibrary {
 public static object Read(string directory){
  var presets=new SortedDictionary<string,Preset>(StringComparer.Ordinal);
  foreach(var preset in DeviceProfiles.Factory())presets[preset.Name]=preset;
  var warnings=new List<string>();string folder=Path.Combine(directory,"presets");
  if(Directory.Exists(folder))foreach(string file in Directory.EnumerateFiles(folder,"*.json").OrderBy(p=>p,StringComparer.Ordinal).Take(200)){
   try{var preset=Preset.Parse(File.ReadAllText(file));presets[preset.Name]=preset;}catch(Exception e){warnings.Add(Path.GetFileName(file)+": "+e.Message);}
  }
  return new{format="org.opendsp.preset-library",version=1,source="OpenDSP PC",presets=presets.Values,warnings};
 }
 public static int Export(string file){File.WriteAllText(file,JsonSerializer.Serialize(Read(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ")),new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));return 0;}
}
