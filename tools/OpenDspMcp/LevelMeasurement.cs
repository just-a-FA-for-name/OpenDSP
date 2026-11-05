using System.Text.Json;

/// <summary>Windowed digital power from counter deltas; no audio capture/files.</summary>
internal static class LevelMeasurement {
 static double Number(JsonElement value,string key){
  double number=value.GetProperty(key).GetDouble();
  if(!double.IsFinite(number)||number<0)throw new InvalidDataException("Invalid meter counter: "+key);
  return number;
 }
 static long Counter(JsonElement value,string key){
  long number=value.GetProperty(key).GetInt64();
  if(number<0)throw new InvalidDataException("Invalid meter counter: "+key);
  return number;
 }
 static double Difference(JsonElement before,JsonElement after,string key){
  double result=Number(after,key)-Number(before,key);
  if(result<0)throw new InvalidOperationException("Meter counters reset; measure again after the stream settles.");
  return result;
 }
 static double? PowerDb(double power)=>power>0?10*Math.Log10(power):null;
 static double? PeakDb(double peak)=>peak>0?20*Math.Log10(peak):null;
 public static object Compare(JsonElement before,JsonElement after,double elapsedSeconds){
  if(before.GetProperty("ok").ValueKind!=JsonValueKind.True||after.GetProperty("ok").ValueKind!=JsonValueKind.True)
   throw new InvalidOperationException("Meter data unavailable for the current output path.");
  if(before.GetProperty("stream_id").GetString()!=after.GetProperty("stream_id").GetString()||
     before.GetProperty("revision").GetString()!=after.GetProperty("revision").GetString()||
     before.GetProperty("sample_rate").GetInt32()!=after.GetProperty("sample_rate").GetInt32())
   throw new InvalidOperationException("Stream, preset or device changed during measurement; result discarded.");
  long frames=Counter(after,"frames")-Counter(before,"frames");
  int rate=after.GetProperty("sample_rate").GetInt32();
  if(frames<=0||rate<=0)throw new InvalidOperationException("No continuous processed frames in this interval.");
  double il=Difference(before,after,"input_left_energy")/frames;
  double ir=Difference(before,after,"input_right_energy")/frames;
  double ol=Difference(before,after,"output_left_energy")/frames;
  double oright=Difference(before,after,"output_right_energy")/frames;
  double input=(il+ir)/2,output=(ol+oright)/2;
  return new{
   ok=true,elapsed_seconds=elapsedSeconds,processed_audio_seconds=frames/(double)rate,frames,sample_rate=rate,
   has_input_signal=input>0,
   input_rms_dbfs=PowerDb(input),output_rms_dbfs=PowerDb(output),
   applied_gain_db=input>0&&output>0?(double?)(10*Math.Log10(output/input)):null,
   input_left_rms_dbfs=PowerDb(il),input_right_rms_dbfs=PowerDb(ir),
   output_left_rms_dbfs=PowerDb(ol),output_right_rms_dbfs=PowerDb(oright),
   lifetime_input_peak_dbfs=PeakDb(Number(after,"lifetime_input_peak")),
   lifetime_output_peak_dbfs=PeakDb(Number(after,"lifetime_output_peak")),
   capture_underflow_bytes=Difference(before,after,"capture_underflow_bytes"),
   capture_overflows=Difference(before,after,"capture_overflows"),
   invalid_input_samples=Difference(before,after,"input_invalid_samples"),
   scope="Managed DSP input/output RMS before output encoding, DoP attenuation and DSD headroom; excludes OS/DAC gain. Peaks are current DSP-processor lifetime maxima, not window peaks. Not LUFS, true-peak, acoustic loudness or a sound-quality judgment."
  };
 }
}
