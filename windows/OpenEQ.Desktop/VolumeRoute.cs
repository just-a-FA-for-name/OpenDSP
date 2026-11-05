using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
namespace OpenEQ;
/// <summary>Keep one initial master attenuation when changing the default endpoint.</summary>
public sealed class VolumeRoute:IDisposable {
 readonly MMDevice input,output;readonly AudioEndpointVolume iv,ov;readonly State state;bool restored;
 static string RecoveryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","volume-recovery.json");
 float inputPercent;
 public float InputPercent=>Volatile.Read(ref inputPercent);
 void InputVolumeChanged(AudioVolumeNotificationData data)=>Volatile.Write(ref inputPercent,data.MasterVolume*100);
 public string Mode=>state.HardwareOrShared?"保留输出设备音量，入口相对 0 dB":"由系统入口接管软件主音量";
 // 部分虚拟线缆（VB Hi-Fi Cable 等）不提供音量接口，激活会抛 CO_E_CLASSSTRING。
 // 音量接管是可选增强：失败时退回“保留手动路由的音量”，绝不阻断音频链路。
 public static VolumeRoute? TryCreate(MMDevice input,MMDevice output,bool exclusive){try{return new VolumeRoute(input,output,exclusive);}catch(COMException){return null;}}
 VolumeRoute(MMDevice input,MMDevice output,bool exclusive){this.input=input;this.output=output;iv=input.AudioEndpointVolume;ov=output.AudioEndpointVolume;
  bool hardwareOrShared=!exclusive||(ov.HardwareSupport&EEndpointHardwareSupport.Volume)!=0;
  state=new State{Input=input.ID,Output=output.ID,OriginalInputDb=iv.MasterVolumeLevel,OriginalInputMute=iv.Mute,OriginalOutputDb=ov.MasterVolumeLevel,OriginalOutputMute=ov.Mute,HardwareOrShared=hardwareOrShared};
  state.AssignedInputDb=Clamp(iv,hardwareOrShared?iv.VolumeRange.MaxDecibels:ov.MasterVolumeLevel);state.AssignedInputMute=ov.Mute;
  Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath)!);File.WriteAllText(RecoveryPath,JsonSerializer.Serialize(state));
  try{iv.MasterVolumeLevel=state.AssignedInputDb;iv.Mute=state.AssignedInputMute;
   try{inputPercent=iv.MasterVolumeLevelScalar*100;}catch{/* Optional UI telemetry must not disable working volume routing. */}
   iv.OnVolumeNotification+=InputVolumeChanged;
  }catch{Dispose();throw;}
 }
 static float Clamp(AudioEndpointVolume v,float db)=>Math.Clamp(db,v.VolumeRange.MinDecibels,v.VolumeRange.MaxDecibels);
 public void Dispose(){if(restored)return;restored=true;iv.OnVolumeNotification-=InputVolumeChanged;bool success=false;
  try{float current=iv.MasterVolumeLevel;bool muted=iv.Mute;try{
    // Carry the user's changes back only if they have not separately adjusted the DAC.
    if(Math.Abs(ov.MasterVolumeLevel-state.OriginalOutputDb)<.05&&Math.Abs(current-state.AssignedInputDb)>.05){float desired=state.HardwareOrShared?state.OriginalOutputDb+current-state.AssignedInputDb:current;ov.MasterVolumeLevel=Clamp(ov,desired);}
    if(ov.Mute==state.OriginalOutputMute&&muted!=state.AssignedInputMute)ov.Mute=muted;
   }catch{/* An unplugged DAC must not prevent restoring the virtual input. */}
   iv.MasterVolumeLevel=state.OriginalInputDb;iv.Mute=state.OriginalInputMute;success=true;
  }catch{/* 设备已拔出或音量接口不可用；保留恢复记录供下次重试 */}finally{if(success&&File.Exists(RecoveryPath))File.Delete(RecoveryPath);}
 }
 public static void Recover(){try{if(!File.Exists(RecoveryPath))return;var s=JsonSerializer.Deserialize<State>(File.ReadAllText(RecoveryPath))??throw new Exception("音量恢复记录无效");using var en=new MMDeviceEnumerator();using var d=en.GetDevice(s.Input);var v=d.AudioEndpointVolume;
  if(Math.Abs(v.MasterVolumeLevel-s.AssignedInputDb)<.05&&v.Mute==s.AssignedInputMute){v.MasterVolumeLevel=s.OriginalInputDb;v.Mute=s.OriginalInputMute;}File.Delete(RecoveryPath);
 }catch{/* 音量接口不可用（部分虚拟线缆）时跳过，保留恢复记录 */}}
 public sealed class State{public string Input{get;set;}="";public string Output{get;set;}="";public float OriginalInputDb{get;set;}public bool OriginalInputMute{get;set;}public float OriginalOutputDb{get;set;}public bool OriginalOutputMute{get;set;}public float AssignedInputDb{get;set;}public bool AssignedInputMute{get;set;}public bool HardwareOrShared{get;set;}}
}
