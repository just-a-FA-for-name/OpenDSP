using System.Diagnostics;
using NAudio.CoreAudioApi;
namespace OpenEQ;
internal static class AudioActivityGuard {
 public static IReadOnlyList<string> Active(string id){using var enumerator=new MMDeviceEnumerator();using var device=enumerator.GetDevice(id);var sessions=device.AudioSessionManager.Sessions;var active=new HashSet<string>();for(int n=0;n<sessions.Count;n++){var session=sessions[n];if(session.State!=NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)continue;uint pid=session.GetProcessID;string name=pid==0?"Windows 系统声音":"PID "+pid;try{if(pid!=0){using var process=Process.GetProcessById((int)pid);name=process.ProcessName;}}catch(ArgumentException){}catch(System.ComponentModel.Win32Exception){}active.Add(name);}return active.ToArray();}
 public static void EnsureIdle(string id){var active=Active(id);if(active.Count>0)throw new InvalidOperationException("设备仍在播放音频："+string.Join("、",active)+"。请先暂停播放，再修改系统设备格式；当前格式未改变");}
}
