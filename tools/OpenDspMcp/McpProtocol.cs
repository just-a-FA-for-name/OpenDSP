using System.Text.Json;

internal sealed class McpProtocol(Func<string,Task<string>> request,Func<TimeSpan,Task>? delay=null) {
 readonly Func<TimeSpan,Task> pause=delay??Task.Delay;
 static object Schema(object properties,params string[] required)=>new{type="object",properties,required,additionalProperties=false};
 static readonly object PresetSchema=new{type="object",description="Complete org.openeq.preset from get_preset. Modify a copy, preserving unrelated output settings."};
 static readonly (string Name,string Description,object Schema,bool ReadOnly)[] Tools={
  ("get_state","Read audio/DSP state. Transport counters do not prove acoustic DSD continuity.",Schema(new{}),true),
  ("get_capabilities","Read preset limits and AI tuning workflow.",Schema(new{}),true),
  ("get_preset","Read the full preset and revision covering settings, output device, rate and mode.",Schema(new{}),true),
  ("list_targets","List sourced acoustic targets and relative listening preferences. Check rig, application, source and requires_measurement; an acoustic target is not a direct EQ preset.",Schema(new{}),true),
  ("get_target","Read one target's metadata and frequency/dB points. Acoustic matching requires raw headphone measurements from the same rig; never invent measurements.",Schema(new{target_id=new{type="string"}},"target_id"),true),
  ("get_tuning_context","Read frequency-response data imported in the GUI, its source/rig, current revision and sample rate. Null measurement means no acoustic data is loaded.",Schema(new{}),true),
  ("preview_target","Read-only fitting at the active sample rate. Supply target_id OR custom target_points; acoustic targets require same-rig raw measured data (or GUI-imported data). Relative targets shape EQ only. Returns preset/base_revision, predicted RMS error, limits and response. Preserve 0dB unless user requests headroom reduction. Apply using apply_preset with auto_headroom matching this preview, then listen or remeasure.",Schema(new{
   target_id=new{type="string"},target_points=new{type="array",maxItems=8192},target_name=new{type="string"},target_kind=new{type="string",@enum=new[]{"acoustic","relative"}},target_rig=new{type="string"},
   measured=new{type="array",maxItems=8192},measurement_rig=new{type="string"},band_count=new{type="integer",minimum=1,maximum=32,@default=20},
   min_hz=new{type="number",minimum=20,maximum=19999},max_hz=new{type="number",minimum=21,maximum=20000},max_boost_db=new{type="number",minimum=0,maximum=12},
   max_cut_db=new{type="number",minimum=.1,maximum=24},max_q=new{type="number",minimum=.3,maximum=8},smooth=new{type="boolean",@default=true},auto_headroom=new{type="boolean",@default=false}
  }),true),
  ("reset_output","Restore defaults for output/compressor/matrix/spatial/all as one undoable edit. Keeps main EQ, preamp and transport. all also resets the spatial block. Requires the latest revision.",Schema(new{section=new{type="string",@enum=new[]{"output","compressor","matrix","spatial","all"}},expected_revision=new{type="string"}},"section","expected_revision"),false),
  ("list_spatial_scenes","Read five optional spatial scenes and their complete settings. Managed DSP path only; no scene is applied by this tool. M/S central information is not isolated vocals.",Schema(new{}),true),
  ("set_spatial","Replace output.spatial as one undoable edit using a copy from get_preset. Supports enabled, mix 0..1, mid_gain_db -12..6, side_gain_db -18..6, mid_filters/side_filters (max16 each, channel both), low/mid/high_width 0..2, crossover Hz, crossfeed 0..0.45, crossfeed_hz 100..1500, crossfeed_delay_ms 0..0.8 and mono. Preserves main EQ, preamp and other rack settings. Requires managed DSP; native APO rejects enabled spatial processing.",Schema(new{spatial=new{type="object"},expected_revision=new{type="string"}},"spatial","expected_revision"),false),
  ("apply_spatial_scene","Apply one scene from list_spatial_scenes to output.spatial only. Undoable, revision-guarded, managed path only. Does not start playback or change format.",Schema(new{scene_id=new{type="string"},expected_revision=new{type="string"}},"scene_id","expected_revision"),false),
  ("get_dsd_diagnostics","Read DoP integrity, queues and modulator overloads. Does not play a test signal.",Schema(new{}),true),
  ("get_audio_diagnostics","Read malformed-input counters and last engine error. Null relay counters mean unavailable, not a clean native-APO bill of health.",Schema(new{}),true),
  ("measure_levels","Measure 1–10 seconds of digital input/output RMS and applied gain on the managed WASAPI path. No recording or test tones. Rejects preset/device/stream changes; silence is null, not invented loudness. Peaks are lifetime maxima.",Schema(new{seconds=new{type="number",minimum=1,maximum=10,@default=3}}),true),
  ("analyze_response","Theoretical magnitude, phase and group delay of the current linear DSP paths. Not a headphone measurement or end-to-end latency measurement.",Schema(new{}),true),
  ("preview_preset","Preview a complete preset without changing sound. Returns normalized preset, headroom, EQ response and base_revision. Prefer this before apply_preset.",Schema(new{preset=PresetSchema,auto_headroom=new{type="boolean",@default=true}},"preset"),true),
  ("apply_preset","Apply a complete preset in one transaction. Pass the preview's base_revision as expected_revision; stale settings/device changes are rejected. Does not start playback or switch formats.",Schema(new{preset=PresetSchema,expected_revision=new{type="string"},auto_headroom=new{type="boolean",@default=true}},"preset","expected_revision"),false),
  ("undo_preset","Undo the last tuning edit, guarded by the current revision.",Schema(new{expected_revision=new{type="string"}},"expected_revision"),false),
  ("set_band","Legacy single-band edit. Prefer preview_preset/apply_preset for related changes.",Schema(new{index=new{type="integer",minimum=0,maximum=31},type=new{type="string"},frequency_hz=new{type="number",minimum=20,maximum=20000},gain_db=new{type="number",minimum=-24,maximum=24},q=new{type="number",minimum=.1,maximum=30},channel=new{type="string"},enabled=new{type="boolean"}},"index"),false),
  ("add_band","Add one EQ band.",Schema(new{type=new{type="string"},frequency_hz=new{type="number"},gain_db=new{type="number"},q=new{type="number"},channel=new{type="string"}}),false),
  ("remove_band","Remove one EQ band.",Schema(new{index=new{type="integer",minimum=0,maximum=31}},"index"),false),
  ("set_preamp","Set preamp, -60..0 dB.",Schema(new{db=new{type="number",minimum=-60,maximum=0}},"db"),false),
  ("set_output","Set output gain, balance, delay, polarity and sample-peak protection.",Schema(new{gain_db=new{type="number"},balance=new{type="number"},left_delay_ms=new{type="number"},right_delay_ms=new{type="number"},invert_left=new{type="boolean"},invert_right=new{type="boolean"},protection=new{type="boolean"},ceiling_db=new{type="number"}}),false),
  ("set_compressor","Set stereo-linked compressor.",Schema(new{enabled=new{type="boolean"},threshold_db=new{type="number"},ratio=new{type="number"},attack_ms=new{type="number"},release_ms=new{type="number"},knee_db=new{type="number"},makeup_db=new{type="number"}}),false),
  ("set_matrix","Set channel matrix and stereo width.",Schema(new{ll=new{type="number"},lr=new{type="number"},rl=new{type="number"},rr=new{type="number"},width=new{type="number"}}),false),
  ("set_dc_block","Enable/disable DC blocking.",Schema(new{enabled=new{type="boolean"}},"enabled"),false),
  ("set_eq_enabled","Enable/disable EQ.",Schema(new{enabled=new{type="boolean"}},"enabled"),false),
  ("set_dsd","Switch off/dop64/dop128. Reopens output; use only for a requested mode change on a compatible DoP DAC. Not an automatic tone test.",Schema(new{mode=new{type="string",@enum=new[]{"off","dop64","dop128"}}},"mode"),false),
  ("apply_curve","Legacy guarded target application. Prefer list_targets/preview_target then apply_preset. name is a catalog ID or name; acoustic targets require real same-rig measurements.",Schema(new{name=new{type="string"},measured=new{type="array",maxItems=8192},measurement_rig=new{type="string"},expected_revision=new{type="string"},auto_headroom=new{type="boolean",@default=false}},"name","expected_revision"),false),
  ("save_preset","Name and save the current preset.",Schema(new{name=new{type="string"}}),false)
 };
 static readonly JsonSerializerOptions Options=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
 static string Error(object? id,int code,string message)=>JsonSerializer.Serialize(new{jsonrpc="2.0",id,error=new{code,message}},Options);

