using System.Diagnostics;
using System.Text.Json;
using NAudio.Dsp;
using NAudio.Wave;
namespace OpenEQ;
internal static class AdaptiveResamplerChecks {
 public static int Run(string path){var checks=new List<string>();var report=new Dictionary<string,object>();
  void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
  try{
   // Exercise the actual downsampling ratio and changing clock correction with
   // varying short callback sizes, rather than a fixed-rate offline converter.
   double level=.1;var converter=new ClockedResampler(new Tone(1000),176400,()=>level);
   var data=new float[8192];for(int i=0;i<100;i++){level=.1+.02*Math.Sin(i*.02);converter.Read(data,0,704+(i%9)*2);}
   long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();long frames=0;
   for(int i=0;i<20000;i++){level=.1+.02*Math.Sin(i*.02);int count=704+(i%9)*2;int n=converter.Read(data,0,count);CheckRead(n,count);frames+=n/2;}
   watch.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
   report["adaptive_render"]=new{audio_seconds=frames/176400d,cpu_seconds=watch.Elapsed.TotalSeconds,allocated_bytes=allocated,filter_builds=converter.FilterBuilds,correction_ppm=converter.CorrectionPpm};
   // Stopwatch allocation is excluded from the zero-allocation measurement below.
   allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<2000;i++){level=.1+.02*Math.Sin(i*.02);converter.Read(data,0,704+(i%9)*2);}allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
   Check(allocated==0,"adaptive_short_reads_zero_allocations");Check(converter.FilterBuilds==1,"sinc_table_built_once_despite_clock_changes");
   Check(watch.Elapsed.TotalSeconds<frames/176400d*.7,"adaptive_resampler_has_realtime_headroom");
   // Compare directly against the dependency's original implementation to
   // quantify the previously hidden per-read coefficient rebuilding cost.
   var original=new WdlResampler();original.SetMode(true,2,true,256,256);original.SetFeedMode(false);
   allocated=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
   for(int i=0;i<1000;i++){original.SetRates(384000*(1+.0001*Math.Sin(i*.02)),176400);int needed=original.ResamplePrepare(352+i%9,2,out var input,out int start);Array.Clear(input,start,needed*2);original.ResampleOut(data,0,needed,352+i%9,2);}
   watch.Stop();report["upstream_1000_callbacks"]=new{seconds=watch.Elapsed.TotalSeconds,allocated_bytes=GC.GetAllocatedBytesForCurrentThread()-allocated};
   foreach(double hz in new[]{1000d,20000,60000,110000}){
    converter=new ClockedResampler(new Tone(hz),176400,()=>.1);for(int i=0;i<100;i++)converter.Read(data,0,2048);
    double power=0;int samples=0;for(int i=0;i<180;i++){int n=converter.Read(data,0,2048);for(int j=0;j<n;j+=2){power+=data[j]*data[j];samples++;}}
    double db=10*Math.Log10(Math.Max(1e-30,power/samples)/(.1*.1/2));report[$"tone_{hz}_gain_db"]=db;
    Check(hz<88000?Math.Abs(db)<.05:db< -85,$"tone_{hz}_passband_or_alias_rejection");
   }
   foreach(bool burstAware in new[]{false,true}){
    const int burst=28672;double duration=burst/176400d;var fifo=new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(384000,2)){BufferDuration=TimeSpan.FromSeconds(1.5)};
    var feed=new byte[384000*8/2];fifo.AddSamples(feed,0,(int)(384000*(burstAware?.1+2*duration:duration+.04))*8);long missing=0;
    var padded=new PaddedCaptureProvider(fifo,n=>missing+=n);converter=new ClockedResampler(padded.ToSampleProvider(),176400,()=>fifo.BufferedDuration.TotalSeconds){TargetBufferSeconds=.1+(burstAware?duration:0)};
    double fractional=0;for(int cycle=0;cycle<1000;cycle++){
     int remaining=burst;while(remaining>0){int framesNow=Math.Min(4096,remaining);converter.Read(data,0,framesNow*2);remaining-=framesNow;}
     double jitter=384000*.025*Math.Sin(cycle*.73);fractional+=384000*duration+jitter-384000*.025*Math.Sin((cycle-1)*.73);int supplied=(int)fractional;fractional-=supplied;fifo.AddSamples(feed,0,supplied*8);
    }
    report[burstAware?"event_burst_aware":"event_old_fixed_target"]=new{simulated_seconds=1000*duration,missing_bytes=missing,final_fifo_ms=fifo.BufferedDuration.TotalMilliseconds};
    Check(burstAware?missing==0:missing>0,burstAware?"163ms_event_callbacks_no_capture_underflow":"old_100ms_target_reproduces_periodic_underflow");
   }
   report["result"]="PASS";return 0;
  }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}
  finally{report["checks"]=checks;report["scope"]="Offline 384k to 176.4k adaptive sinc CPU/allocation and passband/alias tests; hardware continuity requires live soak";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
 }
 static void CheckRead(int n,int count){if(n!=count)throw new Exception("Short adaptive read");}
 sealed class Tone(double hz):ISampleProvider {
  long frame;public WaveFormat WaveFormat{get;}=WaveFormat.CreateIeeeFloatWaveFormat(384000,2);
  public int Read(float[] b,int offset,int count){for(int i=0;i<count;i+=2){float x=(float)(.1*Math.Sin(2*Math.PI*hz*frame++/384000));b[offset+i]=x;b[offset+i+1]=x;}return count;}
 }
}
