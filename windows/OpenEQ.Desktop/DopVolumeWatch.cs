using System.Text.Json;
using NAudio.CoreAudioApi;
namespace OpenEQ;
internal static class DopVolumeWatch {
 public static int Run(string path,int seconds,string id){
  using var en=new MMDeviceEnumerator();using var device=en.GetDevice(id);var volume=device.AudioEndpointVolume;
  var notifications=new List<object>();var gate=new object();var watch=System.Diagnostics.Stopwatch.StartNew();float initial=volume.MasterVolumeLevel;
  void Changed(AudioVolumeNotificationData n){lock(gate)notifications.Add(new{second=watch.Elapsed.TotalSeconds,context=n.EventContext,scalar=n.MasterVolume,channels=n.ChannelVolume,muted=n.Muted});}
  volume.OnVolumeNotification+=Changed;
  try{do{lock(gate){string temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new{elapsed_seconds=watch.Elapsed.TotalSeconds,initial_db=initial,current_db=volume.MasterVolumeLevel,notifications=notifications.ToArray(),scope="Read-only endpoint volume notifications; never writes device settings"},new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);}if(watch.Elapsed.TotalSeconds>=seconds)return 0;Thread.Sleep(1000);}while(true);}
  finally{volume.OnVolumeNotification-=Changed;}
 }
}
