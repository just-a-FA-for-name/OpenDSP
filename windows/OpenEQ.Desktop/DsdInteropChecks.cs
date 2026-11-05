using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
namespace OpenEQ;
/// <summary>Cross-check the actual DoP payload with an independent FFmpeg DSD decoder.</summary>
internal static class DsdInteropChecks {
 sealed class Tones(int rate):ISampleProvider {
  long frame;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(rate,2);
  public int Read(float[] b,int offset,int count){for(int i=offset;i+1<offset+count;i+=2){b[i]=(float)(.1*Math.Sin(2*Math.PI*997*frame/rate));b[i+1]=(float)(.1*Math.Sin(2*Math.PI*3001*frame/rate));frame++;}return count;}
 }
 static void Dsf(string path,byte[] dop,int rate){
  int frames=dop.Length/6,bytesPerChannel=frames*2,blocks=(bytesPerChannel+4095)/4096,padded=blocks*4096;long total=28+52+12+padded*2L;
  using var stream=File.Create(path);using var w=new BinaryWriter(stream,Encoding.ASCII);w.Write(Encoding.ASCII.GetBytes("DSD "));w.Write(28L);w.Write(total);w.Write(0L);
  w.Write(Encoding.ASCII.GetBytes("fmt "));w.Write(52L);w.Write(1u);w.Write(0u);w.Write(2u);w.Write(2u);w.Write((uint)(rate*16));w.Write(8u);w.Write(frames*16L);w.Write(4096u);w.Write(0u);
  w.Write(Encoding.ASCII.GetBytes("data"));w.Write(12+padded*2L);
  for(int block=0;block<blocks;block++)for(int ch=0;ch<2;ch++)for(int i=0;i<4096;i++){
   int byteIndex=block*4096+i;if(byteIndex>=bytesPerChannel){w.Write((byte)0x69);continue;}
   int word=byteIndex/2; // DSF MSBF stores chronological bytes: t0..t7, then t8..t15.
   w.Write(dop[word*6+ch*3+((byteIndex&1)==0?1:0)]);
  }
 }
 static double Magnitude(float[] data,int channel,int rate,double frequency){double re=0,im=0;int start=4096,frames=data.Length/2-start;for(int i=0;i<frames;i++){double phase=2*Math.PI*frequency*i/rate,x=data[(i+start)*2+channel];re+=x*Math.Cos(phase);im+=x*Math.Sin(phase);}return 2*Math.Sqrt(re*re+im*im)/frames;}
 internal static int Run(string reportPath,string ffmpeg,int seconds=1){var report=new Dictionary<string,object>();try{
  if(seconds<1||seconds>120)throw new ArgumentOutOfRangeException(nameof(seconds));
  foreach(string mode in seconds>1?new[]{Dsd.Dop64}:new[]{Dsd.Dop64,Dsd.Dop128}){
   int rate=Dsd.FrameRate(mode);var provider=new DsdProvider(new Tones(rate),mode);var wire=new byte[checked(rate*6*seconds)];provider.Read(wire,0,wire.Length);
   string dsf=Path.GetFullPath(reportPath+"."+mode+".dsf"),raw=dsf+".f32";Dsf(dsf,wire,rate);
   var start=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
   foreach(string arg in new[]{"-hide_banner","-loglevel","error","-y","-i",dsf,"-ar",(seconds>1?88200:rate*2).ToString(),"-f","f32le","-c:a","pcm_f32le",raw})start.ArgumentList.Add(arg);
   using var child=Process.Start(start)??throw new IOException("Could not launch independent DSD decoder");string error=child.StandardError.ReadToEnd();child.WaitForExit();if(child.ExitCode!=0)throw new IOException("Independent decoder failed: "+error);
   byte[] bytes=File.ReadAllBytes(raw);var samples=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);int decodedRate=seconds>1?88200:rate*2;
   double minimumRms=double.MaxValue,maximumRms=0;int window=decodedRate/100;for(int pos=4096;pos+window<samples.Length/2;pos+=window){double sum=0;for(int i=0;i<window;i++)sum+=samples[(pos+i)*2]*samples[(pos+i)*2];double rms=Math.Sqrt(sum/window);minimumRms=Math.Min(minimumRms,rms);maximumRms=Math.Max(maximumRms,rms);}
   if(minimumRms<.025||maximumRms>.045)throw new IOException("Decoded DSD contains a 10ms dropout or level discontinuity");
   double l=Magnitude(samples,0,decodedRate,997),r=Magnitude(samples,1,decodedRate,3001),wrongL=Magnitude(samples,0,decodedRate,3001),wrongR=Magnitude(samples,1,decodedRate,997);
   report[mode]=new{seconds,minimum_10ms_rms=minimumRms,maximum_10ms_rms=maximumRms,decoded_rate=decodedRate,left_amplitude=l,right_amplitude=r,left_wrong_tone=wrongL,right_wrong_tone=wrongR,finite=samples.All(float.IsFinite),dsf};
   if(!samples.All(float.IsFinite)||Math.Abs(l-.05)>.01||Math.Abs(r-.05)>.01||wrongL>.001||wrongR>.001)throw new IOException("Independent DSD decode did not reproduce the expected channel/frequency/amplitude at 50% reference level");
  }report["result"]="PASS";return 0;
 }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}finally{report["scope"]="Offline real DoP payload exported to DSF and independently decoded by installed FFmpeg; no hardware output";File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}}
}
