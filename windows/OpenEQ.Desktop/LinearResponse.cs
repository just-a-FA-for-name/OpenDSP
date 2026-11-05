using System.Numerics;
namespace OpenEQ;

/// <summary>Four linear channel paths; no audio generation, recording or devices.</summary>
public static class LinearResponse {
 public sealed record Transfer(double? GainDb,double? PhaseDegrees,double? GroupDelayMs);
 public sealed record Point(double FrequencyHz,Transfer LeftToLeft,Transfer RightToLeft,Transfer LeftToRight,Transfer RightToRight);

 public static Point[] Analyze(Preset preset,int rate){
  preset.Validate();
  if(rate<8000||rate>768000)throw new ArgumentOutOfRangeException(nameof(rate));
  var bands=preset.Enabled?preset.Bands.Where(b=>b.Enabled&&b.Frequency<rate/2d).ToArray():Array.Empty<Band>();
  var left=bands.Where(b=>b.Channel!="right").Select(b=>new Biquad(b,rate)).ToArray();
  var right=bands.Where(b=>b.Channel!="left").Select(b=>new Biquad(b,rate)).ToArray();
  var m=preset.Output.Matrix??new MatrixConfig();
  double a=(1+m.Width)/2,b=(1-m.Width)/2;
  double[] matrix={a*m.Ll+b*m.Rl,a*m.Lr+b*m.Rr,b*m.Ll+a*m.Rl,b*m.Lr+a*m.Rr};
  var spatial=new SpatialTransfer(preset.Output.Spatial??new SpatialConfig(),rate);
  double gain=Math.Pow(10,((preset.Enabled?preset.Preamp:0)+preset.Output.Gain)/20);
  double gl=gain*Math.Min(1,1-preset.Output.Balance)*(preset.Output.InvertLeft?-1:1);
  double gr=gain*Math.Min(1,1+preset.Output.Balance)*(preset.Output.InvertRight?-1:1);
  int dl=(int)Math.Round(preset.Output.LeftDelay*rate/1000),dr=(int)Math.Round(preset.Output.RightDelay*rate/1000);
  Complex Response(double hz,int path){
   Complex z=Complex.FromPolarCoordinates(1,-2*Math.PI*hz/rate),eq=Complex.One;
   foreach(var filter in path%2==0?left:right)
    eq*=(filter.B0+filter.B1*z+filter.B2*z*z)/(1+filter.A1*z+filter.A2*z*z);
   if(preset.Output.DcBlock)eq*=(1-z)/(1-Math.Exp(-2*Math.PI*20/rate)*z);
   bool toLeft=path<2;
   var sp=spatial.At(hz);
   Complex route=path switch{
    0=>matrix[0]*sp.Direct+matrix[1]*sp.Cross,
    1=>matrix[0]*sp.Cross+matrix[1]*sp.Direct,
    2=>matrix[2]*sp.Direct+matrix[3]*sp.Cross,
    _=>matrix[2]*sp.Cross+matrix[3]*sp.Direct
   };
   return eq*route*(toLeft?gl:gr)*Complex.FromPolarCoordinates(1,-2*Math.PI*hz*(toLeft?dl:dr)/rate);
  }
  Transfer Measure(double hz,int path){
   Complex value=Response(hz,path);
   if(value.Magnitude<1e-12)return new(null,null,null);
   const double epsilon=.01;
   double phaseSlope=(Response(hz+epsilon,path)*Complex.Conjugate(Response(hz-epsilon,path))).Phase/(2*epsilon);
   return new(20*Math.Log10(value.Magnitude),value.Phase*180/Math.PI,-1000*phaseSlope/(2*Math.PI));
  }
  double top=Math.Min(20000,rate*.49);
  return Enumerable.Range(0,97).Select(i=>{
   double hz=20*Math.Pow(top/20,i/96d);
   return new Point(hz,Measure(hz,0),Measure(hz,1),Measure(hz,2),Measure(hz,3));
  }).ToArray();
 }
}
