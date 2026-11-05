using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace OpenEQ;
internal static class BluetoothTest {
 public static int Run(string path){var report=new Dictionary<string,object>();var engine=new AudioEngine();var children=new List<Process>();WasapiLoopbackCapture? rendered=null;long bytes=0;double energy=0;string? original=null;using var en=new MMDeviceEnumerator();
  void Check(bool value,string label){if(!value)throw new Exception(label);}
  try{
   var devices=AudioEngine.Devices();var dst=devices.First(d=>d.Transport=="蓝牙");var src=devices.First(d=>d.Name.Contains("Steam Streaming Speakers"));report["output"]=dst;
   using(var before=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))original=before.ID;
   var preset=Preset.Flat();preset.Preamp=-6;preset.Output.Gain=-24;preset.Bands.Clear();preset.Bands.Add(new Band{Type="peak",Frequency=1000,Gain=-12,Q=2,Channel="left"});
   using var endpoint=en.GetDevice(dst.Id);rendered=new WasapiLoopbackCapture(endpoint);Check(rendered.WaveFormat.Encoding==WaveFormatEncoding.IeeeFloat||rendered.WaveFormat is WaveFormatExtensible,"Bluetooth capture format");
   rendered.DataAvailable+=(s,a)=>{Interlocked.Add(ref bytes,a.BytesRecorded);for(int n=0;n+3<a.BytesRecorded;n+=4){var v=BitConverter.ToSingle(a.Buffer,n);energy+=v*v;}};rendered.StartRecording();
   engine.Start(src.Id,dst.Id,preset,48000,false,true);report["format"]=engine.FormatLabel;
   for(int i=0;i<2;i++){var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};foreach(var arg in new[]{"--tone",i==0?"1000":"3000",i.ToString(),"6"})start.ArgumentList.Add(arg);children.Add(Process.Start(start)!);}
   Thread.Sleep(4000);if(engine.LastError!=null)throw new Exception(engine.LastError);
   double left=10*Math.Log10(engine.EnergyOutLeft/engine.EnergyInLeft),right=10*Math.Log10(engine.EnergyOutRight/engine.EnergyInRight);
   report["left_1000hz_db"]=left;report["left_expected_db"]=-42;report["right_3000hz_db"]=right;report["right_expected_db"]=-30;
   report["render_loopback_bytes"]=bytes;report["render_loopback_energy"]=energy;report["frames"]=engine.Frames;report["overflows"]=engine.OverflowEvents;report["clipped_frames"]=engine.Clipped;
   Check(Math.Abs(left+42)<.3&&Math.Abs(right+30)<.3,"Real Bluetooth-route DSP response");Check(bytes>48000&&energy>0,"No samples delivered to Bluetooth render endpoint");Check(engine.OverflowEvents==0&&engine.Clipped==0,"Buffer overflow or clipping");
   engine.Dispose();using(var after=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))Check(original==after.ID,"Default route restoration");report["default_restored"]=true;report["scope"]="Windows Bluetooth render endpoint and digital DSP; codec and acoustic response are not measured.";report["result"]="PASS";return 0;
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}
  finally{foreach(var p in children){try{if(!p.WaitForExit(3500))p.Kill();}catch{}p.Dispose();}engine.Dispose();try{rendered?.StopRecording();}catch{}rendered?.Dispose();File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}
 }
}
