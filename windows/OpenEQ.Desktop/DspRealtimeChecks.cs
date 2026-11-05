using NAudio.Wave;
using System.Text.Json;
namespace OpenEQ;
internal static class DspRealtimeChecks {
 sealed class InvalidOnce:ISampleProvider {
  bool first=true;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
  public int Read(float[] b,int offset,int count){Array.Fill(b,.1f,offset,count);if(first&&count>=2){b[offset]=float.NaN;b[offset+1]=float.PositiveInfinity;first=false;}return count;}
 }
 sealed class Constant:ISampleProvider {public WaveFormat WaveFormat{get;}public Constant(int rate)=>WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(rate,2);public int Read(float[] b,int offset,int count){Array.Fill(b,.1f,offset,count);return count;}}
 public static int Run(string path){var cases=new List<string>();void Check(bool yes,string name){if(!yes)throw new Exception(name);cases.Add(name);}var report=new Dictionary<string,object>();try{
  var source=new Constant(48000);var dsp=new DspProvider(source,Preset.Flat());var samples=new float[1920];for(int n=0;n<10;n++)dsp.Read(samples,0,samples.Length);var next=Preset.Flat();next.Preamp=-6;dsp.Update(next);long before=GC.GetAllocatedBytesForCurrentThread();dsp.Read(samples,0,samples.Length);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;Check(allocated==0,"parameter_swap_allocates_zero_bytes_in_audio_callback");for(int n=0;n<30;n++)dsp.Read(samples,0,samples.Length);Check(Math.Abs(samples[^1]-.1*Math.Pow(10,-6d/20))<1e-6,"prepared_parameter_update_reaches_requested_gain");
  var shortBlock=new float[128];next.Preamp=-12;dsp.Update(next);dsp.Read(shortBlock,0,shortBlock.Length);next.Preamp=-3;dsp.Update(next);for(int n=0;n<100;n++)dsp.Read(shortBlock,0,shortBlock.Length);Check(Math.Abs(shortBlock[^1]-.1*Math.Pow(10,-3d/20))<1e-6,"rapid_update_keeps_latest_request_after_crossfade");
  var invalid=Preset.Flat();invalid.Bands[0].Type="unsupported";bool rejected=false;try{dsp.Update(invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"invalid_update_rejected_on_control_thread");dsp.Read(samples,0,samples.Length);Check(Math.Abs(samples[^1]-.1*Math.Pow(10,-3d/20))<1e-6,"rejected_update_retains_previous_processor");
  var highBand=Preset.Flat();highBand.Bands.Clear();highBand.Bands.Add(new Band{Frequency=20000,Gain=6});var lowRate=new DspProvider(new Constant(32000),highBand);for(int n=0;n<30;n++)lowRate.Read(samples,0,samples.Length);Check(Math.Abs(samples[^1]-.1)<1e-6,"bands_above_nyquist_skipped_consistently_with_native_apo");
  var fullRate=new DspProvider(new Constant(384000),PresetImport.Graphic31());
  var fullPreset=PresetImport.Graphic31();for(int i=0;i<5;i++)fullRate.Update(fullPreset);
  long updateBefore=GC.GetAllocatedBytesForCurrentThread();
  for(int i=0;i<20;i++){fullPreset.Preamp=-i*.1;fullRate.Update(fullPreset);}
  long updateBytes=(GC.GetAllocatedBytesForCurrentThread()-updateBefore)/20;
  Check(updateBytes<128*1024,"384k_zero_delay_updates_do_not_allocate_full_100ms_delay_buffers");
  report["384k_parameter_update_bytes"]=updateBytes;
  foreach(var delays in new[]{(0d,0d),(7.5,12.25),(100d,0d)}){
   var delayed=Preset.Flat();delayed.Output.LeftDelay=delays.Item1;delayed.Output.RightDelay=delays.Item2;
   delayed.Output.Balance=.2;delayed.Output.InvertLeft=true;
   delayed.Output.DcBlock=true;delayed.Output.Matrix=new MatrixConfig{Width=1.18,Lr=.2};
   delayed.Output.Compressor=new CompConfig{Enabled=true,ThresholdDb=-24,Ratio=3};
   var compact=new DspProvider(new Constant(384000),delayed);var reference=new StereoProcessor(384000);reference.Configure(delayed);
   double error=0;
   for(int block=0;block<64;block++){compact.Read(samples,0,samples.Length);for(int i=0;i<samples.Length;i+=2){reference.Process(.1f,.1f);error=Math.Max(error,Math.Max(Math.Abs(samples[i]-(float)reference.Left),Math.Abs(samples[i+1]-(float)reference.Right)));}}
   Check(error==0,$"384k_delay_{delays.Item1}_{delays.Item2}_matches_full_capacity_reference_sample_for_sample");
  }
  var fifo=new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(384000,2));int missingCalls=0,missingBytes=0;
  var padded=new PaddedCaptureProvider(fifo,n=>{missingCalls++;missingBytes+=n;});var input=Enumerable.Range(1,16).Select(n=>(byte)n).ToArray();var output=Enumerable.Repeat((byte)0xcc,40).ToArray();fifo.AddSamples(input,0,input.Length);
  Check(padded.Read(output,5,24)==24&&output.AsSpan(5,16).SequenceEqual(input)&&output.AsSpan(21,8).ToArray().All(v=>v==0)&&output[4]==0xcc&&output[29]==0xcc,"partial_capture_read_preserves_payload_and_pads_only_missing_bytes");
  Check(missingCalls==1&&missingBytes==8,"partial_underflow_is_counted_not_only_completely_empty_reads");padded.Read(output,5,24);Check(missingCalls==2&&missingBytes==32,"empty_capture_read_reports_exact_missing_bytes");
  fifo.AddSamples(input,0,input.Length);padded.Read(output,5,16);Check(missingCalls==2,"complete_capture_read_never_reports_starvation");
  var timed=new TimedRenderProvider(padded);timed.Read(output,0,16);long callbackBefore=GC.GetAllocatedBytesForCurrentThread();timed.Read(output,0,16);Check(GC.GetAllocatedBytesForCurrentThread()==callbackBefore,"padding_and_render_telemetry_allocate_zero_bytes");
  var guarded=new DspProvider(new InvalidOnce(),Preset.Flat());guarded.Read(samples,0,samples.Length);
  Check(samples.All(float.IsFinite)&&guarded.InvalidSamples==2&&double.IsFinite(guarded.EnergyBefore),"invalid_input_is_counted_without_poisoning_dsp_or_telemetry");
  for(int n=0;n<30;n++)guarded.Read(samples,0,samples.Length);
  Check(Math.Abs(samples[^1]-.1)<1e-6,"normal_audio_recovers_after_invalid_input");
  var integer=new PcmProvider(new InvalidOnce(),24);var integerBytes=new byte[48];integer.Read(integerBytes,0,integerBytes.Length);
  Check(integerBytes.Take(6).All(b=>b==0),"nan_never_converts_to_full_scale_negative_pcm");
  var floating=new FloatProvider(new InvalidOnce(),WaveFormat.CreateIeeeFloatWaveFormat(48000,2));var floatBytes=new byte[32];floating.Read(floatBytes,0,floatBytes.Length);
  Check(BitConverter.ToSingle(floatBytes,0)==0&&BitConverter.ToSingle(floatBytes,4)==0,"float_output_sanitizes_invalid_samples");
  report["callback_allocation_bytes"]=allocated;report["result"]="PASS";return 0;
 }catch(Exception e){report["result"]="FAIL";report["exception"]=e.ToString();return 1;}finally{report["cases"]=cases;report["scope"]="Offline DSP updates, no device, format, routing or audio service changes";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}}
}
