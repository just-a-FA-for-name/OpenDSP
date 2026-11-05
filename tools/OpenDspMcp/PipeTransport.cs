using System.IO.Pipes;
using System.Text;

internal static class PipeTransport {
 public static Task<string> Request(string json)=>RequestTo("OpenDSP",json);
 internal static async Task<string> RequestTo(string name,string json){
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
  using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
  await pipe.ConnectAsync(4000,timeout.Token);
  // Reader and writer share one pipe. Neither may close it while the other
  // still needs to flush/dispose, or a successful reply becomes a false error.
  using var writer=new StreamWriter(pipe,new UTF8Encoding(false),1024,true){AutoFlush=true};
  using var reader=new StreamReader(pipe,Encoding.UTF8,true,1024,true);
  await writer.WriteLineAsync(json.AsMemory(),timeout.Token);
  return await reader.ReadLineAsync(timeout.Token)??throw new IOException("OpenDSP did not respond");
 }
}
