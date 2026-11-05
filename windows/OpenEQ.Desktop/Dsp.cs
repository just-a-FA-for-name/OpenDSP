using NAudio.Wave;

namespace OpenEQ;
public sealed class Biquad {
 public readonly double B0,B1,B2,A1,A2;double z1,z2;
 public Biquad(Band b,double rate){
  if(b.Frequency>=rate/2)throw new ArgumentException("滤波频率超过音频奈奎斯特频率");
  double w=2*Math.PI*b.Frequency/rate,c=Math.Cos(w),s=Math.Sin(w),alpha=s/(2*b.Q),A=Math.Pow(10,b.Gain/40),t=2*Math.Sqrt(A)*alpha;
  double b0,b1,b2,a0,a1,a2;
  switch(b.Type){
   case "peak":b0=1+alpha*A;b1=-2*c;b2=1-alpha*A;a0=1+alpha/A;a1=-2*c;a2=1-alpha/A;break;
   case "low_shelf":b0=A*((A+1)-(A-1)*c+t);b1=2*A*((A-1)-(A+1)*c);b2=A*((A+1)-(A-1)*c-t);a0=(A+1)+(A-1)*c+t;a1=-2*((A-1)+(A+1)*c);a2=(A+1)+(A-1)*c-t;break;
   case "high_shelf":b0=A*((A+1)+(A-1)*c+t);b1=-2*A*((A-1)+(A+1)*c);b2=A*((A+1)+(A-1)*c-t);a0=(A+1)-(A-1)*c+t;a1=2*((A-1)-(A+1)*c);a2=(A+1)-(A-1)*c-t;break;
   case "low_pass":b0=(1-c)/2;b1=1-c;b2=b0;a0=1+alpha;a1=-2*c;a2=1-alpha;break;
   case "high_pass":b0=(1+c)/2;b1=-(1+c);b2=b0;a0=1+alpha;a1=-2*c;a2=1-alpha;break;
   case "notch":b0=1;b1=-2*c;b2=1;a0=1+alpha;a1=-2*c;a2=1-alpha;break;
   case "all_pass":b0=1-alpha;b1=-2*c;b2=1+alpha;a0=1+alpha;a1=-2*c;a2=1-alpha;break;
   default:throw new ArgumentException("Unsupported filter "+b.Type);
  }B0=b0/a0;B1=b1/a0;B2=b2/a0;A1=a1/a0;A2=a2/a0;
 }
 public double Process(double x){double y=B0*x+z1;z1=B1*x-A1*y+z2;z2=B2*x-A2*y;return y;}
 public double Magnitude(double f,double rate){double w=2*Math.PI*f/rate;double nr=B0+B1*Math.Cos(w)+B2*Math.Cos(2*w),ni=-B1*Math.Sin(w)-B2*Math.Sin(2*w),dr=1+A1*Math.Cos(w)+A2*Math.Cos(2*w),di=-A1*Math.Sin(w)-A2*Math.Sin(2*w);return Math.Sqrt((nr*nr+ni*ni)/(dr*dr+di*di));}
}
public sealed class StereoProcessor {
 readonly double[] delayL,delayR;readonly double rate,fade,release;int index,dl,dr;
 double target,gain,lg,rg,ceiling,limiterGain=1;bool protection;
 Biquad[] left=Array.Empty<Biquad>(),right=Array.Empty<Biquad>();
 SpatialProcessor? spatial;
 // DSP rack: DC blocker, channel matrix + stereo width, compressor
 bool dcBlock;double dcA,dcXL,dcYL,dcXR,dcYR;
 double mLL=1,mLR,mRL,mRR=1,width=1;
 bool comp;double compThreshold,compRatio,compKnee,compMakeup,compAttack,compRelease,compEnv=1;
 public double Left,Right;public long Clipped,ProtectedFrames;public double PeakIn,PeakOut;
 public double CompGainDb=>comp?20*Math.Log10(Math.Max(compEnv,1e-9)):0;
 public StereoProcessor(int rate):this(rate,rate/10){}
 internal StereoProcessor(int rate,int delayFrames){this.rate=rate;delayL=new double[checked(delayFrames+1)];delayR=new double[delayL.Length];fade=1-Math.Exp(-1/(rate*.005));release=1-Math.Exp(-1/(rate*.1));}
 public void Configure(Preset p){p.Validate();left=p.Enabled?p.Bands.Where(b=>b.Enabled&&b.Channel!="right"&&b.Frequency<rate/2).Select(b=>new Biquad(b,rate)).ToArray():Array.Empty<Biquad>();right=p.Enabled?p.Bands.Where(b=>b.Enabled&&b.Channel!="left"&&b.Frequency<rate/2).Select(b=>new Biquad(b,rate)).ToArray():Array.Empty<Biquad>();
  target=Math.Pow(10,((p.Enabled?p.Preamp:0)+p.Output.Gain)/20);lg=(p.Output.InvertLeft?-1:1)*Math.Min(1,1-p.Output.Balance);rg=(p.Output.InvertRight?-1:1)*Math.Min(1,1+p.Output.Balance);
  dl=(int)Math.Round(p.Output.LeftDelay*rate/1000);dr=(int)Math.Round(p.Output.RightDelay*rate/1000);protection=p.Output.Protection;ceiling=Math.Pow(10,p.Output.Ceiling/20);
  if(dl>=delayL.Length||dr>=delayR.Length)throw new ArgumentException("Prepared delay capacity is too small");
  dcBlock=p.Output.DcBlock;dcA=Math.Exp(-2*Math.PI*20/rate);
  var m=p.Output.Matrix??new MatrixConfig();mLL=m.Ll;mLR=m.Lr;mRL=m.Rl;mRR=m.Rr;width=m.Width;
  spatial=p.Output.Spatial is {Enabled:true,Mix:>0}?new SpatialProcessor(p.Output.Spatial,(int)rate):null;
  var c=p.Output.Compressor??new CompConfig();comp=c.Enabled;compThreshold=c.ThresholdDb;compRatio=c.Ratio;compKnee=c.KneeDb;compMakeup=c.MakeupDb;
  compAttack=1-Math.Exp(-1/(rate*Math.Max(c.AttackMs,.1)/1000));compRelease=1-Math.Exp(-1/(rate*Math.Max(c.ReleaseMs,5)/1000));
 }
 public void Process(double l,double r){if(!double.IsFinite(l))l=0;if(!double.IsFinite(r))r=0;PeakIn=Math.Max(PeakIn,Math.Max(Math.Abs(l),Math.Abs(r)));
  if(dcBlock){dcYL=l-dcXL+dcA*dcYL;dcXL=l;l=dcYL;dcYR=r-dcXR+dcA*dcYR;dcXR=r;r=dcYR;}
  foreach(var b in left)l=b.Process(l);foreach(var b in right)r=b.Process(r);
  if(spatial!=null){spatial.Process(l,r);l=spatial.Left;r=spatial.Right;}
  if(mLL!=1||mLR!=0||mRL!=0||mRR!=1||width!=1){double nl=l*mLL+r*mLR,nr=l*mRL+r*mRR;if(width!=1){double mid=(nl+nr)*.5,side=(nl-nr)*.5*width;nl=mid+side;nr=mid-side;}l=nl;r=nr;}
  gain+=(target-gain)*fade;delayL[index]=l*gain*lg;delayR[index]=r*gain*rg;Left=delayL[(index-dl+delayL.Length)%delayL.Length];Right=delayR[(index-dr+delayR.Length)%delayR.Length];index=(index+1)%delayL.Length;
  if(comp){double det=Math.Max(Math.Abs(Left),Math.Abs(Right));double over=20*Math.Log10(Math.Max(det,1e-9))-compThreshold;
   double grDb=over>compKnee/2?over*(1/compRatio-1):over>-compKnee/2&&compKnee>0?(1/compRatio-1)*(over+compKnee/2)*(over+compKnee/2)/(2*compKnee):0;
   double t=Math.Pow(10,(grDb+compMakeup)/20);compEnv+=(t-compEnv)*(t<compEnv?compAttack:compRelease);Left*=compEnv;Right*=compEnv;}
  if(protection){double peak=Math.Max(Math.Abs(Left),Math.Abs(Right)),desired=peak>ceiling?ceiling/peak:1;limiterGain=desired<limiterGain?desired:Math.Min(desired,limiterGain+(1-limiterGain)*release);if(limiterGain<.999999)ProtectedFrames++;Left*=limiterGain;Right*=limiterGain;}
  if(Math.Abs(Left)>1||Math.Abs(Right)>1)Clipped++;Left=Math.Clamp(Left,-1,1);Right=Math.Clamp(Right,-1,1);PeakOut=Math.Max(PeakOut,Math.Max(Math.Abs(Left),Math.Abs(Right)));
 }
}
public sealed class DspProvider:ISampleProvider {
 readonly ISampleProvider source;StereoProcessor? pending;StereoProcessor processor;StereoProcessor? old;
 int transition,transitionLength;public WaveFormat WaveFormat=>source.WaveFormat;
 public long Frames;long invalidSamples;public long InvalidSamples=>Interlocked.Read(ref invalidSamples);public double EnergyBefore,EnergyAfter,EnergyInLeft,EnergyInRight,EnergyOutLeft,EnergyOutRight;public long Clipped=>processor.Clipped;public double CompGainDb=>processor.CompGainDb;public double PeakIn=>processor.PeakIn;public double PeakOut=>processor.PeakOut;
 public DspProvider(ISampleProvider source,Preset p){this.source=source;if(source.WaveFormat.Channels!=2)throw new ArgumentException("当前引擎要求立体声");processor=Prepare(p);transitionLength=WaveFormat.SampleRate/50;}
 StereoProcessor Prepare(Preset p){
  var copy=p.Copy(); // validation and allocation stay off the render callback
  int rate=WaveFormat.SampleRate;
  int capacity=Math.Max((int)Math.Round(copy.Output.LeftDelay*rate/1000),(int)Math.Round(copy.Output.RightDelay*rate/1000));
  var next=new StereoProcessor(rate,capacity);next.Configure(copy);return next;
 }
 public void Update(Preset p){var next=Prepare(p);Interlocked.Exchange(ref pending,next);}
 public int Read(float[] buffer,int offset,int count){if(old==null){var next=Interlocked.Exchange(ref pending,null);if(next!=null){old=processor;processor=next;transition=0;}}
  int n=source.Read(buffer,offset,count);for(int i=offset;i+1<offset+n;i+=2){double l=buffer[i],r=buffer[i+1];if(!double.IsFinite(l)){l=0;Interlocked.Increment(ref invalidSamples);}if(!double.IsFinite(r)){r=0;Interlocked.Increment(ref invalidSamples);}processor.Process(l,r);double ol=processor.Left,orr=processor.Right;
   if(old!=null){old.Process(l,r);double mix=(double)transition/transitionLength;ol=old.Left*(1-mix)+ol*mix;orr=old.Right*(1-mix)+orr*mix;if(++transition>=transitionLength)old=null;}
   buffer[i]=(float)ol;buffer[i+1]=(float)orr;Interlocked.Increment(ref Frames);EnergyBefore+=(l*l+r*r)/2;EnergyAfter+=(ol*ol+orr*orr)/2;EnergyInLeft+=l*l;EnergyInRight+=r*r;EnergyOutLeft+=ol*ol;EnergyOutRight+=orr*orr;
  }return n;
 }
}
public sealed class PcmProvider:IWaveProvider {
 readonly ISampleProvider source;readonly int bits;float[] samples=Array.Empty<float>();
 public WaveFormat WaveFormat{get;}
 public PcmProvider(ISampleProvider source,int bits,WaveFormat? format=null){if(!new[]{16,24,32}.Contains(bits))throw new ArgumentException("PCM 位数必须为 16/24/32");this.source=source;this.bits=bits;WaveFormat=format??new WaveFormat(source.WaveFormat.SampleRate,bits,source.WaveFormat.Channels);}
 public int Read(byte[] b,int offset,int count){int width=bits/8;int wanted=count/width;if(samples.Length<wanted)samples=new float[wanted];int n=source.Read(samples,0,wanted);double scale=Math.Pow(2,bits-1);for(int i=0;i<n;i++){double sample=float.IsFinite(samples[i])?samples[i]:0;long v=(long)Math.Round(Math.Clamp(sample,-1,1)*scale);v=Math.Clamp(v,-(long)scale,(long)scale-1);for(int j=0;j<width;j++)b[offset+i*width+j]=(byte)(v>>(8*j));}return n*width;}
}

public sealed class FloatProvider:IWaveProvider {
 readonly ISampleProvider source;float[] samples=Array.Empty<float>();public WaveFormat WaveFormat{get;}
 public static bool Supports(WaveFormat f)=>f.BitsPerSample==32&&(f.Encoding==WaveFormatEncoding.IeeeFloat||f is WaveFormatExtensible e&&e.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71"));
 public FloatProvider(ISampleProvider source,WaveFormat format){if(!Supports(format)||source.WaveFormat.SampleRate!=format.SampleRate||source.WaveFormat.Channels!=format.Channels)throw new ArgumentException("浮点输出格式不匹配");this.source=source;WaveFormat=format;}
 public int Read(byte[] buffer,int offset,int count){int wanted=count/4;if(samples.Length<wanted)samples=new float[wanted];int n=source.Read(samples,0,wanted);for(int i=0;i<n;i++)if(!float.IsFinite(samples[i]))samples[i]=0;Buffer.BlockCopy(samples,0,buffer,offset,n*4);return n*4;}
}
