using NAudio.CoreAudioApi;
using System.Text.Json;
namespace OpenEQ;
internal static class AudioSessionReport {
 public static int Run(string path){
  using var en=new MMDeviceEnumerator();var devices=new List<object>();
  foreach(var d in en.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){
   try{var sessions=new List<object>();var list=d.AudioSessionManager.Sessions;
    for(int i=0;i<list.Count;i++){var s=list[i];string process="";try{process=System.Diagnostics.Process.GetProcessById((int)s.GetProcessID).ProcessName;}catch{}
     sessions.Add(new{pid=s.GetProcessID,process,state=s.State.ToString(),mute=s.SimpleAudioVolume.Mute,volume=s.SimpleAudioVolume.Volume,peak=s.AudioMeterInformation.MasterPeakValue});}
    devices.Add(new{id=d.ID,name=d.FriendlyName,peak=d.AudioMeterInformation.MasterPeakValue,sessions});
   }catch(Exception ex){devices.Add(new{id=d.ID,name=d.FriendlyName,error=ex.Message});}finally{d.Dispose();}
  }
  File.WriteAllText(path,JsonSerializer.Serialize(devices,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));return 0;
 }
}
