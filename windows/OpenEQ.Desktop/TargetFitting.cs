namespace OpenEQ;

public sealed record TargetFitOptions(int Bands=20,double MinHz=20,double MaxHz=8000,double MaxBoostDb=6,double MaxCutDb=12,double MaxQ=3,bool Smooth=true);
public sealed record TargetFitResult(List<Band> Bands,double BeforeRmsDb,double AfterRmsDb,double MaximumErrorDb,
 double[][] Response,int ClippedPoints,string Scope,TargetFitOptions Options);

/// <summary>Shape fitting at the actual DSP rate. Never measures or plays audio.</summary>
public static class TargetFitting {
 public static TargetFitResult Fit(TargetCurves.Curve target,List<CurvePoint>? measured,int rate,TargetFitOptions options,string? rig=null){
  Preset.Range(rate,8000,768000,"采样率");Preset.Range(options.Bands,1,32,"拟合段数");
  Preset.Range(options.MinHz,20,19999,"拟合下限");Preset.Range(options.MaxHz,options.MinHz+1,Math.Min(20000,rate*.49),"拟合上限");
  Preset.Range(options.MaxBoostDb,0,12,"最大提升");Preset.Range(options.MaxCutDb,.1,24,"最大衰减");Preset.Range(options.MaxQ,.3,8,"最大Q");
  if(target.RequiresMeasurement){
   if(measured==null)throw new ArgumentException("声学目标需要真实耳机频响；没有测量时请选择听感偏好曲线。");
   if(!string.Equals(rig,target.Rig,StringComparison.OrdinalIgnoreCase))throw new ArgumentException($"测量体系不匹配：此目标需要 {target.Rig} 数据。");
  }else if(measured!=null)throw new ArgumentException("听感偏好曲线是相对EQ形状，不可减去原始声学测量；请改选声学目标。");
  if(measured!=null){
   TargetCurves.ValidatePoints(measured);
   if(measured[0].Hz>Math.Min(options.MinHz,1000)||measured[^1].Hz<Math.Max(options.MaxHz,1000))
    throw new ArgumentException("测量未覆盖拟合范围和1kHz对齐点；不会外推缺失的频响。");
  }
  if(target.RequiresMeasurement&&(target.Points[0,0]>Math.Min(options.MinHz,1000)||target.Points[target.Points.GetLength(0)-1,0]<Math.Max(options.MaxHz,1000)))
   throw new ArgumentException("目标数据未覆盖拟合范围和1kHz对齐点。");
  const int count=160;
  var frequencies=new double[count];var desired=new double[count];var raw=new double[count];var measuredShape=new double[count];var targetShape=new double[count];
  double ta=target.RequiresMeasurement?target.Response(1000):0,ma=measured!=null?TargetCurves.Interpolate(measured,1000):0;
  double At(Func<double,double> response,double hz){
   if(!options.Smooth||!target.RequiresMeasurement)return response(hz);
   double sum=0;for(int j=-4;j<=4;j++)sum+=response(Math.Clamp(hz*Math.Pow(2,j/48d),options.MinHz,options.MaxHz));return sum/9;
  }
  int clipped=0;
  for(int i=0;i<count;i++){
   frequencies[i]=options.MinHz*Math.Pow(options.MaxHz/options.MinHz,i/(count-1d));
   targetShape[i]=At(target.Response,frequencies[i])-ta;
   measuredShape[i]=measured!=null?At(hz=>TargetCurves.Interpolate(measured,hz),frequencies[i])-ma:0;
   raw[i]=targetShape[i]-measuredShape[i];desired[i]=Math.Clamp(raw[i],-options.MaxCutDb,options.MaxBoostDb);
   if(Math.Abs(raw[i]-desired[i])>1e-9)clipped++;
  }
  var residual=(double[])desired.Clone();var bands=new List<Band>();var contributions=new List<double[]>();
  double[] Response(Band band){var filter=new Biquad(band,rate);return frequencies.Select(hz=>20*Math.Log10(Math.Max(1e-12,filter.Magnitude(hz,rate)))).ToArray();}
  bool WithinBounds(double[] remaining){
   for(int i=0;i<count;i++){double eq=desired[i]-remaining[i];if(eq>options.MaxBoostDb+.25||eq< -options.MaxCutDb-.25)return false;}return true;
  }
  double Score(double[] remaining)=>remaining.Sum(v=>v*v);
  for(int iteration=0;iteration<options.Bands;iteration++){
   if(Math.Sqrt(Score(residual)/count)<.08)break;
   var centers=new List<int>();
   foreach(int index in Enumerable.Range(0,count).OrderByDescending(i=>Math.Abs(residual[i]))){
    if(centers.All(other=>Math.Abs(Math.Log2(frequencies[index]/frequencies[other]))>.2))centers.Add(index);
    if(centers.Count==5)break;
   }
   Band? best=null;double[]? bestResponse=null,bestResidual=null;double bestScore=Score(residual);
   foreach(int center in centers){
    var types=new List<string>{"peak"};
    if(center<12&&options.MinHz<=25)types.Add("low_shelf");
    if(center>count-13&&options.MaxHz>=16000)types.Add("high_shelf");
    foreach(string type in types)foreach(double q in new[]{.3,.5,.707,1,1.5,2,3}.Where(q=>q<=options.MaxQ&&(frequencies[center]<=6000||q<=1.5)))
     foreach(double fraction in new[]{.65,1d}){
      var band=new Band{Type=type,Frequency=Math.Round(frequencies[center],1),Gain=Math.Round(Math.Clamp(residual[center]*fraction,-options.MaxCutDb,options.MaxBoostDb),2),Q=type=="peak"?q:.707};
      if(Math.Abs(band.Gain)<.05)continue;
      var response=Response(band);var next=residual.Select((value,i)=>value-response[i]).ToArray();if(!WithinBounds(next))continue;
      double score=Score(next);if(score<bestScore-1e-6){bestScore=score;best=band;bestResponse=response;bestResidual=next;}
     }
   }
   if(best==null)break;
   bands.Add(best);contributions.Add(bestResponse!);residual=bestResidual!;
  }
  // Refine existing sections rather than merely adding more overlapping filters.
  for(int pass=0;pass<2;pass++)for(int n=0;n<bands.Count;n++){
   var current=bands[n];double bestScore=Score(residual);Band best=current;var bestResponse=contributions[n];var bestResidual=residual;
   var variations=new[]{
    (current.Frequency,current.Gain+.2,current.Q),(current.Frequency,current.Gain-.2,current.Q),
    (current.Frequency*.94,current.Gain,current.Q),(current.Frequency*1.06,current.Gain,current.Q),
    (current.Frequency,current.Gain,current.Q*.85),(current.Frequency,current.Gain,current.Q*1.15)};
   foreach(var (hz,gain,q) in variations){
    if(hz<options.MinHz||hz>options.MaxHz||gain>options.MaxBoostDb||gain< -options.MaxCutDb||q<.3||q>options.MaxQ||(hz>6000&&q>1.5))continue;
    var trial=new Band{Type=current.Type,Frequency=Math.Round(hz,1),Gain=Math.Round(gain,2),Q=current.Type=="peak"?Math.Round(q,3):.707};
    var response=Response(trial);var next=residual.Select((value,i)=>value+contributions[n][i]-response[i]).ToArray();
    if(!WithinBounds(next))continue;double score=Score(next);
    if(score<bestScore-1e-6){bestScore=score;best=trial;bestResponse=response;bestResidual=next;}
   }
   bands[n]=best;contributions[n]=bestResponse;residual=bestResidual;
  }
  var eq=desired.Select((value,i)=>value-residual[i]).ToArray();
  var errors=raw.Select((value,i)=>value-eq[i]).ToArray();
  return new(bands,Math.Sqrt(raw.Sum(v=>v*v)/count),Math.Sqrt(errors.Sum(v=>v*v)/count),errors.Max(Math.Abs),
   frequencies.Select((hz,i)=>new[]{hz,targetShape[i],measuredShape[i],eq[i],measuredShape[i]+eq[i]}).ToArray(),
   clipped,target.RequiresMeasurement?"测量与目标在1kHz对齐的EQ形状预测；不含输出机架、实际佩戴或重新实测":"相对数字EQ形状；不代表耳机匹配了声学目标",options);
 }
}
