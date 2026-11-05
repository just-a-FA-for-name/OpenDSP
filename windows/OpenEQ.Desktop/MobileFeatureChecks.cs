using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
namespace OpenEQ;
internal static class MobileFeatureChecks {
 public static int Fixture(string file){
  var preset=Preset.Flat();preset.Preamp=-3;int revision=1;object gate=new();
  using var server=new MobileSyncServer(19875,request=>{lock(gate){
   using var document=JsonDocument.Parse(request);var r=document.RootElement;
   if(r.GetProperty("cmd").GetString()=="apply_preset"){
    if(r.GetProperty("expected_revision").GetString()!=revision.ToString())return "{\"ok\":false,\"error\":\"stale revision\"}";
    preset=Preset.Parse(r.GetProperty("preset").GetRawText());revision++;
   }
   return JsonSerializer.Serialize(new{ok=true,preset,revision=revision.ToString(),sample_rate=384000});
  }});
  File.WriteAllText(file,server.PairingJson());Thread.Sleep(TimeSpan.FromMinutes(20));return 0;
 }
 public static int Run(string folder){
  Directory.CreateDirectory(folder);var original=Preset.Flat();original.Preamp=-3;var current=original.Copy();int revision=1;
  using var server=new MobileSyncServer(0,request=>{
   using var document=JsonDocument.Parse(request);var r=document.RootElement;string cmd=r.GetProperty("cmd").GetString()!;
   if(cmd=="get_preset")return JsonSerializer.Serialize(new{ok=true,preset=current,revision=revision.ToString(),sample_rate=384000});
   if(cmd=="apply_preset"){
    if(r.GetProperty("expected_revision").GetString()!=revision.ToString())return "{\"ok\":false,\"error\":\"stale revision\"}";
    current=Preset.Parse(r.GetProperty("preset").GetRawText());revision++;return JsonSerializer.Serialize(new{ok=true,preset=current,revision=revision.ToString()});
   }return "{\"ok\":false,\"error\":\"unsupported test command\"}";
  });
  using var http=new HttpClient{BaseAddress=new Uri($"http://127.0.0.1:{server.Port}"),Timeout=TimeSpan.FromSeconds(5)};
  var denied=http.GetAsync("/v1/state").GetAwaiter().GetResult();if(denied.StatusCode!=HttpStatusCode.Unauthorized)throw new Exception("Unauthenticated request accepted");
  http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",server.Token);
  string Read()=>http.GetStringAsync("/v1/state").GetAwaiter().GetResult();
  string Post(object value){using var content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json");return http.PostAsync("/v1/command",content).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();}
  using(var state=JsonDocument.Parse(Read()))if(!state.RootElement.GetProperty("ok").GetBoolean())throw new Exception("State unavailable");
  var candidate=original.Copy();candidate.Preamp=-4;
  using(var applied=JsonDocument.Parse(Post(new{cmd="apply_preset",expected_revision="1",preset=candidate,auto_headroom=false})))if(!applied.RootElement.GetProperty("ok").GetBoolean())throw new Exception("Apply failed");
  using(var stale=JsonDocument.Parse(Post(new{cmd="apply_preset",expected_revision="1",preset=original,auto_headroom=false})))if(stale.RootElement.GetProperty("ok").GetBoolean()||current.Preamp!=-4)throw new Exception("Stale write accepted");
  using(var blocked=http.PostAsync("/v1/command",new StringContent("{\"cmd\":\"start\"}",Encoding.UTF8,"application/json")).GetAwaiter().GetResult())if(blocked.StatusCode!=HttpStatusCode.Forbidden)throw new Exception("Playback control exposed");
  File.WriteAllText(Path.Combine(folder,"bridge-checks.json"),JsonSerializer.Serialize(new{result="PASS",unauthenticated_rejected=true,stale_write_rejected=true,playback_control_rejected=true,endpoint="loopback only"}));
  var full=Preset.Parse(File.ReadAllText(Path.Combine(folder,"desktop-complete.json")));
  File.WriteAllText(Path.Combine(folder,"desktop-response.json"),JsonSerializer.Serialize(new{points=LinearResponse.Analyze(full,384000)}));
  var target=TargetCurves.Find("preference-7");var options=new TargetFitOptions(Bands:12,MaxHz:20000);var fit=TargetFitting.Fit(target,null,384000,options);
  File.WriteAllText(Path.Combine(folder,"desktop-target-fit.json"),JsonSerializer.Serialize(new{target_id=target.Id,bands=fit.Bands,before=fit.BeforeRmsDb,after=fit.AfterRmsDb,maximum=fit.MaximumErrorDb,response=fit.Response}));
  return 0;
 }
}
