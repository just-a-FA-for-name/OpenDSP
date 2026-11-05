using NAudio.Wave;
using System.Text.Json;
namespace OpenEQ;
internal static class DspRackChecks {
 public static int Run(string path){var report=new Dictionary<string,object>();var cases=new List<string>();void Check(bool yes,string name){if(!yes)throw new Exception(name);cases.Add(name);}
  try{
   // 1) compressor gain reduction: 0.5 sine, threshold -20, ratio 4, knee 0
   var p=Preset.Flat();p.Bands.Clear();p.Output.Protection=false;var c=p.Output.Compressor??new CompConfig();c.Enabled=true;c.ThresholdDb=-20;c.Ratio=4;c.KneeDb=0;c.AttackMs=1;c.ReleaseMs=200;c.MakeupDb=0;p.Output.Compressor=c;
   double amp1=Measure(p,.5,48000);double expected=.5*Math.Pow(10,((-6.0206+20)*(1-1.0/4))*-1/20); // GR=-10.48dB
   report["compressor"]=new{amp_db=20*Math.Log10(amp1),expected_db=20*Math.Log10(expected)};
   Check(Math.Abs(20*Math.Log10(amp1/expected))<1.2,"compressor_gain_reduction");
   // 2) makeup restores level
   c.MakeupDb=10.48;double amp2=Measure(p,.5,48000);report["compressor_makeup_db"]=20*Math.Log10(amp2/.5);
   Check(Math.Abs(20*Math.Log10(amp2/.5))<1.2,"compressor_makeup");
   // 3) width 0 collapses to mono
   c.Enabled=false;var m=p.Output.Matrix??new MatrixConfig();m.Width=0;p.Output.Matrix=m;
   double diff=MonoDiff(p);report["width0_channel_diff"]=diff;Check(diff<1e-6,"width_zero_is_mono");
   // 4) DC blocker removes offset
   m.Width=1;p.Output.DcBlock=true;double dc=MeasureDc(p,.2,48000);report["dc_residual"]=dc;Check(dc<1e-3,"dc_block_removes_offset");
   // 5) every built-in target curve fits with ~12 bands
   foreach(var curve in TargetCurves.BuiltIn.Where(c=>!c.RequiresMeasurement)){var bands=TargetCurves.Generate(curve,null,12);var fit=Preset.Flat();fit.Bands.Clear();foreach(var b in bands)fit.Bands.Add(b);
    double rms=FitRms(fit,curve,null);report["fit_"+curve.Name]=new{bands=bands.Count,rms_db=rms};Check(rms<1.2,$"curve_fit_{curve.Name}");}
   // 6) measured → target cancels a known coloration
   var colored=Preset.Flat();colored.Bands.Clear();colored.Bands.Add(new Band{Type="low_shelf",Frequency=120,Gain=6,Q=.707});colored.Bands.Add(new Band{Type="peak",Frequency=3000,Gain=3,Q=2});
   var measured=new List<CurvePoint>();for(int i=0;i<64;i++){double hz=20*Math.Pow(1000,i/63.0);measured.Add(new CurvePoint(hz,colored.Response(hz,true)));}
   var flatTarget=new TargetCurves.Curve("Synthetic flat","Test fixture",new double[,]{{20,0},{1000,0},{20000,0}},"fixture","acoustic","fixture");
   var cancel=TargetCurves.Generate(flatTarget,measured,16,48000,"fixture");var fix=Preset.Flat();fix.Bands.Clear();foreach(var b in cancel)fix.Bands.Add(b);
   double cancelRms=FitRms(fix,flatTarget,measured);report["measured_fit_rms_db"]=cancelRms;Check(cancelRms<1.5,"measured_to_target_fit");
   report["result"]="PASS";return 0;
  }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}
  finally{report["cases"]=cases;report["scope"]="Offline DSP rack + tuning-curve fitting; no devices";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals}));}}
 static double Measure(Preset p,double amp,int rate){var dsp=new DspProvider(new Tone(rate,amp),p);var buf=new float[rate*2];double lastRms=0;for(int sec=0;sec<2;sec++){dsp.Read(buf,0,buf.Length);if(sec==1){double s=0;for(int i=0;i<buf.Length;i+=2)s+=buf[i]*buf[i];lastRms=Math.Sqrt(s/(buf.Length/2));}}return lastRms*Math.Sqrt(2);}
 static double MeasureDc(Preset p,double dc,int rate){var dsp=new DspProvider(new Const(rate,dc),p);var buf=new float[rate*2];for(int sec=0;sec<2;sec++)dsp.Read(buf,0,buf.Length);return Math.Abs(buf[^2]);}
 static double MonoDiff(Preset p){var dsp=new DspProvider(new Split(new Tone(48000,.5),new Tone(48000,.3)),p);var buf=new float[48000*2];for(int s=0;s<2;s++)dsp.Read(buf,0,buf.Length);double max=0;for(int i=0;i<buf.Length;i+=2)max=Math.Max(max,Math.Abs(buf[i]-buf[i+1]));return max;}
 static double FitRms(Preset fit,TargetCurves.Curve target,List<CurvePoint>? measured){double sum=0;int n=0;for(int i=0;i<64;i++){double hz=20*Math.Pow(1000,i/63.0);double m=0;if(measured!=null){foreach(var pt in measured)if(Math.Abs(pt.Hz-hz)<1){m=pt.Db;break;}}
  double want=target.Response(hz);double got=fit.Response(hz,true)+m;double e=got-want;sum+=e*e;n++;}return Math.Sqrt(sum/n);}
 sealed class Tone:ISampleProvider {readonly double rate,amp;long f;public Tone(double rate,double amp){this.rate=rate;this.amp=amp;WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat((int)rate,2);}public WaveFormat WaveFormat{get;}public int Read(float[] b,int o,int c){for(int i=o;i+1<o+c;i+=2){float x=(float)(amp*Math.Sin(2*Math.PI*1000*f++/rate));b[i]=x;b[i+1]=x;}return c;}}
 sealed class Const:ISampleProvider {readonly double rate,dc;public Const(double rate,double dc){this.rate=rate;this.dc=dc;WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat((int)rate,2);}public WaveFormat WaveFormat{get;}public int Read(float[] b,int o,int c){Array.Fill(b,(float)dc,o,c);return c;}}
 sealed class Split:ISampleProvider {readonly ISampleProvider l,r;public Split(ISampleProvider l,ISampleProvider r){this.l=l;this.r=r;WaveFormat=l.WaveFormat;}public WaveFormat WaveFormat{get;}public int Read(float[] b,int o,int c){var a=new float[c/2];l.Read(a,0,a.Length);var d=new float[c/2];r.Read(d,0,d.Length);for(int i=0;i<a.Length;i++){b[o+i*2]=a[i];b[o+i*2+1]=d[i];}return c;}}
}
