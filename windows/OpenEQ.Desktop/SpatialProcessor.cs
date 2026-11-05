using System.Numerics;
namespace OpenEQ;

/// <summary>Managed-path M/S EQ, complementary side-band widths and mono-preserving crossfeed.</summary>
public sealed class SpatialProcessor {
 readonly Biquad[] mid,side;
 readonly bool active,widthChanged,mono;
 readonly double midGain,sideGain,mix,lowWidth,midWidth,highWidth,lowPole,highPole,crossPole,crossAmount;
 readonly double[] crossDelay;readonly int delayFrames;int index;
 double lowState,highState,crossState;
 public double Left{get;private set;}public double Right{get;private set;}
 internal static double Pole(double hz,double rate)=>Math.Exp(-2*Math.PI*hz/rate);
 public SpatialProcessor(SpatialConfig config,int rate){
  config.Validate();active=config.Enabled&&config.Mix>0;mono=config.Mono;mix=config.Mix;
  mid=active?config.MidBands.Where(b=>b.Enabled&&b.Frequency<rate/2d).Select(b=>new Biquad(b,rate)).ToArray():Array.Empty<Biquad>();
  side=active?config.SideBands.Where(b=>b.Enabled&&b.Frequency<rate/2d).Select(b=>new Biquad(b,rate)).ToArray():Array.Empty<Biquad>();
  midGain=Math.Pow(10,config.MidGain/20);sideGain=Math.Pow(10,config.SideGain/20);
  lowWidth=config.LowWidth;midWidth=config.MidWidth;highWidth=config.HighWidth;
  widthChanged=lowWidth!=1||midWidth!=1||highWidth!=1;
  lowPole=Pole(config.LowCrossover,rate);highPole=Pole(config.HighCrossover,rate);crossPole=Pole(config.CrossfeedHz,rate);crossAmount=config.Crossfeed;
  delayFrames=active&&crossAmount>0?(int)Math.Round(config.CrossfeedDelay*rate/1000d):0;
  crossDelay=new double[delayFrames+1];
 }
 public void Process(double left,double right){
  if(!active){Left=left;Right=right;return;}
  double m=(left+right)*.5,s=(left-right)*.5;
  foreach(var b in mid)m=b.Process(m);foreach(var b in side)s=b.Process(s);
  m*=midGain;s*=sideGain;
  if(widthChanged){lowState=(1-lowPole)*s+lowPole*lowState;highState=(1-highPole)*s+highPole*highState;s=lowWidth*lowState+midWidth*(highState-lowState)+highWidth*(s-highState);}
  if(mono)s=0;
  if(crossAmount>0){
   crossState=(1-crossPole)*s+crossPole*crossState;crossDelay[index]=crossState;
   s-=2*crossAmount*crossDelay[(index-delayFrames+crossDelay.Length)%crossDelay.Length];
   index=(index+1)%crossDelay.Length;
  }
  Left=left*(1-mix)+(m+s)*mix;Right=right*(1-mix)+(m-s)*mix;
 }
}

/// <summary>The same discrete transfer as SpatialProcessor, for analysis only.</summary>
public sealed class SpatialTransfer {
 readonly SpatialConfig config;readonly int rate,delay;
 readonly Biquad[] mid,side;
 public SpatialTransfer(SpatialConfig config,int rate){
  config.Validate();this.config=config;this.rate=rate;delay=(int)Math.Round(config.CrossfeedDelay*rate/1000d);
  mid=config.MidBands.Where(b=>b.Enabled&&b.Frequency<rate/2d).Select(b=>new Biquad(b,rate)).ToArray();
  side=config.SideBands.Where(b=>b.Enabled&&b.Frequency<rate/2d).Select(b=>new Biquad(b,rate)).ToArray();
 }
 internal static Complex Filter(Biquad filter,Complex z)=>(filter.B0+filter.B1*z+filter.B2*z*z)/(1+filter.A1*z+filter.A2*z*z);
 public (Complex Direct,Complex Cross) At(double hz){
  if(!config.Enabled||config.Mix==0)return(Complex.One,Complex.Zero);
  Complex z=Complex.FromPolarCoordinates(1,-2*Math.PI*hz/rate);
  Complex Low(double cutoff){double pole=SpatialProcessor.Pole(cutoff,rate);return (1-pole)/(1-pole*z);}
  Complex m=Math.Pow(10,config.MidGain/20),s=Math.Pow(10,config.SideGain/20);
  foreach(var b in mid)m*=Filter(b,z);foreach(var b in side)s*=Filter(b,z);
  s*=config.HighWidth+(config.MidWidth-config.HighWidth)*Low(config.HighCrossover)+(config.LowWidth-config.MidWidth)*Low(config.LowCrossover);
  if(config.Mono)s=0;
  s*=1-2*config.Crossfeed*Low(config.CrossfeedHz)*Complex.FromPolarCoordinates(1,-2*Math.PI*hz*delay/rate);
  m=1-config.Mix+config.Mix*m;s=1-config.Mix+config.Mix*s;
  return((m+s)*.5,(m-s)*.5);
 }
 public double MaximumRowGain(){
  if(!config.Enabled)return 1;
  var frequencies=Enumerable.Range(0,1537).Select(i=>20*Math.Pow(Math.Min(20000,rate*.49)/20,i/1536d))
   .Concat(config.MidBands.Concat(config.SideBands).Where(b=>b.Enabled&&b.Frequency<rate/2d).Select(b=>b.Frequency));
  double gain=1;foreach(double hz in frequencies){var response=At(hz);gain=Math.Max(gain,response.Direct.Magnitude+response.Cross.Magnitude);}return gain;
 }
}
