using System.IO.Pipes;
namespace OpenEQ;
/// <summary>
/// Named-pipe JSON control interface (\.\pipe\OpenDSP). One JSON object per line
/// in, one JSON object per line out. Blocking accept loop with a worker per client.
/// </summary>
public sealed class ControlServer:IDisposable {
 readonly Func<string,string> handler;readonly CancellationTokenSource cts=new();readonly Thread accept;
 static readonly string LogPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","control.log");
 static void Log(string msg){try{File.AppendAllText(LogPath,$"{DateTimeOffset.Now:O} {msg}{Environment.NewLine}");}catch{}}
 public ControlServer(Func<string,string> handler){this.handler=handler;accept=new Thread(Loop){IsBackground=true,Name="OpenDSP.Control"};}
 public void Start(){Log("start");accept.Start();Log("thread-started");}
 void Loop(){Log("loop-enter");while(!cts.IsCancellationRequested){NamedPipeServerStream? pending=null;try{pending=new NamedPipeServerStream("OpenDSP",PipeDirection.InOut,NamedPipeServerStream.MaxAllowedServerInstances,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);Log("listening");pending.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();Log("client-connected");var connected=pending;pending=null;ThreadPool.QueueUserWorkItem(_=>Serve(connected));}catch(OperationCanceledException){pending?.Dispose();break;}catch(Exception ex){Log("loop-error "+ex.GetType().Name+": "+ex.Message);try{pending?.Dispose();}catch{}Thread.Sleep(200);}}Log("loop-exit");}
 void Serve(NamedPipeServerStream pipe){Log("serve-enter");try{using(pipe){using var reader=new StreamReader(pipe);using var writer=new StreamWriter(pipe){AutoFlush=true};string? line;while((line=reader.ReadLine())!=null&&!cts.IsCancellationRequested){string response;try{response=handler(line);}catch(Exception ex){response=System.Text.Json.JsonSerializer.Serialize(new{ok=false,error=ex.Message});}writer.WriteLine(response);}}Log("serve-done");}catch(Exception ex){Log("serve-error "+ex.GetType().Name+": "+ex.Message);}}
 public void Dispose(){Log("dispose");cts.Cancel();try{accept.Join(1000);}catch{}if(!accept.IsAlive)cts.Dispose();}
}
