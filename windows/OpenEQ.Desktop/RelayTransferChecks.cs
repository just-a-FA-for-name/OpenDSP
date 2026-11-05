using NAudio.Wave;
using System.Text.Json;
namespace OpenEQ;
/// <summary>
/// Offline probe for the relay chain (entry 192k → upsample 384k → DSP):
/// verifies per-channel EQ transfer AND output frequency preservation.
/// Catches channel swaps / pitch shifts that device tests can only guess at.
/// </summary>
internal static class RelayTransferChecks {
 public static int Run(string path){var report=new Dictionary<string,object>();var cases=new List<string>();void Check(bool yes,string name){if(!yes)throw new Exception(name);cases.Add(name);}
  try{
   foreach(var targetRate in new[]{48000,192000,384000}){foreach(bool viaResampler in new[]{false,true}){
    int inputRate=192000;int analysisRate=viaResampler?targetRate:inputRate;var preset=Preset.Flat();preset.Preamp=-6;preset.Output.Gain=-24;preset.Bands.Clear();preset.Bands.Add(new Band{Type="peak",Frequency=1000,Gain=-12,Q=2,Channel="left"});
    var source=new DualTone(inputRate);
    ISampleProvider stream=source;ClockedResampler? clock=null;
    if(viaResampler){clock=new ClockedResampler(source,targetRate,()=>.1);stream=clock;}
    var dsp=new DspProvider(stream,preset);
    // warm-up (resampler latency), then integrate exactly one second
    var warm=new float[analysisRate*2/10*2];dsp.Read(warm,0,warm.Length);
    var data=new float[analysisRate*2];int got=0;while(got<data.Length){int n=dsp.Read(data,got,data.Length-got);if(n<=0)throw new Exception("no data");got+=n;}
    double mag1L=Goertzel(data,0,analysisRate,1000),mag3L=Goertzel(data,0,analysisRate,3000),mag1R=Goertzel(data,1,analysisRate,1000),mag3R=Goertzel(data,1,analysisRate,3000);
    double transferL=10*Math.Log10(dsp.EnergyOutLeft/dsp.EnergyInLeft),transferR=10*Math.Log10(dsp.EnergyOutRight/dsp.EnergyInRight);
    // Expected: left = preamp+output+band(1k) = -42 at 1k tone; right = -30 at 3k tone.
    // Frequency preservation: left output tone must live at 1k (mag1L >> mag3L), right at 3k.
    bool freqOk=mag1L>mag3L*10&&mag3R>mag1R*10;
    bool swap=(mag3L>mag1L*10)||(mag1R>mag3R*10);
    report[$"rate_{targetRate}_{(viaResampler?"resample":"direct")}"]=new{left_db=transferL,right_db=transferR,left_1k_db=20*Math.Log10(Math.Max(mag1L,1e-12)),left_3k_db=20*Math.Log10(Math.Max(mag3L,1e-12)),right_1k_db=20*Math.Log10(Math.Max(mag1R,1e-12)),right_3k_db=20*Math.Log10(Math.Max(mag3R,1e-12)),frequency_preserved=freqOk,channel_swapped=swap};
    Check(freqOk,$"rate_{targetRate}_{(viaResampler?"resample":"direct")}_frequency_preserved");
    Check(!swap,$"rate_{targetRate}_{(viaResampler?"resample":"direct")}_channels_not_swapped");
    Check(Math.Abs(transferL+42)<.25,$"rate_{targetRate}_{(viaResampler?"resample":"direct")}_left_transfer");
    Check(Math.Abs(transferR+30)<.25,$"rate_{targetRate}_{(viaResampler?"resample":"direct")}_right_transfer");
   }}
   report["result"]="PASS";
   // Device-faithful variant: bursty 192k input through a BufferedWaveProvider (like WASAPI
   // loopback), active clock servo with fluctuating buffer levels, PCM32 round-trip.
   foreach(var targetRate in new[]{48000,384000}){var preset=Preset.Flat();preset.Preamp=-6;preset.Output.Gain=-24;preset.Bands.Clear();preset.Bands.Add(new Band{Type="peak",Frequency=1000,Gain=-12,Q=2,Channel="left"});
    int inputRate=192000;var buffer=new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(inputRate,2)){BufferDuration=TimeSpan.FromMilliseconds(1500),DiscardOnBufferOverflow=false,ReadFully=true};
    var feeder=new DualTone(inputRate);int inPerIter=inputRate/50;var feed=new float[inPerIter*2];int phase=0;
    ISampleProvider stream=buffer.ToSampleProvider();ClockedResampler? clock=null;double fakeLevel=.2;clock=new ClockedResampler(stream,targetRate,()=>fakeLevel);stream=clock;
    var dsp=new DspProvider(stream,preset);var pcm=new PcmProvider(dsp,32,DeviceFormats.Create(targetRate,32,true));
    // realistic bursty input: 20 ms capture bursts matched to consumption, 200 ms
    // pre-fill, ~100 ppm clock skew (rare single-frame underfeed), no starvation.
    for(int i=0;i<10;i++){int n=feeder.Read(feed,0,inPerIter*2);buffer.AddSamples(ToBytes(feed,n),0,n*4);}
    var bytes=new byte[targetRate*2*4*2];var decoded=new float[targetRate*2*2];
    int byteCount=0;int chunk=targetRate/50*2;
    while(byteCount<bytes.Length){
     int n=feeder.Read(feed,0,inPerIter*2);int feedNow=(phase++%500==499)?n-4:n;
     buffer.AddSamples(ToBytes(feed,feedNow),0,feedNow*4);
     fakeLevel=buffer.BufferedDuration.TotalSeconds;
     int want=Math.Min(bytes.Length-byteCount,chunk*4);int got=pcm.Read(bytes,byteCount,want);if(got<=0)throw new Exception("no pcm data");byteCount+=got;
    }
    for(int i=0;i<decoded.Length;i++)decoded[i]=(float)(BitConverter.ToInt32(bytes,i*4)/2147483648.0);
    double mag1L=Goertzel(decoded,0,targetRate,1000),mag3L=Goertzel(decoded,0,targetRate,3000),mag1R=Goertzel(decoded,1,targetRate,1000),mag3R=Goertzel(decoded,1,targetRate,3000);
    double transferL=10*Math.Log10(dsp.EnergyOutLeft/dsp.EnergyInLeft),transferR=10*Math.Log10(dsp.EnergyOutRight/dsp.EnergyInRight);
    bool freqOk=mag1L>mag3L*10&&mag3R>mag1R*10;bool swap=(mag3L>mag1L*10)||(mag1R>mag3R*10);
    report[$"bursty_{targetRate}"]=new{left_db=transferL,right_db=transferR,frequency_preserved=freqOk,channel_swapped=swap};
    Check(freqOk,$"bursty_{targetRate}_frequency_preserved");Check(!swap,$"bursty_{targetRate}_channels_not_swapped");
    Check(Math.Abs(transferL+42)<.5,$"bursty_{targetRate}_left_transfer");Check(Math.Abs(transferR+30)<.5,$"bursty_{targetRate}_right_transfer");
   }
   return 0;
  }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}
  finally{report["cases"]=cases;report["scope"]="Offline synthetic dual-tone through relay math; no devices, no routing";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));}}
 static double Goertzel(float[] data,int channel,int rate,double frequency){double w=2*Math.PI*frequency/rate,coeff=2*Math.Cos(w),s0=0,s1=0,s2=0;int frames=data.Length/2;for(int n=0;n<frames;n++){s0=data[n*2+channel]+coeff*s1-s2;s2=s1;s1=s0;}double power=s1*s1+s2*s2-coeff*s1*s2;return Math.Sqrt(Math.Max(power,0))*2/frames;}
 static byte[] ToBytes(float[] samples,int count){var bytes=new byte[count*4];Buffer.BlockCopy(samples,0,bytes,0,count*4);return bytes;}
 sealed class DualTone:ISampleProvider {readonly double rate;long frame;public DualTone(int rate){this.rate=rate;WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(rate,2);}public WaveFormat WaveFormat{get;}
  public int Read(float[] b,int offset,int count){for(int i=offset;i+1<offset+count;i+=2){b[i]=(float)(.003*Math.Sin(2*Math.PI*1000*frame/rate));b[i+1]=(float)(.003*Math.Sin(2*Math.PI*3000*frame/rate));frame++;}return count;}}
}
