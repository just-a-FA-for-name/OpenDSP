using System.Text.Json;
namespace OpenEQ;
internal static class TargetFittingChecks {
 public static int Run(string path){
  var cases=new List<string>();var report=new Dictionary<string,object>();
  void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
  void Reject(Action action,string name){bool failed=false;try{action();}catch(ArgumentException){failed=true;}Check(failed,name);}
  string Json(object value)=>JsonSerializer.Serialize(value);
  try{
   Check(TargetCurves.BuiltIn.Length>=30&&TargetCurves.BuiltIn.Count(c=>c.RequiresMeasurement)==15,"catalog_has_sourced_acoustic_targets_and_relative_preferences");
   foreach(var curve in TargetCurves.BuiltIn)Check(Enumerable.Range(0,100).Select(i=>curve.Response(20*Math.Pow(1000,i/99d))).All(double.IsFinite),"finite_curve_"+curve.Id);
   var target=TargetCurves.Find("harman-ie-2019");
   Check(target.RequiresMeasurement&&target.Rig=="711"&&target.Source.Contains("7ae0f56"),"source_and_measurement_system_are_explicit");
   Reject(()=>TargetFitting.Fit(target,null,384000,new()),"raw_acoustic_target_never_applied_without_measurement");
   var points=Enumerable.Range(0,401).Select(i=>20*Math.Pow(1000,i/400d)).ToArray();
   var deviation=new Biquad(new Band{Frequency=5500,Gain=4,Q=1},384000);
   // Synthetic fixtures only; no claims about SGOR Luna or any physical earphone.
   List<CurvePoint> Synthetic(double offset)=>points.Select(hz=>new CurvePoint(hz,target.Response(hz)+offset+20*Math.Log10(deviation.Magnitude(hz,384000)))).ToList();
   var measured=Synthetic(80);
   Reject(()=>TargetFitting.Fit(target,measured,384000,new(),"5128"),"different_measurement_system_is_rejected");
   var result=TargetFitting.Fit(target,measured,384000,new(Bands:20,Smooth:false),"711");
   Check(result.AfterRmsDb<result.BeforeRmsDb*.3,"known_synthetic_response_is_moved_toward_target");
   var shifted=TargetFitting.Fit(target,Synthetic(90),384000,new(Bands:20,Smooth:false),"711");
   Check(Math.Abs(result.AfterRmsDb-shifted.AfterRmsDb)<1e-6,"arbitrary_measurement_level_does_not_become_eq_gain");
   var prepared=result.Bands.Select(b=>new Biquad(b,384000)).ToArray();
   Check(result.Response.All(row=>Math.Abs(prepared.Sum(b=>20*Math.Log10(Math.Max(1e-12,b.Magnitude(row[0],384000))))-row[3])<1e-8),"prediction_uses_actual_384k_biquads");
   Check(result.Bands.Count<=20&&result.Response.All(row=>row[3]<=6.251&&row[3]>=-12.251),"band_count_and_summed_correction_limits_hold");
   Reject(()=>TargetFitting.Fit(target,measured.Where(p=>p.Hz>100).ToList(),384000,new(),"711"),"missing_frequency_coverage_is_not_extrapolated");
   Reject(()=>TargetCurves.ParseMeasured("20,0\n1000,NaN\n20000,0"),"nan_measurement_rejected");
   Reject(()=>TargetCurves.ParseMeasured("20,0\n1000,1\n1000,2\n20000,0"),"duplicate_frequencies_rejected");
   Check(TargetCurves.ParseMeasured("frequency,raw\n20000,0\n20,1\n1000,2")[0].Hz==20,"csv_header_and_unsorted_input_are_supported");
   var relative=TargetCurves.Find("ACG 甜润女声");
   Reject(()=>TargetFitting.Fit(relative,measured,384000,new(),"711"),"relative_shape_cannot_be_subtracted_from_raw_acoustic_measurement");
   var current=Preset.Flat();current.Name="user EQ";current.Preamp=0;current.Output.Gain=-7;current.Output.Balance=.25;
   current.Output.LeftDelay=3;current.Output.RightDelay=6;current.Output.InvertLeft=true;current.Output.Protection=false;current.Output.Ceiling=-4;current.Output.DcBlock=true;
   current.Output.Compressor=new CompConfig{Enabled=true,ThresholdDb=-27,Ratio=7,AttackMs=2,ReleaseMs=250,KneeDb=3,MakeupDb=2};
   current.Output.Matrix=new MatrixConfig{Ll=.8,Lr=.2,Rl=.1,Rr=.9,Width=1.2};
   string original=current.Encode(),eq=Json(current.Bands);
   var output=RackDefaults.Reset(current,"output");
   Check(output.Output.Gain==0&&output.Output.Balance==0&&output.Output.LeftDelay==0&&output.Output.RightDelay==0&&!output.Output.InvertLeft&&output.Output.Protection&&output.Output.Ceiling==-1,"output_reset_restores_every_output_default");
   Check(Json(output.Output.Compressor)==Json(current.Output.Compressor)&&Json(output.Output.Matrix)==Json(current.Output.Matrix)&&output.Output.DcBlock,"output_reset_keeps_other_modules");
   var comp=RackDefaults.Reset(current,"compressor");
   Check(Json(comp.Output.Compressor)==Json(new CompConfig())&&comp.Output.Gain==-7&&Json(comp.Output.Matrix)==Json(current.Output.Matrix),"compressor_reset_is_scoped");
   var matrix=RackDefaults.Reset(current,"matrix");
   Check(Json(matrix.Output.Matrix)==Json(new MatrixConfig())&&Json(matrix.Output.Compressor)==Json(current.Output.Compressor),"matrix_reset_is_scoped");
   var all=RackDefaults.Reset(current,"all");
   Check(Json(all.Output)==Json(new OutputConfig())&&Json(all.Bands)==eq&&all.Preamp==0&&all.Name==current.Name&&current.Encode()==original,"all_output_reset_preserves_eq_and_source");
   Reject(()=>RackDefaults.Reset(current,"device"),"unknown_reset_section_is_rejected");
   using var request=JsonDocument.Parse("""{"target_id":"preference-7","band_count":16,"auto_headroom":false}""");
   var proposal=TargetWorkflow.Build(request.RootElement,current,384000,"fixture");
   Check(current.Encode()==original&&proposal.Preview.Preset.Preamp==0&&Json(proposal.Preview.Preset.Output)==Json(current.Output),"target_preview_preserves_zero_preamp_output_rack_and_live_state");
   report["synthetic_before_rms_db"]=result.BeforeRmsDb;report["synthetic_after_rms_db"]=result.AfterRmsDb;
   report["catalog_count"]=TargetCurves.BuiltIn.Length;report["result"]="PASS";return 0;
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}
  finally{report["cases"]=cases;report["scope"]="Synthetic offline fitting and defaults only; no acoustic measurement, test tones or device writes";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
