using NAudio.CoreAudioApi;
namespace OpenEQ;
internal static class AudioDiagnostics {
 public static object Read(){using var en=new MMDeviceEnumerator();return en.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active).Select(device=>{try{
  var sessions=new List<object>();var all=device.AudioSessionManager.Sessions;for(int n=0;n<all.Count;n++){var s=all[n];sessions.Add(new{process=s.GetProcessID,name=s.DisplayName,state=s.State.ToString(),volume=s.SimpleAudioVolume.Volume,mute=s.SimpleAudioVolume.Mute});}
  return (object)new{id=device.ID,name=device.FriendlyName,mix=device.AudioClient.MixFormat.ToString(),hardware_volume=device.AudioEndpointVolume.HardwareSupport.ToString(),master_db=device.AudioEndpointVolume.MasterVolumeLevel,master=device.AudioEndpointVolume.MasterVolumeLevelScalar,mute=device.AudioEndpointVolume.Mute,sessions};
 }catch(Exception e){return new{name=device.FriendlyName,error=e.Message};}finally{device.Dispose();}}).ToArray();}
}
