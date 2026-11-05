namespace OpenEQ;

/// <summary>UI-only response cache. No playback state or coefficients are changed.</summary>
internal sealed class EqPlotCache {
 const int Count=361;
 internal static readonly double[] Frequencies=Enumerable.Range(0,Count).Select(i=>20*Math.Pow(1000,i/360d)).ToArray();
 readonly double[] cos1=new double[Count],sin1=new double[Count],cos2=new double[Count],sin2=new double[Count];
 public double[] LeftDb{get;}=new double[Count];
 public double[] RightDb{get;}=new double[Count];
 public double[] NodeDb{get;private set;}=Array.Empty<double>();
 BandState[] bands=Array.Empty<BandState>();
 int sampleRate=-1;double preamp;bool enabled,initialized;
 internal int Rebuilds{get;private set;}
 readonly record struct BandState(string Type,double Frequency,double Gain,double Q,string Channel,bool Enabled){
  public static BandState Of(Band band)=>new(band.Type,band.Frequency,band.Gain,band.Q,band.Channel,band.Enabled);
 }
 public bool Update(Preset preset,int rate){
  bool changed=!initialized||sampleRate!=rate||preamp!=preset.Preamp||enabled!=preset.Enabled||bands.Length!=preset.Bands.Count;
  if(!changed)for(int i=0;i<bands.Length;i++)if(bands[i]!=BandState.Of(preset.Bands[i])){changed=true;break;}
  if(!changed)return false;
  if(rate<=0)throw new ArgumentOutOfRangeException(nameof(rate));
  if(sampleRate!=rate){
   for(int i=0;i<Count;i++){double w=2*Math.PI*Frequencies[i]/rate;cos1[i]=Math.Cos(w);sin1[i]=Math.Sin(w);cos2[i]=Math.Cos(2*w);sin2[i]=Math.Sin(2*w);}
  }
  sampleRate=rate;preamp=preset.Preamp;enabled=preset.Enabled;initialized=true;
  if(bands.Length!=preset.Bands.Count){bands=new BandState[preset.Bands.Count];NodeDb=new double[bands.Length];}
  for(int i=0;i<bands.Length;i++)bands[i]=BandState.Of(preset.Bands[i]);
  Array.Fill(LeftDb,enabled?preamp:0);Array.Fill(RightDb,enabled?preamp:0);
  Array.Fill(NodeDb,enabled?preamp:0);
  if(enabled){
   for(int i=0;i<bands.Length;i++){
    var band=preset.Bands[i];
    if(!band.Enabled||band.Frequency>=rate/2d)continue;
    var filter=new Biquad(band,rate);
    for(int n=0;n<Count;n++){
     double db=Gain(filter,cos1[n],sin1[n],cos2[n],sin2[n]);
     if(band.Channel!="right")LeftDb[n]+=db;
     if(band.Channel!="left")RightDb[n]+=db;
    }
    for(int n=0;n<bands.Length;n++){
     bool left=bands[n].Channel!="right";
     if(band.Channel!="both"&&band.Channel!=(left?"left":"right"))continue;
     double w=2*Math.PI*bands[n].Frequency/rate;
     NodeDb[n]+=Gain(filter,Math.Cos(w),Math.Sin(w),Math.Cos(2*w),Math.Sin(2*w));
    }
   }
  }
  Rebuilds++;return true;
 }
 static double Gain(Biquad f,double c1,double s1,double c2,double s2){
  double nr=f.B0+f.B1*c1+f.B2*c2,ni=-f.B1*s1-f.B2*s2;
  double dr=1+f.A1*c1+f.A2*c2,di=-f.A1*s1-f.A2*s2;
  return 10*Math.Log10(Math.Max(1e-24,(nr*nr+ni*ni)/(dr*dr+di*di)));
 }
}
