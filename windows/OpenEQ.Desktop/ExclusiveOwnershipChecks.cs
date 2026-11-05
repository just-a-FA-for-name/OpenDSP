using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace OpenEQ;
/// <summary>
/// Fixed-format USB exclusive ownership proof. Opens one exclusive stream at the
/// device's current baseline format only; never rotates rates or changes the
/// persistent Windows device format.
/// </summary>
internal static class ExclusiveOwnershipChecks {
 const int BaselineRate=48000,BaselineBits=32;
 public static int Run(string path,string name){var report=new Dictionary<string,object>();var cases=new List<string>();void Check(bool yes,string label){if(!yes)throw new Exception(label);cases.Add(label);}
  try{
   var device=AudioEngine.Devices().FirstOrDefault(d=>d.Name.Contains(name,StringComparison.OrdinalIgnoreCase))??throw new Exception("未找到输出设备 "+name);
   report["device"]=device;var current=NativeDeviceFormat.Read(device.Id);var physical=NativeFormatState.Describe(current.Endpoint);report["physical_format_before"]=physical;
   Check(physical.Rate==BaselineRate&&physical.Bits==BaselineBits,"baseline_must_be_48000_32_refusing_to_change_formats");
   AudioRestartGuard.EnsureReady(device.Id);AudioActivityGuard.EnsureIdle(device.Id);
   Check(ExclusiveOwnership.SharedInitializationError(device.Id)==null,"shared_path_available_before_test");
   var format=DeviceFormats.Require(device.Id,BaselineRate,BaselineBits);report["exclusive_format"]=NativeFormatState.Describe(NativeDeviceFormat.Backend.Encode(format));
   long rendered;string label;using(var en=new MMDeviceEnumerator())using(var mm=en.GetDevice(device.Id))using(var output=new WasapiOut(mm,AudioClientShareMode.Exclusive,false,80)){
    output.Init(new SilenceProvider(format));output.Play();Check(output.OutputWaveFormat.SampleRate==BaselineRate&&output.OutputWaveFormat.BitsPerSample==BaselineBits&&output.OutputWaveFormat.Channels==2,"requested_format_preserved_by_driver");
    label=output.OutputWaveFormat.SampleRate+" Hz / "+output.OutputWaveFormat.BitsPerSample+"-bit / "+output.OutputWaveFormat.Channels+"ch exclusive";
    Thread.Sleep(1500);rendered=output.GetPosition();Check(rendered>0,"exclusive_render_clock_advanced");
    ExclusiveOwnership.Confirm(device.Id);Check(ExclusiveOwnership.SharedInitializationError(device.Id)==ExclusiveOwnership.DeviceInUse,"shared_client_blocked_with_device_in_use");
    output.Stop();
   }
   Check(ExclusiveOwnership.SharedInitializationError(device.Id)==null,"shared_path_reopened_after_release");
   report["format_label"]=label;report["rendered_bytes"]=rendered;
   report["result"]="PASS";report["note"]="真实设备所有权已核验；全应用声音仍依赖自有虚拟入口驱动，本检查不覆盖。";return 0;
  }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}
  finally{report["cases"]=cases;report["scope"]="单条 48 kHz / 32-bit 独占流：不轮换采样率、不修改持久设备格式、不安装/卸载 APO、不刷新音频服务";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}}
}
