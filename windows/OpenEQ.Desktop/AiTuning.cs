using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenEQ;

/// <summary>Offline, non-mutating analysis for AI tuning. No devices or audio IO.</summary>
public static class AiTuning {
 public static string Revision(Preset preset,string context)=>
  Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context+"\n"+preset.Encode()))).ToLowerInvariant();

 public static void RequireRevision(string expected,Preset current,string context){
  if(string.IsNullOrEmpty(expected)||!string.Equals(expected,Revision(current,context),StringComparison.Ordinal))
   throw new InvalidOperationException("调音状态或输出设备已改变，请重新读取并预览方案，未覆盖当前设置。");
 }

 public static object Capabilities()=>new{
  protocol_version=3,
  preset_format="org.openeq.preset",
  filter_types=Preset.Types,
  max_filters=32,
  frequency_hz=new[]{20,20000},
  band_gain_db=new[]{-24,24},
  q=new[]{.1,30},
  preamp_db=new[]{-60,0},
  workflow=new[]{"get_preset","preview_preset","apply_preset","undo_preset"},
  target_workflow=new[]{"list_targets","get_target","get_tuning_context","preview_target","apply_preset","undo_preset"},
  reset_sections=new[]{"output","compressor","matrix","spatial","all"},
  advanced_spatial=new{managed_path=true,native_apo=false,mid_filters=16,side_filters=16,features=new[]{"mid_side_eq","three_band_width","crossfeed","wet_dry","mono_mix"}},
  analysis=new[]{"analyze_response","get_dsd_diagnostics","get_audio_diagnostics","get_meter_snapshot"},
  sample_peak_only=true,
  notes="频响分析为数字滤波器估算，不是耳机实测、响度匹配或 true-peak 测量。"
 };

 public static TuningPreview Preview(Preset current,Preset proposed,int sampleRate,string context,bool autoHeadroom=true){
  if(sampleRate<8000||sampleRate>768000)throw new ArgumentOutOfRangeException(nameof(sampleRate));
  proposed.Validate();
  var candidate=proposed.Copy();
  var warnings=new List<string>();
  var filters=candidate.Bands.Where(b=>b.Enabled&&b.Frequency<sampleRate/2d).ToArray();
  var left=filters.Where(b=>b.Channel!="right").Select(b=>new Biquad(b,sampleRate)).ToArray();
  var right=filters.Where(b=>b.Channel!="left").Select(b=>new Biquad(b,sampleRate)).ToArray();
  double Response(Biquad[] bands,double frequency){
   double gain=0;foreach(var band in bands)gain+=20*Math.Log10(Math.Max(1e-12,band.Magnitude(frequency,sampleRate)));
   return gain;
  }
  double top=Math.Min(20000,sampleRate*.49),peak=0;
  var frequencies=Enumerable.Range(0,1537).Select(i=>20*Math.Pow(top/20,i/1536d))
   .Concat(filters.Select(b=>Math.Min(top,b.Frequency)));
  if(candidate.Enabled)foreach(double frequency in frequencies)
   peak=Math.Max(peak,Math.Max(Response(left,frequency),Response(right,frequency)));

  // Matrix/width can add gain even with EQ bypassed. Use the largest absolute
  // row sum as a conservative bound, plus positive compressor makeup gain.
  var m=candidate.Output.Matrix??new MatrixConfig();
  double a=(1+m.Width)/2,b=(1-m.Width)/2;
  double matrixBound=Math.Max(
   Math.Abs(a*m.Ll+b*m.Rl)+Math.Abs(a*m.Lr+b*m.Rr),
   Math.Abs(b*m.Ll+a*m.Rl)+Math.Abs(b*m.Lr+a*m.Rr));
  double rackBoost=20*Math.Log10(Math.Max(1,matrixBound));
  if(candidate.Output.Spatial is {Enabled:true} spatial){
   rackBoost+=20*Math.Log10(new SpatialTransfer(spatial,sampleRate).MaximumRowGain());
   warnings.Add("高级M/S/声场仅用于中转DSP链路；原生APO不支持。EQ图只显示主EQ，完整声道响应请用analyze_response。");
   if(spatial.MidBands.Concat(spatial.SideBands).Any(b=>b.Enabled&&b.Frequency>=sampleRate/2d))
    warnings.Add("M/S中超过奈奎斯特频率的滤波器会被忽略。");
  }
  var compressor=candidate.Output.Compressor??new CompConfig();
  if(compressor.Enabled)rackBoost+=Math.Max(0,compressor.MakeupDb);
  double preampLimit=-Math.Max(0,Math.Ceiling((peak+rackBoost+candidate.Output.Gain+1-1e-9)*10)/10);
  if(autoHeadroom){
   if(candidate.Enabled){
    if(preampLimit< -60)throw new ArgumentException("叠加增益过大，前级余量不足；请减少滤波器提升或输出处理增益。");
    candidate.Preamp=Math.Min(candidate.Preamp,preampLimit);
   }else{
    double outputLimit=-Math.Ceiling((rackBoost+1-1e-9)*10)/10;
    if(outputLimit< -60)throw new ArgumentException("输出处理叠加增益过大。");
    candidate.Output.Gain=Math.Min(candidate.Output.Gain,outputLimit);
    warnings.Add("EQ 旁路时前级不生效，自动余量改为降低输出增益。");
   }
  }
  candidate.Validate();
  if(candidate.Bands.Any(f=>f.Enabled&&f.Frequency>=sampleRate/2d))
   warnings.Add("存在超过当前奈奎斯特频率的频段，实际 DSP 和此预览均忽略这些频段。");
  if(!candidate.Output.Protection)warnings.Add("当前 sample-peak 保护已关闭。");
  warnings.Add("余量为频响/矩阵/补偿增益的保守估算，不保证重建波形的 true-peak，也不代表耳机测量结果。");
  var curve=Enumerable.Range(0,97).Select(i=>{
   double hz=20*Math.Pow(top/20,i/96d);
   return new[]{hz,candidate.Enabled?Response(left,hz)+candidate.Preamp:0,
    candidate.Enabled?Response(right,hz)+candidate.Preamp:0};
  }).ToArray();
  int changed=Enumerable.Range(0,Math.Max(current.Bands.Count,candidate.Bands.Count)).Count(i=>
   i>=current.Bands.Count||i>=candidate.Bands.Count||
   JsonSerializer.Serialize(current.Bands[i])!=JsonSerializer.Serialize(candidate.Bands[i]));
  double estimatedPeak=(candidate.Enabled?peak+candidate.Preamp:0)+rackBoost+candidate.Output.Gain;
  return new(candidate,Revision(current,context),sampleRate,changed,preampLimit,estimatedPeak,curve,warnings.ToArray());
 }
}

public sealed record TuningPreview(
 Preset Preset,string BaseRevision,int SampleRate,int ChangedFilters,
 double SuggestedPreampDb,double EstimatedPeakGainDb,double[][] EqResponse,string[] Warnings);
