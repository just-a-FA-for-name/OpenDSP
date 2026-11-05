using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace OpenEQ;

/// <summary>Authenticated loopback bridge. Phone access uses an explicitly configured adb reverse.</summary>
public sealed class MobileSyncServer:IDisposable {
 readonly TcpListener listener;readonly CancellationTokenSource stop=new();readonly Func<string,string> command;
 readonly byte[] tokenBytes;public string Token{get;}public int Port{get;}public long Requests;
 static readonly HashSet<string> Allowed=new(){"get_preset","get_preset_library","apply_preset","preview_preset","list_targets","get_target","preview_target","analyze_response","get_capabilities","get_tuning_context"};
 public MobileSyncServer(int port=19874,Func<string,string>? handler=null){
  Token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));tokenBytes=Encoding.ASCII.GetBytes("Bearer "+Token);command=handler??PipeCommand;
  listener=new TcpListener(IPAddress.Loopback,port);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;_ =Accept();
 }
 public string PairingJson()=>JsonSerializer.Serialize(new{format="org.opendsp.sync-pair",version=1,url=$"http://127.0.0.1:{Port}",token=Token},new JsonSerializerOptions{WriteIndented=true});
 async Task Accept(){while(!stop.IsCancellationRequested)try{var client=await listener.AcceptTcpClientAsync(stop.Token);_ =Task.Run(()=>Serve(client));}catch(OperationCanceledException){break;}catch(SocketException)when(stop.IsCancellationRequested){break;}}
 async Task Serve(TcpClient client){
  using(client)try{
   client.ReceiveTimeout=5000;client.SendTimeout=5000;using var stream=client.GetStream();
   using var requestTimeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);requestTimeout.CancelAfter(10000);var ct=requestTimeout.Token;
   var header=new List<byte>();byte[] one=new byte[1];
   while(header.Count<16384){if(await stream.ReadAsync(one,ct)==0)return;header.Add(one[0]);int n=header.Count;if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10)break;}
   if(header.Count>=16384){await Reply(stream,413,"{\"ok\":false,\"error\":\"Header too large\"}",ct);return;}
   var lines=Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");var start=lines[0].Split(' ');
   var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var line in lines.Skip(1)){int at=line.IndexOf(':');if(at>0){string key=line[..at].Trim();if(!headers.TryAdd(key,line[(at+1)..].Trim())){await Reply(stream,400,"{\"ok\":false,\"error\":\"Duplicate header\"}",ct);return;}}}
   byte[] supplied=Encoding.ASCII.GetBytes(headers.GetValueOrDefault("Authorization",""));
   if(!CryptographicOperations.FixedTimeEquals(tokenBytes,supplied)){await Reply(stream,401,"{\"ok\":false,\"error\":\"Pairing required\"}",ct);return;}
   if(start.Length!=3||headers.ContainsKey("Transfer-Encoding")||headers.ContainsKey("Origin")){await Reply(stream,400,"{\"ok\":false,\"error\":\"Unsupported request\"}",ct);return;}
   string request;
   if(start[0]=="GET"&&start[1]=="/v1/state")request="{\"cmd\":\"get_preset\"}";
   else if(start[0]=="POST"&&start[1]=="/v1/command"){
    if(!int.TryParse(headers.GetValueOrDefault("Content-Length",""),out int length)||length<1||length>1048576){await Reply(stream,413,"{\"ok\":false,\"error\":\"Invalid body size\"}",ct);return;}
    byte[] body=new byte[length];await stream.ReadExactlyAsync(body,ct);request=new UTF8Encoding(false,true).GetString(body);
    using var json=JsonDocument.Parse(request);string cmd=json.RootElement.GetProperty("cmd").GetString()??"";
    if(!Allowed.Contains(cmd)){await Reply(stream,403,"{\"ok\":false,\"error\":\"Command not permitted by sync bridge\"}",ct);return;}
   }else{await Reply(stream,404,"{\"ok\":false,\"error\":\"Unknown path\"}",ct);return;}
   string response;
   try{response=command(request);}catch(Exception e){response=JsonSerializer.Serialize(new{ok=false,error=e.Message});}
   Interlocked.Increment(ref Requests);await Reply(stream,200,response,ct);
  }catch(Exception){}
 }
 static async Task Reply(NetworkStream stream,int status,string json,CancellationToken ct){
  byte[] body=Encoding.UTF8.GetBytes(json),header=Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
  await stream.WriteAsync(header,ct);await stream.WriteAsync(body,ct);
 }
 static string PipeCommand(string request){
  using var pipe=new NamedPipeClientStream(".","OpenDSP",PipeDirection.InOut,PipeOptions.Asynchronous);using var timeout=new CancellationTokenSource(5000);
  pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(pipe,Encoding.UTF8,false,4096,true);
  writer.WriteLineAsync(request.AsMemory(),timeout.Token).GetAwaiter().GetResult();return reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult()??throw new IOException("Desktop disconnected");
 }
 public void Dispose(){stop.Cancel();listener.Stop();}
}
