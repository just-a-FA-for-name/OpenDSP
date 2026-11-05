using NAudio.Wave;
namespace OpenEQ;
/// <summary>
/// PCM→DSD output stage: 16× polyphase interpolation + fifth-order sigma-delta
/// modulation + DoP (DSD-over-PCM) framing. Produces 24-bit DoP frames at
/// 176.4 kHz (DSD64) or 352.8 kHz (DSD128) for WASAPI exclusive output.
/// DoP layout per channel: each frame pair carries 32 DSD bits (16 per frame,
/// MSB first) with marker bytes 0x05 (first frame) / 0xFA (second frame).
/// </summary>
public static class Dsd {
 public const string Off="off",Dop64="dop64",Dop128="dop128";
 public static int FrameRate(string mode)=>mode==Dop64?176400:352800;
}
/// <summary>
/// 5th-order error-feedback sigma-delta modulator, one per channel, 1-bit output
/// (1→+1, 0→−1). NTF zeros spread across the audio band (one at DC, two conjugate
/// pairs) with poles at z=r; r is found at runtime by scan so ‖NTF‖∞ ≤ 1.45
/// (Lee stability criterion for 1-bit quantizers). TPDF comparator dither breaks
/// limit cycles. Design technique: classic Schreier/Lee ΔΣ synthesis (textbook).
/// </summary>
public sealed class DsdModulator {
 const int Order=5;
 public const double MaximumInput=.5;
 long limitedSamples;
 public long LimitedSamples=>Interlocked.Read(ref limitedSamples);
 public double NoiseTransferPeak{get;}
 readonly double[] eh=new double[Order];readonly double[] fh=new double[Order];
 readonly double[] b=new double[Order+1];readonly double[] a=new double[Order+1];
 readonly Random rng=new(0x5f3759df);readonly double dither;
 public DsdModulator(double dsdRate,double bandwidthHz=20000,double ntfPeak=1.45,double dither=.02){
  if(!double.IsFinite(dsdRate)||!double.IsFinite(bandwidthHz)||bandwidthHz<=0||dsdRate<=bandwidthHz*2)throw new ArgumentOutOfRangeException(nameof(dsdRate));
  if(!double.IsFinite(ntfPeak)||ntfPeak<=1||ntfPeak>1.5)throw new ArgumentOutOfRangeException(nameof(ntfPeak));
  if(!double.IsFinite(dither)||dither<0||dither>.1)throw new ArgumentOutOfRangeException(nameof(dither));
  double wbw=2*Math.PI*bandwidthHz/dsdRate;
  double t1=.35*wbw,t2=.75*wbw;
  double[] n=PolyMul(PolyMul(new[]{1d,-1},new[]{1,-2*Math.Cos(t1),1}),new[]{1,-2*Math.Cos(t2),1});
  bool designed=false;
  for(int iter=0;iter<=49;iter++){ // Bound the actual NTF, not the feedback transfer NTF - 1.
   double r=.5+iter*.01;
   a[0]=1;for(int k=1;k<=Order;k++)a[k]=Binom(Order,k)*Math.Pow(-r,k);
   for(int k=0;k<=Order;k++)b[k]=n[k]-a[k];
   double peak=PeakNtf(n);
   if(double.IsFinite(peak)&&peak<=ntfPeak){NoiseTransferPeak=peak;designed=true;break;}
  }
  if(!designed)throw new ArgumentException("DSD noise-transfer target could not be satisfied",nameof(ntfPeak));
  this.dither=dither;
 }
 double PeakNtf(double[] numerator){double max=0;for(int i=0;i<=4096;i++){double w=Math.PI*i/4096;Complex num=Eval(numerator,w),den=Eval(a,w);double m=Complex.Abs(num/den);if(m>max)max=m;}return max;}
 static Complex Eval(double[] c,double w){var z=new Complex(Math.Cos(w),-Math.Sin(w));var acc=Complex.Zero;var p=Complex.One;for(int k=0;k<c.Length;k++){acc+=c[k]*p;p*=z;}return acc;}
 static double[] PolyMul(double[] x,double[] y){var r=new double[x.Length+y.Length-1];for(int i=0;i<x.Length;i++)for(int j=0;j<y.Length;j++)r[i+j]+=x[i]*y[j];return r;}
 static double Binom(int n,int k){double v=1;for(int i=0;i<k;i++)v=v*(n-i)/(i+1);return v;}
 public int Process(double x){
  // Reject invalid input before touching the feedback history. Otherwise one
  // NaN permanently turns the bitstream into zeros while DoP markers stay valid.
  if(!double.IsFinite(x))throw new ArgumentOutOfRangeException(nameof(x),"DSD input must be finite");
  if(x>MaximumInput||x< -MaximumInput){x=Math.Clamp(x,-MaximumInput,MaximumInput);Interlocked.Increment(ref limitedSamples);}
  double f=0;for(int k=0;k<Order;k++)f+=b[k+1]*eh[k];for(int k=0;k<Order;k++)f-=a[k+1]*fh[k];
  double u=x+f;
  if(!double.IsFinite(u)||Math.Abs(u)>16)throw new ArithmeticException("DSD modulator became unstable; refusing invalid output");
  double tpdf=(rng.NextDouble()-rng.NextDouble())*dither;
  int bit=u+tpdf>=0?1:0;double q=bit==0?-1:1;
  double e=q-u; // dither only steers the decision; the error excludes it so NTF shapes it
  for(int k=Order-1;k>0;k--){eh[k]=eh[k-1];fh[k]=fh[k-1];}eh[0]=e;fh[0]=f;
  return bit;
 }
 readonly struct Complex{public readonly double Re,Im;public Complex(double re,double im){Re=re;Im=im;}public static readonly Complex Zero=new(0,0),One=new(1,0);public static Complex operator +(Complex x,Complex y)=>new(x.Re+y.Re,x.Im+y.Im);public static Complex operator *(Complex x,Complex y)=>new(x.Re*y.Re-x.Im*y.Im,x.Re*y.Im+x.Im*y.Re);public static Complex operator *(double s,Complex x)=>new(s*x.Re,s*x.Im);public static Complex operator /(Complex x,Complex y){double d=y.Re*y.Re+y.Im*y.Im;return new((x.Re*y.Re+x.Im*y.Im)/d,(x.Im*y.Re-x.Re*y.Im)/d);}public static double Abs(Complex x)=>Math.Sqrt(x.Re*x.Re+x.Im*x.Im);}
}
/// <summary>16× polyphase Blackman-windowed-sinc interpolator, 8 taps/phase, per-phase DC-normalized.</summary>
public sealed class Upsampler16 {
 const int Phases=16,TapsPerPhase=8;
 readonly double[] h=new double[Phases*TapsPerPhase];readonly double[] hist=new double[TapsPerPhase];
 public Upsampler16(){
  double fc=1.0/(2*Phases);int total=Phases*TapsPerPhase;
  for(int n=0;n<total;n++){int k=n-(total-1)/2;double sinc=k==0?2*fc:Math.Sin(2*Math.PI*fc*k)/(Math.PI*k);double w=.42-.5*Math.Cos(2*Math.PI*n/(total-1))+.08*Math.Cos(4*Math.PI*n/(total-1));h[n]=sinc*w;}
  for(int p=0;p<Phases;p++){double sum=0;for(int t=0;t<TapsPerPhase;t++)sum+=h[t*Phases+p];if(sum!=0)for(int t=0;t<TapsPerPhase;t++)h[t*Phases+p]/=sum;}
 }
 public void Process(double x,double[] output,int offset){
  for(int t=TapsPerPhase-1;t>0;t--)hist[t]=hist[t-1];hist[0]=x;
  for(int p=0;p<Phases;p++){double acc=0;for(int t=0;t<TapsPerPhase;t++)acc+=h[t*Phases+p]*hist[t];output[offset+p]=acc;}
 }
}
/// <summary>ISampleProvider(float PCM @ frame rate) → IWaveProvider(24-bit DoP stereo @ frame rate).</summary>
public static class DopFrame {
 // DoP 1.1 线格式（纯函数，供黄金样本逐字节锁死）：
 // 24-bit 载体：字节0=DSD位0-7，字节1=DSD位8-15，字节2=标记(0x05/0xFA)。
 // 32-bit 载体：低 8 位 padding=0，高 24 位同上（左对齐）。
 public static void Pack(int wordL,int wordR,byte marker,byte[] buffer,int o,int width){
  if(width==4){buffer[o]=0;buffer[o+4]=0;o++;}
  buffer[o]=(byte)wordL;buffer[o+1]=(byte)(wordL>>8);buffer[o+2]=marker;
  buffer[o+width]=(byte)wordR;buffer[o+width+1]=(byte)(wordR>>8);buffer[o+width+2]=marker;}
}
public sealed class DsdProvider:IWaveProvider {
 // 0 dB SACD is a 50% modulation index. Keep 6.02 dB of modulator headroom;
 // do not compensate by changing device volume or processing the DoP carrier.
 public const double InputGain=.5;
 public static double HeadroomDb=>20*Math.Log10(InputGain);
 readonly ISampleProvider source;readonly int frameRate;
 readonly Upsampler16 upL=new(),upR=new();readonly DsdModulator modL,modR;
 readonly double[] tempL=new double[16],tempR=new double[16];readonly float[] pcm;
 public WaveFormat WaveFormat{get;}
 public long LimitedSamples=>modL.LimitedSamples+modR.LimitedSamples;
 public DsdProvider(ISampleProvider source,string mode,WaveFormat? carrierFormat=null){if(mode!=Dsd.Dop64&&mode!=Dsd.Dop128)throw new ArgumentException("未知 DSD 模式");this.source=source;frameRate=Dsd.FrameRate(mode);if(source.WaveFormat.Channels!=2||source.WaveFormat.SampleRate!=frameRate)throw new ArgumentException("DoP source must be stereo PCM at the DoP frame rate");modL=new DsdModulator(frameRate*16.0);modR=new DsdModulator(frameRate*16.0);WaveFormat=carrierFormat??new WaveFormat(frameRate,24,2);if(WaveFormat.SampleRate!=frameRate||WaveFormat.Channels!=2||WaveFormat.BitsPerSample is not (24 or 32)||!(WaveFormat.Encoding==WaveFormatEncoding.Pcm||WaveFormat is WaveFormatExtensible ext&&ext.SubFormat==new Guid("00000001-0000-0010-8000-00aa00389b71")))throw new ArgumentException("DoP requires an integer 24/32-bit PCM carrier at its frame rate");pcm=new float[8192];}
 long framePosition;
 public int Read(byte[] buffer,int offset,int count){
  ArgumentNullException.ThrowIfNull(buffer);
  if(offset<0||count<0||offset>buffer.Length-count)throw new ArgumentOutOfRangeException();
  int stride=WaveFormat.BlockAlign,width=WaveFormat.BitsPerSample/8;int requested=count/stride,written=0;
  while(written<requested){
   int chunk=Math.Min(requested-written,pcm.Length/2);
   int got=source.Read(pcm,0,chunk*2);
   if(got<0||got>chunk*2||(got&1)!=0)throw new InvalidDataException("Invalid stereo PCM read count");
   int frames=got/2;if(frames==0)break;
   for(int f=0;f<frames;f++){
    float left=pcm[f*2],right=pcm[f*2+1];
    if(!float.IsFinite(left)||!float.IsFinite(right))throw new InvalidDataException("Non-finite PCM at DSD input");
    upL.Process(left*InputGain,tempL,0);upR.Process(right*InputGain,tempR,0);
    int wordL=0,wordR=0;
    // DoP 1.1: oldest DSD sample occupies bit 15 (t0), newest bit 0 (t15).
    for(int t=0;t<16;t++){wordL=(wordL<<1)|modL.Process(tempL[t]);wordR=(wordR<<1)|modR.Process(tempR[t]);}
    byte marker=(byte)((framePosition++&1)==0?0x05:0xFA);
    DopFrame.Pack(wordL,wordR,marker,buffer,offset+(written+f)*stride,width);
   }
   written+=frames;
  }
  return written*stride;
 }
}