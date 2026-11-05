using System.IO.Pipes;
using System.Text.Json;
namespace OpenEQ;
internal static class AudioSoakMonitor {
 public static int Run(string path,int seconds){
  path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  var samples=new List<object>();var watch=System.Diagnostics.Stopwatch.StartNew();
  do{
   try{using var pipe=new NamedPipeClientStream(".","OpenDSP",PipeDirection.InOut,PipeOptions.Asynchronous);pipe.Connect(2000);
    using var writer=new StreamWriter(pipe,new System.Text.UTF8Encoding(false),1024,true){AutoFlush=true};using var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,true,1024,true);writer.WriteLine("{\"cmd\":\"get_state\"}");
    using var cancel=new CancellationTokenSource(3000);string? json=reader.ReadLineAsync(cancel.Token).AsTask().GetAwaiter().GetResult();
    using var doc=JsonDocument.Parse(json??throw new IOException("Empty control response"));
    samples.Add(new{second=watch.Elapsed.TotalSeconds,state=doc.RootElement.Clone()});
   }catch(Exception ex){samples.Add(new{second=watch.Elapsed.TotalSeconds,error=ex.Message});}
   string temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new{status=watch.Elapsed.TotalSeconds>=seconds?"COMPLETE":"RUNNING",elapsed_seconds=watch.Elapsed.TotalSeconds,scope="Read-only control/FIFO monitoring; acoustic continuity requires listening",samples},new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);
   if(watch.Elapsed.TotalSeconds>=seconds)return 0;Thread.Sleep(5000);
  }while(true);
 }
}
