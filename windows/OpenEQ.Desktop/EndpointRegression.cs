using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace OpenEQ;
internal static class EndpointRegression {
 public static int Run(string path,string name,bool polling=false){var report=new Dictionary<string,object>();var cases=new List<object>();using var en=new MMDeviceEnumerator();var engine=new AudioEngine();
  try{var list=AudioEngine.Devices();var dst=list.Single(d=>d.Name.Contains(name,StringComparison.OrdinalIgnoreCase));var src=list.First(d=>d.Name.Contains("Steam Streaming Speakers"));using var input=en.GetDevice(src.Id);var caps=DeviceFormats.Read(dst.Id);report["device"]=dst;report["capabilities"]=caps;report["endpoints_before"]=AudioDiagnostics.Read();
   var testCases=caps.Exclusive.Where(f=>f.Rate>=44100).OrderBy(f=>f.Rate).ThenBy(f=>f.Bits).Select(f=>(f.Rate,f.Bits,true)).ToList();testCases.Add((caps.MixRate,caps.MixBits,false));
   foreach(var c in testCases){float beforeDb=input.AudioEndpointVolume.MasterVolumeLevel;bool beforeMute=input.AudioEndpointVolume.Mute;var preset=Preset.Flat();preset.Bands.Clear();preset.Bands.Add(new Band{Frequency=1000,Gain=-12,Q=2,Channel="left"});
    try{engine.Start(src.Id,dst.Id,preset,c.Item1,c.Item3,true,c.Item2,polling&&c.Item3);using var tone=new WasapiOut(input,AudioClientShareMode.Shared,true,60);tone.Init(new StereoTone().ToWaveProvider());tone.Play();Thread.Sleep(500);double il=engine.EnergyInLeft,ir=engine.EnergyInRight,ol=engine.EnergyOutLeft,orr=engine.EnergyOutRight;Thread.Sleep(1100);if(engine.LastError!=null)throw new Exception(engine.LastError);
     double left=10*Math.Log10((engine.EnergyOutLeft-ol)/(engine.EnergyInLeft-il)),right=10*Math.Log10((engine.EnergyOutRight-orr)/(engine.EnergyInRight-ir));long rendered=engine.RenderedBytes;string format=engine.FormatLabel;var volumeMode=engine.VolumeMode;Console.WriteLine($"{c.Item1} Hz / {c.Item2} bit {(c.Item3?"exclusive":"shared")} {(polling?"polling":"event")} L={left:F3} R={right:F3}");
     if(!double.IsFinite(left)||Math.Abs(left+12)>.15||!double.IsFinite(right)||Math.Abs(right)>.02)throw new Exception($"Unexpected EQ gain L={left} R={right}");if(engine.OverflowEvents!=0||engine.Clipped!=0||rendered<=0)throw new Exception("Render clock did not advance, or overflow/clipping occurred");tone.Stop();engine.Dispose();
     if(Math.Abs(input.AudioEndpointVolume.MasterVolumeLevel-beforeDb)>.05||input.AudioEndpointVolume.Mute!=beforeMute)throw new Exception("Virtual input volume leaked across a restart");
     cases.Add(new{rate=c.Item1,bits=c.Item2,exclusive=c.Item3,format,left_db=left,right_db=right,rendered_bytes=rendered,virtual_volume_restored=true,volume_mode=volumeMode,result="PASS"});
    }catch(Exception ex){engine.Dispose();cases.Add(new{rate=c.Item1,bits=c.Item2,exclusive=c.Item3,result="FAIL",error=ex.ToString()});throw;}
   }report["endpoints_after"]=AudioDiagnostics.Read();report["result"]="PASS";return 0;
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}
  finally{engine.Dispose();report["cases"]=cases;report["scope"]="Real driver initialization, render clock, digital DSP and repeated start/stop; acoustic DAC output requires listening or analog measurement.";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}
 }
 sealed class StereoTone:ISampleProvider{long frame;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);public int Read(float[] buffer,int offset,int count){for(int n=offset;n+1<offset+count;n+=2){buffer[n]=(float)(.003*Math.Sin(2*Math.PI*1000*frame/48000));buffer[n+1]=(float)(.003*Math.Sin(2*Math.PI*3000*frame++/48000));}return count;}}
}