 public async Task<string?> Handle(string line){
  JsonDocument document;
  try{document=JsonDocument.Parse(line);}catch(JsonException){return Error(null,-32700,"Invalid JSON");}
  using(document){
   var root=document.RootElement;
   if(root.ValueKind!=JsonValueKind.Object)return Error(null,-32600,"Expected a JSON-RPC object");
   bool hasId=root.TryGetProperty("id",out var idValue);
   object? id=hasId?idValue.Clone():null;
   if(hasId&&idValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
    return Error(null,-32600,"Invalid request ID");
   if(!root.TryGetProperty("jsonrpc",out var version)||version.ValueKind!=JsonValueKind.String||version.GetString()!="2.0"||
      !root.TryGetProperty("method",out var methodValue)||methodValue.ValueKind!=JsonValueKind.String)
    return Error(id,-32600,"Invalid JSON-RPC request");
   if(!hasId)return null; // Notifications never execute a tuning operation.
   object result;
   string method=methodValue.GetString()!;
   switch(method){
    case "initialize":{
     string protocol="2024-11-05";
     if(root.TryGetProperty("params",out var init)&&init.ValueKind==JsonValueKind.Object&&init.TryGetProperty("protocolVersion",out var pv)&&pv.ValueKind==JsonValueKind.String&&pv.GetString() is "2024-11-05" or "2025-03-26" or "2025-06-18")protocol=pv.GetString()!;
     result=new{protocolVersion=protocol,capabilities=new{tools=new{}},serverInfo=new{name="opendsp-mcp",version="0.5.0"}};
     break;
    }
    case "ping":result=new{};break;
    case "tools/list":result=new{tools=Tools.Select(t=>new{name=t.Name,description=t.Description,inputSchema=t.Schema,annotations=new{readOnlyHint=t.ReadOnly,openWorldHint=false}}).ToArray()};break;
    case "tools/call":{
     if(!root.TryGetProperty("params",out var p)||p.ValueKind!=JsonValueKind.Object||!p.TryGetProperty("name",out var nameValue)||nameValue.ValueKind!=JsonValueKind.String)
      return Error(id,-32602,"tools/call requires a tool name");
     string name=nameValue.GetString()!;
     var tool=Tools.FirstOrDefault(t=>t.Name==name);
     if(tool.Name==null)return Error(id,-32602,"Unknown tool: "+name);
     bool hasArguments=p.TryGetProperty("arguments",out var arguments);
     if(hasArguments&&arguments.ValueKind!=JsonValueKind.Object)return Error(id,-32602,"arguments must be an object");
     var schema=JsonSerializer.SerializeToElement(tool.Schema);
     var properties=schema.GetProperty("properties");
     var cmd=new Dictionary<string,object?>{{"cmd",name}};
     if(hasArguments)foreach(var item in arguments.EnumerateObject()){
      if(!properties.TryGetProperty(item.Name,out _))return Error(id,-32602,"Unsupported argument: "+item.Name);
      if(!cmd.TryAdd(item.Name,item.Value.Clone()))return Error(id,-32602,"Duplicate argument: "+item.Name);
     }
     foreach(var required in schema.GetProperty("required").EnumerateArray())
      if(!cmd.ContainsKey(required.GetString()!))return Error(id,-32602,"Missing argument: "+required.GetString());
     string upstream;bool failed;
     try{
      if(name=="measure_levels"){
       double seconds=3;
       if(hasArguments&&arguments.TryGetProperty("seconds",out var duration)){
        if(duration.ValueKind!=JsonValueKind.Number||!duration.TryGetDouble(out seconds)||!double.IsFinite(seconds)||seconds<1||seconds>10)
         return Error(id,-32602,"seconds must be between 1 and 10");
       }
       using var first=JsonDocument.Parse(await request("{\"cmd\":\"get_meter_snapshot\"}"));
       if(!first.RootElement.TryGetProperty("ok",out var ready)||ready.ValueKind!=JsonValueKind.True)
        throw new InvalidOperationException("OpenDSP meter unavailable; use the current candidate on an active managed WASAPI path.");
       var watch=System.Diagnostics.Stopwatch.StartNew();
       await pause(TimeSpan.FromSeconds(seconds));
       using var second=JsonDocument.Parse(await request("{\"cmd\":\"get_meter_snapshot\"}"));
       upstream=JsonSerializer.Serialize(LevelMeasurement.Compare(first.RootElement,second.RootElement,watch.Elapsed.TotalSeconds),Options);
      }else upstream=await request(JsonSerializer.Serialize(cmd,Options));
      using var reply=JsonDocument.Parse(upstream);
      failed=reply.RootElement.ValueKind!=JsonValueKind.Object||!reply.RootElement.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True;
     }catch(Exception ex){upstream=JsonSerializer.Serialize(new{ok=false,error=ex.Message},Options);failed=true;}
     result=new{content=new[]{new{type="text",text=upstream}},isError=failed};break;
    }
    default:return Error(id,-32601,"Method not found: "+method);
   }
   return JsonSerializer.Serialize(new{jsonrpc="2.0",id,result},Options);
  }
 }
}
