using System.Numerics;
using System.Text.Json;
using NAudio.Wave;
namespace OpenEQ;
internal static class SpatialChecks {
 public static int Run(string path){
  var cases=new List<string>();var report=new Dictionary<string,object>();
  void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
  try{
   const int rate=384000;
   var disabled=new SpatialProcessor(new(){MidGain=6,LowWidth=0,Crossfeed=.4},rate);
   disabled.Process(.123,-.234);Check(disabled.Left==.123&&disabled.Right==-.234,"disabled_block_is_bit_exact_bypass");
   var unity=new SpatialProcessor(new(){Enabled=true},rate);unity.Process(.123,-.234);
   Check(Math.Abs(unity.Left-.123)<1e-15&&Math.Abs(unity.Right+.234)<1e-15,"enabled_defaults_preserve_stereo");
   var dry=new SpatialProcessor(new(){Enabled=true,Mix=0,MidGain=6,Mono=true,Crossfeed=.4},rate);dry.Process(.123,-.234);
   Check(dry.Left==.123&&dry.Right==-.234,"zero_mix_is_bit_exact_bypass");
   var sideOnly=new SpatialProcessor(new(){Enabled=true,SideGain=6,SideBands=new(){new(){Frequency=1500,Gain=12}},LowWidth=0,MidWidth=2,HighWidth=2,Crossfeed=.4},rate);
   double monoError=0;for(int i=0;i<rate/10;i++){double x=.1*Math.Sin(2*Math.PI*1500*i/rate);sideOnly.Process(x,x);monoError=Math.Max(monoError,Math.Max(Math.Abs(sideOnly.Left-x),Math.Abs(sideOnly.Right-x)));}
   Check(monoError<1e-12,"side_eq_width_and_crossfeed_preserve_centered_mono");
   var mono=new SpatialProcessor(SpatialScenes.Create("mono"),rate);double difference=0;
   for(int i=0;i<10000;i++){mono.Process(.1*Math.Sin(i*.02),.2*Math.Cos(i*.03));difference=Math.Max(difference,Math.Abs(mono.Left-mono.Right));}
   Check(difference==0,"mono_scene_folds_both_channels_identically");
   double worst=0;
   var config=SpatialScenes.Create("wide");config.Crossfeed=.23;config.CrossfeedDelay=.37;config.Mix=.72;config.MidGain=1.2;config.SideGain=-.6;
   config.MidBands.Add(new(){Frequency=2000,Gain=2,Q=.9});config.SideBands.Add(new(){Frequency=500,Gain=-2,Q=1.2});
   foreach(double hz in new[]{100d,1000,8000,16000}){
    var processor=new SpatialProcessor(config,rate);var expected=new SpatialTransfer(config,rate).At(hz);
    Complex left=0,right=0;int warm=rate/10,count=rate/5;
    for(int i=0;i<warm+count;i++){
     double phase=2*Math.PI*hz*i/rate,x=.02*Math.Cos(phase);processor.Process(x,0);
     if(i>=warm){var reference=Complex.FromPolarCoordinates(1,-phase);left+=processor.Left*reference;right+=processor.Right*reference;}
    }
    left*=2/(count*.02);right*=2/(count*.02);
    worst=Math.Max(worst,Math.Max((left-expected.Direct).Magnitude,(right-expected.Cross).Magnitude));
   }
   Check(worst<1e-8,"384k_sample_processing_matches_complex_spatial_transfer");
   var bass=new SpatialTransfer(SpatialScenes.Create("bass-center"),rate);
   var bassLow=bass.At(30);var bassHigh=bass.At(6000);
   Check((bassLow.Direct-bassLow.Cross).Magnitude<(bassHigh.Direct-bassHigh.Cross).Magnitude*.3,"low_center_scene_reduces_low_side_energy_more_than_high");
   var preset=Preset.Blank();preset.Output.Spatial=config;preset.Output.Protection=false;
   preset.Output.Matrix=new(){Ll=1,Lr=.15,Rl=-.05,Rr=.9,Width=1.1};preset.Output.Balance=.1;preset.Output.LeftDelay=.1;preset.Output.RightDelay=.2;
   var linear=LinearResponse.Analyze(preset,rate);var point=linear[64];double frequency=point.FrequencyHz;
   var dsp=new StereoProcessor(rate);dsp.Configure(preset);Complex outL=0,outR=0;int start=rate/5,frames=rate;
   for(int i=0;i<start+frames;i++){double phase=2*Math.PI*frequency*i/rate;dsp.Process(.01*Math.Cos(phase),0);if(i>=start){var reference=Complex.FromPolarCoordinates(1,-phase);outL+=dsp.Left*reference;outR+=dsp.Right*reference;}}
   outL*=2/(frames*.01);outR*=2/(frames*.01);
   Check(Math.Abs(20*Math.Log10(outL.Magnitude)-point.LeftToLeft.GainDb!.Value)<.01&&Math.Abs(20*Math.Log10(outR.Magnitude)-point.LeftToRight.GainDb!.Value)<.01,"complete_linear_analysis_includes_spatial_and_output_matrix_order");
   var protectedPreset=Preset.Blank();protectedPreset.Output.Spatial=new(){Enabled=true,MidGain=6,SideGain=6,LowWidth=2,MidWidth=2,HighWidth=2};
   var preview=AiTuning.Preview(Preset.Blank(),protectedPreset,rate,"fixture");
   Check(preview.Preset.Preamp<=-13&&preview.EstimatedPeakGainDb<=-.99,"headroom_analysis_accounts_for_spatial_gain");
   bool refused=false;try{NativeApo.EnsureSupported(protectedPreset);}catch(InvalidOperationException){refused=true;}
   Check(refused,"native_apo_rejects_unsupported_spatial_before_device_access");
   protectedPreset.Output.Spatial.Enabled=false;NativeApo.EnsureSupported(protectedPreset);
   Check(true,"disabled_spatial_can_be_stored_on_native_path");
   var bank=Preset.Blank();bank.Output.Spatial=config;bank.Output.Gain=-3;
   var reset=RackDefaults.Reset(bank,"output");
   Check(JsonSerializer.Serialize(reset.Output.Spatial)==JsonSerializer.Serialize(config),"output_reset_preserves_spatial_module");
   Check(!RackDefaults.Reset(bank,"spatial").Output.Spatial.Enabled&&RackDefaults.Reset(bank,"spatial").Output.Gain==-3,"spatial_reset_is_scoped");
   var provider=new DspProvider(new Input(rate),bank);var samples=new float[4096];
   provider.Read(samples,0,samples.Length);bank.Output.Spatial.SideGain=-1;provider.Update(bank);
   long before=GC.GetAllocatedBytesForCurrentThread();provider.Read(samples,0,samples.Length);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
   Check(allocated==0&&samples.All(float.IsFinite),"prepared_spatial_update_allocates_zero_in_audio_callback");
   var invalid=new DspProvider(new Input(rate,true),bank);invalid.Read(samples,0,samples.Length);for(int i=0;i<30;i++)invalid.Read(samples,0,samples.Length);
   Check(samples.All(float.IsFinite)&&invalid.InvalidSamples==2,"non_finite_input_does_not_poison_spatial_state");
   Check(Preset.Parse(bank.Encode()).Output.Spatial.SideBands.Count==config.SideBands.Count,"spatial_banks_round_trip_with_preset");
   var maximum=Preset.Blank();maximum.Output.Spatial=new(){Enabled=true,Crossfeed=.2};
   for(int i=0;i<32;i++)maximum.Bands.Add(new(){Frequency=30*Math.Pow(500,i/31d),Gain=i%2==0?.5:-.5,Q=1});
   for(int i=0;i<16;i++){maximum.Output.Spatial.MidBands.Add(new(){Frequency=40*Math.Pow(400,i/15d),Gain=.3});maximum.Output.Spatial.SideBands.Add(new(){Frequency=50*Math.Pow(300,i/15d),Gain=-.3});}
   var full=new DspProvider(new Input(rate),maximum);var block=new float[7680];for(int i=0;i<5;i++)full.Read(block,0,block.Length);
   long memory=GC.GetAllocatedBytesForCurrentThread(),started=System.Diagnostics.Stopwatch.GetTimestamp();
   for(int i=0;i<100;i++)full.Read(block,0,block.Length);
   double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;long used=GC.GetAllocatedBytesForCurrentThread()-memory;
   Check(used==0&&block.All(float.IsFinite),"32_main_plus_16_mid_plus_16_side_remain_finite_and_allocation_free_at384k");
   report["one_second_maximum_bank_processing_ms"]=elapsed;
   report["maximum_spatial_complex_error"]=worst;report["callback_allocation_bytes"]=allocated;report["result"]="PASS";return 0;
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}
  finally{report["cases"]=cases;report["scope"]="Offline synthetic processing at384k; no device writes or test audio";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
 }
 sealed class Input:ISampleProvider {
  public WaveFormat WaveFormat{get;}readonly bool invalid;bool sent;long frame;
  public Input(int rate,bool invalid=false){WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(rate,2);this.invalid=invalid;}
  public int Read(float[] b,int offset,int count){for(int i=0;i<count;i+=2){b[offset+i]=(float)(.03*Math.Sin(frame*.02));b[offset+i+1]=(float)(.03*Math.Cos(frame++*.03));}if(invalid&&!sent){b[offset]=float.NaN;b[offset+1]=float.PositiveInfinity;sent=true;}return count;}
 }
}
