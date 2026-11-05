using NAudio.Wave;
using System.Text.Json;
namespace OpenEQ;
internal static class DsdOutputChecks {
 public static int Run(string path){var report=new Dictionary<string,object>();var cases=new List<string>();void Check(bool yes,string name){if(!yes)throw new Exception(name);cases.Add(name);}
  // DoP 1.1 榛勯噾鏍锋湰锛氭寜瑙勮寖鎵嬬畻閫愬瓧鑺傞攣姝荤嚎鏍煎紡锛堢嫭绔嬩簬琚祴浠ｇ爜锛岄槻绯荤粺鎬т綅搴?甯冨眬閿欒锛?  var g=new byte[24];DopFrame.Pack(0x8000,0x0001,0x05,g,0,3);
  var g=new byte[24];DopFrame.Pack(0x8000,0x0001,0x05,g,0,3);
  Check(g[0]==0x00&&g[1]==0x80&&g[2]==0x05&&g[3]==0x01&&g[4]==0x00&&g[5]==0x05,"golden_24bit_t0_is_msb_of_word");
  DopFrame.Pack(0x1234,0xABCD,0xFA,g,6,3);
  Check(g[6]==0x34&&g[7]==0x12&&g[8]==0xFA&&g[9]==0xCD&&g[10]==0xAB&&g[11]==0xFA,"golden_24bit_little_endian_word_and_marker");
  DopFrame.Pack(0x8000,0x0001,0x05,g,12,4);
  Check(g[12]==0x00&&g[13]==0x00&&g[14]==0x80&&g[15]==0x05&&g[16]==0x00&&g[17]==0x01&&g[18]==0x00&&g[19]==0x05,"golden_32bit_left_aligned_low8_padding");
  DopFrame.Pack(0xFFFF,0x0000,0xFA,g,0,3);
  Check(g[0]==0xFF&&g[1]==0xFF&&g[2]==0xFA&&g[3]==0x00&&g[4]==0x00&&g[5]==0xFA,"golden_word_edges");
  try{
   foreach(var mode in new[]{Dsd.Dop64,Dsd.Dop128}){
    int frameRate=Dsd.FrameRate(mode);const double freq=997,amp=.5;
    var design=new DsdModulator(frameRate*16d);double actualPeak=IndependentNtfPeak(design);
    Check(actualPeak<=1.4501,$"{mode}_actual_ntf_obeys_lee_bound");
    Check(Math.Abs(actualPeak-design.NoiseTransferPeak)<.001,$"{mode}_reported_ntf_is_not_feedback_transfer");
    var clean=new DsdModulator(frameRate*16d);bool invalidRejected=false;
    try{design.Process(double.NaN);}catch(ArgumentOutOfRangeException){invalidRejected=true;}
    Check(invalidRejected,$"{mode}_nonfinite_input_rejected_before_history_changes");
    bool same=true;for(int n=0;n<4096;n++)if(design.Process(0)!=clean.Process(0))same=false;
    Check(same,$"{mode}_invalid_input_does_not_poison_later_silence");
    var limited=new DsdModulator(frameRate*16d);
    for(int n=0;n<100000;n++)limited.Process(.95*Math.Sin(2*Math.PI*997*n/(frameRate*16d)));
    Check(limited.LimitedSamples>0,$"{mode}_excess_modulation_is_bounded_and_counted");
    var full=new DsdProvider(new Sine(frameRate,freq,.95),mode);
    var fullBytes=new byte[frameRate/4*6];full.Read(fullBytes,0,fullBytes.Length);
    var fullDecoded=new double[fullBytes.Length/6];
    for(int f=0;f<fullDecoded.Length;f++)fullDecoded[f]=(2*System.Numerics.BitOperations.PopCount((uint)(fullBytes[f*6]|fullBytes[f*6+1]<<8))-16)/16d;
    Check(Math.Abs(GoertzelAmp(fullDecoded,frameRate,freq)-.475)<.01,$"{mode}_near_full_scale_pcm_retains_sine_at_safe_reference_level");
    Check(full.LimitedSamples==0,$"{mode}_headroom_avoids_limiting_normal_full_scale_tone");
    report[mode+"_design"]=new{actual_ntf_peak=actualPeak,declared_ntf_peak=design.NoiseTransferPeak,headroom_db=DsdProvider.HeadroomDb,overload_samples=limited.LimitedSamples};
    var source=new Sine(frameRate,freq,amp);var provider=new DopIntegrityProvider(new DsdProvider(source,mode));
    // collect 0.5 s of DoP frames
    int frames=frameRate/2;var bytes=new byte[frames*6];int got=0;while(got<bytes.Length){int n=provider.Read(bytes,got,bytes.Length-got);if(n<=0)throw new Exception("no DoP data");got+=n;}
    Check(provider.Verified==frames&&provider.Invalid==0,$"{mode}_final_software_marker_integrity");
    var damaged=new DopIntegrityProvider(new Damaged(new DsdProvider(new Sine(frameRate,freq,amp),mode)));bool markerRejected=false;try{damaged.Read(new byte[1024*6],0,1024*6);}catch(IOException){markerRejected=true;}
    Check(markerRejected&&damaged.Invalid==1,$"{mode}_single_bad_marker_rejected_before_submission");
    // 1) framing: markers at bytes 2 and 5 of every frame, alternating 0x05/0xFA by frame parity
    bool framingOk=true;for(int f=0;f<frames;f++){byte expect=(byte)((f&1)==0?0x05:0xFA);if(bytes[f*6+2]!=expect||bytes[f*6+5]!=expect){framingOk=false;break;}}
    Check(framingOk,$"{mode}_dop_markers_ok");
    // Odd-sized callback boundaries must be byte-identical to a continuous stream.
    var fragmented=new DsdProvider(new Sine(frameRate,freq,amp),mode);var chunks=new byte[6000];int position=0;
    foreach(int n in new[]{1,3,7,17,31,129,257,555}){
     Check(fragmented.Read(chunks,position*6,n*6)==n*6,$"{mode}_fragment_{n}_length");position+=n;
    }
    Check(chunks.AsSpan(0,position*6).SequenceEqual(bytes.AsSpan(0,position*6)),$"{mode}_odd_callback_continuity");
    // Independent standard layout: chronological t0 is bit 15, not bit 0.
    var referenceUp=new Upsampler16();var referenceMod=new DsdModulator(frameRate*16d);var sixteen=new double[16];bool wireOrder=true;
    for(int f=0;f<128;f++){
     float sample=(float)(amp*Math.Sin(2*Math.PI*freq*f/frameRate));referenceUp.Process(sample*.5,sixteen,0);
     int word=bytes[f*6]|(bytes[f*6+1]<<8);
     for(int t=0;t<16;t++)if(((word>>(15-t))&1)!=referenceMod.Process(sixteen[t]))wireOrder=false;
    }
    Check(wireOrder,$"{mode}_standard_t0_is_msb");
    var carrier32=new DsdProvider(new Sine(frameRate,freq,amp),mode,DeviceFormats.Create(frameRate,32,true));
    var wire32=new byte[128*8];Check(carrier32.Read(wire32,0,wire32.Length)==wire32.Length,$"{mode}_32bit_extensible_length");
    bool carrierOk=true;for(int f=0;f<128;f++)for(int ch=0;ch<2;ch++){
     int o=f*8+ch*4,p=f*6+ch*3;
     if(wire32[o]!=0||!wire32.AsSpan(o+1,3).SequenceEqual(bytes.AsSpan(p,3)))carrierOk=false;
    }
    Check(carrierOk,$"{mode}_32bit_left_aligned_dop_payload");
    bool rejected=false;try{_ = new DsdProvider(new Sine(48000,freq,amp),mode);}catch(ArgumentException){rejected=true;}
    Check(rejected,$"{mode}_reject_wrong_source_rate");
    // 2) unpack DSD bits per channel (MSB first, 16 bits/frame)
    var bitsL=new double[frames*16];var bitsR=new double[frames*16];
    for(int f=0;f<frames;f++){int wL=bytes[f*6]|(bytes[f*6+1]<<8),wR=bytes[f*6+3]|(bytes[f*6+4]<<8);for(int b=0;b<16;b++){bitsL[f*16+b]=(wL>>(15-b)&1)==1?1:-1;bitsR[f*16+b]=(wR>>(15-b)&1)==1?1:-1;}}
    // 3) demodulate: 16-point boxcar per frame → frame-rate signal; measure tone + residual
    double meanL=0;for(int i=0;i<bitsL.Length;i++)meanL+=bitsL[i];meanL/=bitsL.Length;
    var demod=new double[frames];for(int f=0;f<frames;f++){double s=0;for(int b=0;b<16;b++)s+=bitsL[f*16+b];demod[f]=s/16;}
    double tone=GoertzelAmp(demod,frameRate,freq);
    double toneDb=20*Math.Log10(Math.Max(tone,1e-12)/(amp*.5));
    // residual after sin/cos projection fit (amplitude AND phase), so the tone
    // itself is not mistaken for noise
    double cs=0,cc=0;for(int f=0;f<frames;f++){double w=2*Math.PI*freq*f/frameRate;cs+=demod[f]*Math.Sin(w);cc+=demod[f]*Math.Cos(w);}
    double fitA=2*cs/frames,fitB=2*cc/frames;
    double res=0;for(int f=0;f<frames;f++){double w=2*Math.PI*freq*f/frameRate;double d=demod[f]-fitA*Math.Sin(w)-fitB*Math.Cos(w);res+=d*d;}res=Math.Sqrt(res/frames);
    double resDb=20*Math.Log10(Math.Max(res,1e-12));
    // authoritative measure: FFT of the raw 1-bit stream (no folding bias)
    int nfft=1<<20;var re=new double[nfft];var im=new double[nfft];
    for(int i=0;i<nfft&&i<bitsL.Length;i++){double t=2*Math.PI*i/(nfft-1);double w=.35875-.48829*Math.Cos(t)+.14128*Math.Cos(2*t)-.01168*Math.Cos(3*t);re[i]=bitsL[i]*w;} // Blackman-Harris: -92 dB sidelobes, measurement not leakage-limited
    Fft(re,im);double fsDsd=frameRate*16.0;
    double sigP=0,noiseP=0,oobP=0,b1=0,b2=0,b3=0;
    for(int k=1;k<nfft/2;k++){double f=k*fsDsd/nfft;double p=re[k]*re[k]+im[k]*im[k];
     if(Math.Abs(f-freq)<40)sigP+=p;else if(f>=20&&f<=20000){noiseP+=p;if(f<200)b1+=p;else if(f<2000)b2+=p;else b3+=p;}else if(f>24000)oobP+=p;}
    double snrDb=10*Math.Log10(Math.Max(sigP,1e-30)/Math.Max(noiseP,1e-30));
    double oobInbandRatioDb=10*Math.Log10(Math.Max(oobP,1e-30)/Math.Max(noiseP,1e-30));
    double n1Db=10*Math.Log10(Math.Max(b1,1e-30)),n2Db=10*Math.Log10(Math.Max(b2,1e-30)),n3Db=10*Math.Log10(Math.Max(b3,1e-30));
    // out-of-band energy: raw bit toggling activity must be high (noise-shaped)
    long flips=0;for(int i=1;i<bitsL.Length;i++)if(bitsL[i]!=bitsL[i-1])flips++;
    report[mode]=new{frame_rate=frameRate,dop_markers=framingOk,dc_offset=meanL,tone_amp_db=toneDb,demod_residual_db=resDb,snr_db=snrDb,oob_vs_inband_db=oobInbandRatioDb,noise_20_200_db=n1Db,noise_200_2k_db=n2Db,noise_2k_20k_db=n3Db,bit_flip_ratio=(double)flips/(bitsL.Length-1)};
    Check(Math.Abs(meanL)<.02,$"{mode}_modulator_stable_dc");
    Check(Math.Abs(toneDb)<.5,$"{mode}_tone_amplitude_matches_50_percent_dsd_reference");
    Check(snrDb>60,$"{mode}_inband_snr"); // 5th-order shaping ⇒ >60 dB in-band SNR (typically 90+)
    Check(oobInbandRatioDb>20,$"{mode}_noise_shaped_out_of_band");
    Check((double)flips/(bitsL.Length-1)>.25,$"{mode}_high_frequency_dsd_activity");
    // Measure the real render-thread workload against an 80 ms output buffer.
    // This measures the modulator only, not end-to-end device deadlines.
    var perf=new DsdProvider(new Sine(frameRate,freq,.1),mode,DeviceFormats.Create(frameRate,32,true));
    var block=new byte[(int)(frameRate*.08)*8];perf.Read(block,0,block.Length);var timings=new List<double>();
    for(int i=0;i<20;i++){var watch=System.Diagnostics.Stopwatch.StartNew();perf.Read(block,0,block.Length);timings.Add(watch.Elapsed.TotalMilliseconds);}
    report[mode+"_render_cost"]=new{buffer_ms=80,mean_ms=timings.Average(),max_ms=timings.Max(),scope="Offline modulator cost only; hardware playback not validated"};
   }
   bool badDesignRejected=false;try{_ = new DsdModulator(2822400,ntfPeak:1.000001);}catch(ArgumentException){badDesignRejected=true;}
   Check(badDesignRejected,"unachievable_ntf_target_is_not_silently_accepted");
   report["result"]="PASS";return 0;
  }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}
  finally{report["cases"]=cases;report["scope"]="Offline PCM→DSD(DoP) stage validation; no devices";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));}}
 static double GoertzelAmp(double[] data,int rate,double frequency){double w=2*Math.PI*frequency/rate,coeff=2*Math.Cos(w),s1=0,s2=0;for(int n=0;n<data.Length;n++){double s0=data[n]+coeff*s1-s2;s2=s1;s1=s0;}double power=s1*s1+s2*s2-coeff*s1*s2;return Math.Sqrt(Math.Max(power,0))*2/data.Length;}
 static double IndependentNtfPeak(DsdModulator modulator){
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  double[] Get(string key)=>(double[])typeof(DsdModulator).GetField(key,flags)!.GetValue(modulator)!;
  var a=Get("a");var b=Get("b");
  System.Numerics.Complex At(double[] coefficients,System.Numerics.Complex z){System.Numerics.Complex value=0;for(int k=coefficients.Length-1;k>=0;k--)value=value*z+coefficients[k];return value;}
  double peak=0;for(int n=0;n<=16384;n++){var z=System.Numerics.Complex.FromPolarCoordinates(1,-Math.PI*n/16384);peak=Math.Max(peak,(1+At(b,z)/At(a,z)).Magnitude);}return peak;
 }
 static void Fft(double[] re,double[] im){int n=re.Length;for(int i=1,j=0;i<n;i++){int bit=n>>1;for(;(j&bit)!=0;bit>>=1)j^=bit;j^=bit;if(i<j){(re[i],re[j])=(re[j],re[i]);(im[i],im[j])=(im[j],im[i]);}}
  for(int len=2;len<=n;len<<=1){double ang=-2*Math.PI/len;double wr=Math.Cos(ang),wi=Math.Sin(ang);for(int i=0;i<n;i+=len){double cr=1,ci=0;for(int k=0;k<len/2;k++){double ur=re[i+k],ui=im[i+k];double vr=re[i+k+len/2]*cr-im[i+k+len/2]*ci,vi=re[i+k+len/2]*ci+im[i+k+len/2]*cr;re[i+k]=ur+vr;im[i+k]=ui+vi;re[i+k+len/2]=ur-vr;im[i+k+len/2]=ui-vi;double ncr=cr*wr-ci*wi;ci=cr*wi+ci*wr;cr=ncr;}}}}
 sealed class Damaged(IWaveProvider source):IWaveProvider {public WaveFormat WaveFormat=>source.WaveFormat;public int Read(byte[] bytes,int offset,int count){int n=source.Read(bytes,offset,count);if(n>0)bytes[offset+2]^=1;return n;}}
 sealed class Sine:ISampleProvider {readonly double rate,freq,amp;long frame;public Sine(int rate,double freq,double amp){this.rate=rate;this.freq=freq;this.amp=amp;WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(rate,2);}public WaveFormat WaveFormat{get;}
  public int Read(float[] b,int offset,int count){for(int i=offset;i+1<offset+count;i+=2){float x=(float)(amp*Math.Sin(2*Math.PI*freq*frame++/rate));b[i]=x;b[i+1]=x;}return count;}}
}
