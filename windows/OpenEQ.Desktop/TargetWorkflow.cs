using System.Text.Json;
namespace OpenEQ;

public sealed record TargetProposal(TargetCurves.Curve Target,TargetFitResult Fit,TuningPreview Preview) {
 public object Payload()=>new{
  ok=true,target=TargetWorkflow.Describe(Target),preset=Preview.Preset,base_revision=Preview.BaseRevision,sample_rate=Preview.SampleRate,
  fit=new{before_rms_db=Fit.BeforeRmsDb,after_rms_db=Fit.AfterRmsDb,maximum_error_db=Fit.MaximumErrorDb,clipped_points=Fit.ClippedPoints,
   band_count=Fit.Bands.Count,options=Fit.Options,scope=Fit.Scope,response_columns=new[]{"hz","target_shape_db","measured_shape_db","eq_db","predicted_shape_db"},response=Fit.Response},
  suggested_preamp_db=Preview.SuggestedPreampDb,estimated_peak_gain_db=Preview.EstimatedPeakGainDb,warnings=Preview.Warnings,
  apply_arguments=new{preset=Preview.Preset,expected_revision=Preview.BaseRevision,auto_headroom=false},
  workflow="Review predicted error, limits and headroom, then apply_preset using base_revision; verify by listening."
 };
}
public static class TargetWorkflow {
 public static object Describe(TargetCurves.Curve c)=>new{id=c.Id,name=c.Name,kind=c.Kind,rig=c.Rig,application=c.Application,description=c.Description,source=c.Source,requires_measurement=c.RequiresMeasurement};
 public static List<CurvePoint> Points(JsonElement value){
  if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>8192)throw new ArgumentException("频响点必须为不超过8192项的数组");
  var points=new List<CurvePoint>();
  foreach(var item in value.EnumerateArray()){
   if(item.ValueKind!=JsonValueKind.Array||item.GetArrayLength()!=2)throw new ArgumentException("频响点必须是 [frequency_hz, db]");
   points.Add(new(item[0].GetDouble(),item[1].GetDouble()));
  }
  points=points.OrderBy(p=>p.Hz).ToList();TargetCurves.ValidatePoints(points);return points;
 }
 public static TargetProposal Build(JsonElement request,Preset current,int rate,string context,List<CurvePoint>? loaded=null,string? loadedRig=null){
  TargetCurves.Curve target;
  if(request.TryGetProperty("target_points",out var custom)){
   if(request.TryGetProperty("target_id",out _))throw new ArgumentException("Choose target_id or target_points, not both");
   string kind=request.TryGetProperty("target_kind",out var k)?k.GetString()??"acoustic":"acoustic";
   if(kind is not ("acoustic" or "relative"))throw new ArgumentException("target_kind must be acoustic or relative");
   string rig=request.TryGetProperty("target_rig",out var tr)?tr.GetString()??"":"";
   if(kind=="acoustic"&&string.IsNullOrWhiteSpace(rig))throw new ArgumentException("Custom acoustic target requires target_rig");
   string name=request.TryGetProperty("target_name",out var tn)?tn.GetString()??"自定义目标":"自定义目标";
   if(name.Length>80)throw new ArgumentException("Target name too long");
   target=new(name,"用户提供的目标CSV/点列",TargetCurves.ToArray(Points(custom)),"custom",kind,kind=="relative"?"none":rig,"custom","用户提供；需自行核对测量体系和来源");
  }else target=TargetCurves.Find(request.GetProperty("target_id").GetString()!);
  var measured=request.TryGetProperty("measured",out var m)?Points(m):target.RequiresMeasurement?loaded:null;
  string? measurementRig=request.TryGetProperty("measurement_rig",out var mr)?mr.GetString():loadedRig;
  double Number(string key,double fallback)=>request.TryGetProperty(key,out var value)?value.GetDouble():fallback;
  int count=request.TryGetProperty("band_count",out var bc)?bc.GetInt32():20;
  var options=new TargetFitOptions(count,Number("min_hz",20),Number("max_hz",Math.Min(target.RequiresMeasurement?8000:20000,rate*.49)),
   Number("max_boost_db",6),Number("max_cut_db",12),Number("max_q",3),!request.TryGetProperty("smooth",out var sm)||sm.GetBoolean());
  var fit=TargetFitting.Fit(target,measured,rate,options,measurementRig);
  var next=current.Copy();next.Name="→ "+target.Name;next.Enabled=true;next.Bands=fit.Bands;
  var preview=AiTuning.Preview(current,next,rate,context,request.TryGetProperty("auto_headroom",out var ah)&&ah.GetBoolean());
  if(next.Output.Spatial is {Enabled:true})preview=preview with{Warnings=preview.Warnings.Append("高级空间块仍然开启；目标拟合误差仅针对主EQ，不代表加入M/S/交叉馈送后的最终耳机响应。需要基准匹配时可先旁路空间模块。").ToArray()};
  return new(target,fit,preview);
 }
}
