using System.Text.Json;

internal static class McpChecks {
 public static async Task<int> Run(string path){
  var cases=new List<string>();
  void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
  var report=new Dictionary<string,object>();
  try{
   int calls=0;string? forwarded=null;string upstream="{ \"ok\": true, \"value\": 1 }";
   var protocol=new McpProtocol(json=>{calls++;forwarded=json;return Task.FromResult(upstream);});
   async Task<JsonElement> Send(string json){
    string response=await protocol.Handle(json)??throw new Exception("Expected a reply");
    using var document=JsonDocument.Parse(response);return document.RootElement.Clone();
   }
   var invalid=await Send("{");
   Check(invalid.GetProperty("error").GetProperty("code").GetInt32()==-32700,"invalid_json_has_parse_error");
   Check((await Send("[]")).GetProperty("error").GetProperty("code").GetInt32()==-32600,"invalid_request_is_contained");
   var init=await Send("""{"jsonrpc":"2.0","id":"init","method":"initialize","params":{"protocolVersion":"2025-03-26"}}""");
   Check(init.GetProperty("id").GetString()=="init"&&init.GetProperty("result").GetProperty("protocolVersion").GetString()=="2025-03-26","initialize_negotiates_supported_protocol_and_preserves_id");
   var list=(await Send("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")).GetProperty("result").GetProperty("tools");
   foreach(string name in new[]{"get_preset","preview_preset","apply_preset","undo_preset","analyze_response","get_dsd_diagnostics","get_audio_diagnostics","measure_levels","list_targets","get_target","get_tuning_context","preview_target","reset_output","list_spatial_scenes","set_spatial","apply_spatial_scene"})
    Check(list.EnumerateArray().Any(t=>t.GetProperty("name").GetString()==name),"advertises_"+name);
   Check(list.EnumerateArray().Single(t=>t.GetProperty("name").GetString()=="preview_preset").GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean(),"preview_is_read_only");
   var unknown=await Send("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"stop"}}""");
   Check(unknown.TryGetProperty("error",out _)&&calls==0,"unadvertised_command_never_reaches_pipe");
   var overrideCmd=await Send("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_state","arguments":{"cmd":"set_dsd","mode":"dop128"}}}""");
   Check(overrideCmd.TryGetProperty("error",out _)&&calls==0,"arguments_cannot_override_command");
   var missing=await Send("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"apply_preset","arguments":{"preset":{}}}}""");
   Check(missing.TryGetProperty("error",out _)&&calls==0,"revision_required_before_apply");
   Check((await Send("""{"jsonrpc":"2.0","id":50,"method":"tools/call","params":{"name":"reset_output","arguments":{"section":"all"}}}""")).TryGetProperty("error",out _)&&calls==0,"reset_requires_revision_before_pipe_access");
   Check(list.EnumerateArray().Single(t=>t.GetProperty("name").GetString()=="preview_target").GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean(),"target_preview_is_read_only");
   Check(await protocol.Handle("""{"jsonrpc":"2.0","method":"tools/call","params":{"name":"set_preamp","arguments":{"db":-60}}}""")==null&&calls==0,"notifications_never_apply_tuning");
   var success=await Send("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"get_state","arguments":{}}}""");
   Check(!success.GetProperty("result").GetProperty("isError").GetBoolean(),"spaced_success_json_is_parsed_structurally");
   using(var sent=JsonDocument.Parse(forwarded!))Check(sent.RootElement.GetProperty("cmd").GetString()=="get_state","tool_name_is_forwarded");
   upstream="{\"ok\":false,\"error\":\"failed\"}";
   Check((await Send("""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"get_state"}}""")).GetProperty("result").GetProperty("isError").GetBoolean(),"upstream_failure_is_tool_error");
   upstream="not json";
   Check((await Send("""{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"get_state"}}""")).GetProperty("result").GetProperty("isError").GetBoolean(),"malformed_upstream_does_not_crash_server");
   Check((await Send("""{"jsonrpc":"2.0","id":9,"method":"ping"}""")).TryGetProperty("result",out _),"server_continues_after_bad_requests");
   JsonElement Snapshot(long frames,string stream="s",string revision="r",double power=1)=>JsonSerializer.SerializeToElement(new{
    ok=true,stream_id=stream,revision,sample_rate=48000,frames,
    input_left_energy=frames*.01*power,input_right_energy=frames*.04*power,
    output_left_energy=frames*.0025*power,output_right_energy=frames*.01*power,
    lifetime_input_peak=.4,lifetime_output_peak=.2,
    capture_underflow_bytes=0,capture_overflows=0,input_invalid_samples=0
   });
   var window=JsonSerializer.SerializeToElement(LevelMeasurement.Compare(Snapshot(48000),Snapshot(96000),1));
   Check(Math.Abs(window.GetProperty("input_left_rms_dbfs").GetDouble()+20)<1e-9&&Math.Abs(window.GetProperty("applied_gain_db").GetDouble()+6.020599913)<1e-6,"interval_rms_and_gain_use_counter_deltas");
   var silence=JsonSerializer.SerializeToElement(LevelMeasurement.Compare(Snapshot(48000,power:0),Snapshot(96000,power:0),1));
   Check(silence.GetProperty("input_rms_dbfs").ValueKind==JsonValueKind.Null&&!silence.GetProperty("has_input_signal").GetBoolean(),"silence_is_not_fabricated_loudness");
   bool rejected=false;try{LevelMeasurement.Compare(Snapshot(48000),Snapshot(96000,stream:"new"),1);}catch(InvalidOperationException){rejected=true;}
   Check(rejected,"meter_rejects_stream_restart_even_when_counters_have_caught_up");
   rejected=false;try{LevelMeasurement.Compare(Snapshot(48000),Snapshot(96000,revision:"changed"),1);}catch(InvalidOperationException){rejected=true;}
   Check(rejected,"meter_rejects_preset_or_device_changes");
   rejected=false;try{LevelMeasurement.Compare(Snapshot(48000),Snapshot(48000),1);}catch(InvalidOperationException){rejected=true;}
   Check(rejected,"meter_rejects_a_stopped_frame_counter");
   int meterRequests=0;
   var measurement=new McpProtocol(json=>{
    using var request=JsonDocument.Parse(json);
    if(request.RootElement.GetProperty("cmd").GetString()!="get_meter_snapshot")throw new Exception("Unexpected meter operation");
    return Task.FromResult(Snapshot(++meterRequests*48000).GetRawText());
   },_=>Task.CompletedTask);
   using(var response=JsonDocument.Parse((await measurement.Handle("""{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"measure_levels","arguments":{"seconds":1}}}"""))!))
    Check(!response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean()&&meterRequests==2,"measurement_uses_two_read_only_snapshots");
   using(var response=JsonDocument.Parse((await measurement.Handle("""{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"measure_levels","arguments":{"seconds":100}}}"""))!))
    Check(response.RootElement.TryGetProperty("error",out _)&&meterRequests==2,"measurement_duration_is_bounded_before_pipe_access");
   string pipeName="OpenDSP.TransportTest."+Guid.NewGuid().ToString("N");
   using var server=new System.IO.Pipes.NamedPipeServerStream(pipeName,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous);
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
   async Task Serve(){
    await server.WaitForConnectionAsync(timeout.Token);
    using var reader=new StreamReader(server,System.Text.Encoding.UTF8,true,1024,true);
    using var writer=new StreamWriter(server,new System.Text.UTF8Encoding(false),1024,true){AutoFlush=true};
    await reader.ReadLineAsync(timeout.Token);
    await writer.WriteLineAsync("{\"ok\":true,\"transport\":\"test\"}".AsMemory(),timeout.Token);
   }
   var serving=Serve();
   string roundTrip=await PipeTransport.RequestTo(pipeName,"{\"cmd\":\"get_state\"}");
   await serving;
   using(var response=JsonDocument.Parse(roundTrip))
    Check(response.RootElement.GetProperty("ok").GetBoolean(),"real_pipe_disposal_preserves_successful_reply");
   report["result"]="PASS";return 0;
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}
  finally{report["scope"]="Offline JSON-RPC dispatch with a fake pipe; no audio or device writes";report["cases"]=cases;File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
