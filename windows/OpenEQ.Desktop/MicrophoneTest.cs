using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace OpenEQ;
internal static class MicrophoneTest {
 public static int Run(string path,int seconds){var log=new Dictionary<string,object>();WindowsListen.ListenInfo? original=null;var engine=new AudioEngine();WasapiCapture? mic=null;long bytes=0;double peak=0;string? error=null;
  try{original=WindowsListen.ReadAll().Single(x=>x.Name.Contains("Tears DSP",StringComparison.OrdinalIgnoreCase));log["before"]=original;WindowsListen.SetEnabled(original.Id,true);var enabled=WindowsListen.ReadAll().Single(x=>x.Id==original.Id);if(!enabled.Enabled)throw new Exception("Windows did not enable Listen-to-this-device");
   var devices=AudioEngine.Devices();var source=devices.First(d=>d.Name.Contains("Steam Streaming Speakers"));var output=devices.First(d=>d.Name.Contains("Tears DSP",StringComparison.OrdinalIgnoreCase));var preset=Preset.Flat();preset.Output.Gain=-24;
   engine.Start(source.Id,output.Id,preset,48000,true,true);var during=WindowsListen.ReadAll().Single(x=>x.Id==original.Id);log["during"]=during;if(during.Target!=source.Id||!during.Enabled)throw new Exception("Windows Listen target not redirected into OpenEQ");
   using var en=new MMDeviceEnumerator();using var device=en.GetDevice(original.Id);mic=new WasapiCapture(device);log["capture_format"]=mic.WaveFormat.ToString();mic.DataAvailable+=(s,e)=>{Interlocked.Add(ref bytes,e.BytesRecorded);if(mic.WaveFormat.BitsPerSample==32)for(int i=0;i+3<e.BytesRecorded;i+=4)peak=Math.Max(peak,Math.Abs(BitConverter.ToSingle(e.Buffer,i)));else if(mic.WaveFormat.BitsPerSample==16)for(int i=0;i+1<e.BytesRecorded;i+=2)peak=Math.Max(peak,Math.Abs(BitConverter.ToInt16(e.Buffer,i)/32768.0));};mic.RecordingStopped+=(s,e)=>{if(e.Exception!=null)error=e.Exception.Message;};mic.StartRecording();Thread.Sleep(seconds*1000);mic.StopRecording();
   log["capture_bytes_while_output_exclusive"]=bytes;log["microphone_peak"]=peak;log["open_eq_input_energy"]=engine.EnergyBefore;log["processed_frames"]=engine.Frames;log["active_loopback_sessions"]=engine.ActiveInputSessions();log["overflow_events"]=engine.OverflowEvents;log["clipped_frames"]=engine.Clipped;log["format"]=engine.FormatLabel;log["capture_error"]=error??"none";
   int sessionsEnabled=engine.ActiveInputSessions();WindowsListen.SetEnabled(original.Id,false);Thread.Sleep(600);int sessionsDisabled=engine.ActiveInputSessions();log["native_listen_session_disappeared_when_disabled"]=sessionsDisabled<sessionsEnabled;log["sessions_after_listen_disabled"]=sessionsDisabled;
   if(sessionsDisabled>=sessionsEnabled)throw new Exception("Could not attribute incoming stream to Windows Listen: disabling it did not remove its audio session");
   if(bytes==0||error!=null)throw new Exception("Microphone capture unavailable during exclusive output");if(engine.OverflowEvents!=0||engine.Clipped!=0)throw new Exception("Audio transport overflow or clipping");log["signal_detected"]=peak>1e-7&&engine.EnergyBefore>1e-10;log["result"]=peak>1e-7&&engine.EnergyBefore>1e-10?"PASS":"INCOMPLETE_SILENT_MIC";return (string)log["result"]=="PASS"?0:2;
  }catch(Exception ex){log["result"]="FAIL";log["exception"]=ex.ToString();return 1;}
  finally{try{mic?.Dispose();}catch{}engine.Dispose();if(original!=null){try{WindowsListen.RestoreSettings(original);log["after"]=WindowsListen.ReadAll().Single(x=>x.Id==original.Id);}catch(Exception ex){log["restore_error"]=ex.ToString();}}Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}
 }
}

internal static class MicrophoneListen {
 public static int Run(string path,string name,int seconds,double frequency=1000){var log=new Dictionary<string,object>();
  try{using var en=new MMDeviceEnumerator();var dev=en.EnumerateAudioEndPoints(DataFlow.Capture,DeviceState.Active).First(d=>d.FriendlyName.Contains(name,StringComparison.OrdinalIgnoreCase));
   using var mic=new WasapiCapture(dev);long bytes=0;double peak=0;double energy=0;double w=2*Math.PI*frequency/mic.WaveFormat.SampleRate,coeff=2*Math.Cos(w),s1=0,s2=0;long samples=0;
   mic.DataAvailable+=(s,e)=>{Interlocked.Add(ref bytes,e.BytesRecorded);if(mic.WaveFormat.BitsPerSample==32)for(int n=0;n+3<e.BytesRecorded;n+=4){double v=BitConverter.ToSingle(e.Buffer,n);peak=Math.Max(peak,Math.Abs(v));energy+=v*v;double s0=v+coeff*s1-s2;s2=s1;s1=s0;samples++;}else if(mic.WaveFormat.BitsPerSample==16)for(int n=0;n+1<e.BytesRecorded;n+=2){double v=BitConverter.ToInt16(e.Buffer,n)/32768.0;peak=Math.Max(peak,Math.Abs(v));energy+=v*v;double s0=v+coeff*s1-s2;s2=s1;s1=s0;samples++;}};
   mic.StartRecording();Thread.Sleep(seconds*1000);mic.StopRecording();
   double power=s1*s1+s2*s2-coeff*s1*s2;double toneAmp=Math.Sqrt(Math.Max(power,0))*2/Math.Max(1,samples);double rms=Math.Sqrt(energy/Math.Max(1,samples));
   double micDb=dev.AudioEndpointVolume.MasterVolumeLevel;bool micMute=dev.AudioEndpointVolume.Mute;
   log["device"]=dev.FriendlyName;log["mic_volume_db"]=micDb;log["mic_mute"]=micMute;log["bytes"]=bytes;log["peak"]=peak;log["rms"]=rms;log["rms_db"]=20*Math.Log10(Math.Max(rms,1e-12));log["tone_hz"]=frequency;log["tone_amplitude"]=toneAmp;log["tone_db"]=20*Math.Log10(Math.Max(toneAmp,1e-12));log["signal_detected"]=rms>2e-4;log["result"]=rms>2e-4?"PASS":"SILENT";return 0;
  }catch(Exception ex){log["result"]="FAIL";log["exception"]=ex.ToString();return 1;}
  finally{File.WriteAllText(path,JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true}));}}
 }
