using NAudio.Wave;
using System.Text.Json;
namespace OpenEQ;
internal static class ResamplerChecks {
 public static int Run(string path){var cases=new List<object>();var report=new Dictionary<string,object>();try{
  foreach(int taps in new[]{64,256})foreach(int outputRate in new[]{44100,48000,96000,192000,384000})foreach(double frequency in new[]{1000d,10000,16000,18000,20000,22000}){
   var source=new Sine(frequency);var resampler=new ClockedResampler(source,outputRate,()=>.1,taps,taps==64?32:256);var data=new float[outputRate*2];var scratch=new float[outputRate/5*2];int skipped=0;while(skipped<outputRate/5){int n=resampler.Read(scratch,0,Math.Min(scratch.Length,(outputRate/5-skipped)*2));if(n<=0)throw new Exception("ASRC returned no startup data");skipped+=n/2;}
   int received=0;while(received<data.Length){int n=resampler.Read(data,received,Math.Min(2048,data.Length-received));if(n<=0)throw new Exception("ASRC returned no data");received+=n;}
   double sine=0,cosine=0,power=0;for(int i=0;i<outputRate;i++){double x=data[i*2],w=2*Math.PI*frequency*i/outputRate;sine+=x*Math.Sin(w);cosine+=x*Math.Cos(w);power+=x*x;}double a=sine*2/outputRate,b=cosine*2/outputRate,amplitude=Math.Sqrt(a*a+b*b);double error=0;for(int i=0;i<outputRate;i++){double w=2*Math.PI*frequency*i/outputRate;double d=data[i*2]-a*Math.Sin(w)-b*Math.Cos(w);error+=d*d;}
   cases.Add(new{taps,phases=taps==64?32:256,input_rate=48000,output_rate=outputRate,frequency_hz=frequency,gain_db=20*Math.Log10(amplitude/.1),residual_db=10*Math.Log10(Math.Max(error,1e-30)/power),clock_correction_ppm=resampler.CorrectionPpm});
  }report["result"]="MEASURED";return 0;
 }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}finally{report["cases"]=cases;report["scope"]="Offline full-band sine projection at a fixed ideal clock. Residual includes harmonics and imaging; excludes actual device clocks, driver and acoustic output.";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}}
 sealed class Sine:ISampleProvider {readonly double frequency;long frame;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);public Sine(double frequency){this.frequency=frequency;}public int Read(float[] b,int offset,int count){for(int i=offset;i+1<offset+count;i+=2){float x=(float)(.1*Math.Sin(2*Math.PI*frequency*frame++/48000));b[i]=x;b[i+1]=x;}return count;}}
}
