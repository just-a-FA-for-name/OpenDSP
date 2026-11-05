using System.IO.MemoryMappedFiles;
using System.Text.Json;
namespace OpenEQ;
internal static class NativeMusicCheck {
 public static int Run(string path,string name,bool filter=false,double frequency=180,double cut=-6.1){var report=new Dictionary<string,object>();var phases=new List<object>();try{if(filter){Preset.Range(frequency,20,20000,"测试频率");Preset.Range(cut,-24,0,"测试增益");report["filter_frequency_hz"]=frequency;}var device=AudioEngine.Devices().Single(d=>d.Name.Contains(name,StringComparison.OrdinalIgnoreCase));AudioRestartGuard.EnsureReady(device.Id);var format=NativeDeviceFormat.Read(device.Id);report["physical_format"]=NativeFormatState.Describe(format.Endpoint);report["mix_format"]=NativeFormatState.Describe(format.Mix);report["sessions_before"]=AudioDiagnostics.Read();
   var preset=Preset.Flat();preset.Bands.Clear();preset.Output.Protection=false;using var apo=new NativeApo(device.Id,preset,false);using var telemetry=new NativeTelemetryReader(NativeApo.ConfigPath(device.Id));
   void Progress(string phase)=>File.WriteAllText(path+".progress.json",JsonSerializer.Serialize(new{phase,time=DateTimeOffset.Now}));
   Progress("waiting_for_existing_music");bool ready=false;for(int n=0;n<100;n++){double before=telemetry.InputEnergy;long frames=apo.Frames;Thread.Sleep(300);if(apo.Frames>frames&&telemetry.InputEnergy-before>1e-12){ready=true;break;}}if(!ready)throw new Exception("等待音乐播放超时；未启动音量对比。请在音频服务重建后重新播放音乐");
   foreach(double gain in new[]{0.0,filter?cut:-6.0,0.0}){Progress("music_gain_"+gain.ToString(System.Globalization.CultureInfo.InvariantCulture));if(filter){preset.Bands.Clear();preset.Bands.Add(new Band{Type="low_shelf",Frequency=frequency,Gain=gain,Q=.707});}else preset.Output.Gain=gain;apo.Update(preset);Thread.Sleep(700);long frames=apo.Frames;double input=telemetry.InputEnergy,output=telemetry.OutputEnergy;Thread.Sleep(5300);double inputDelta=telemetry.InputEnergy-input,outputDelta=telemetry.OutputEnergy-output;double? measured=inputDelta>1e-12&&outputDelta>0?10*Math.Log10(outputDelta/inputDelta):null;phases.Add(new{mode=filter?"low shelf":"output gain",requested_db=gain,measured_db=measured,input_energy=inputDelta,frames=apo.Frames-frames,health=apo.Health()});if(apo.Frames<=frames||inputDelta<=1e-12||measured==null||(filter?(gain==0?Math.Abs(measured.Value)>.1:measured.Value>0.3||measured.Value<gain-.5):Math.Abs(measured.Value-gain)>.1))throw new Exception("实际音乐流未确认或增益不匹配；不会以无声时的遥测当作通过");}
   Progress("finished");
   report["digital_result"]="PASS";return 0;
  }catch(Exception e){report["digital_result"]="FAIL";report["exception"]=e.ToString();return 1;}finally{report["phases"]=phases;report["scope"]="Existing application music only; no generated tone, endpoint visibility or format changes. Acoustic result requires user confirmation.";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}}
}


