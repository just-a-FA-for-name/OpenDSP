using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace OpenEQ;

internal interface IDopVolumeBackend:IDisposable {
 float Db{get;set;}
 bool SupportsUnity{get;}
}
internal sealed class DopEndpointVolume:IDopVolumeBackend {
 readonly MMDevice device;
 internal AudioEndpointVolume Volume=>device.AudioEndpointVolume;
 public DopEndpointVolume(string id){using var en=new MMDeviceEnumerator();device=en.GetDevice(id);}
 public float Db{get=>Volume.MasterVolumeLevel;set=>Volume.MasterVolumeLevel=value;}
 public bool SupportsUnity=>Volume.VolumeRange.MinDecibels<=0&&Volume.VolumeRange.MaxDecibels>=0;
 public void Dispose()=>device.Dispose();
}
internal sealed record DopVolumeState(string DeviceId,int OwnerPid,long OwnerStarted,string Token,float RestoreDb,bool Fake=false,bool UncertainWrite=false);

/// <summary>Moves endpoint attenuation before DoP packing; never scales DoP bytes.</summary>
internal sealed class DopVolumeLease:IDisposable {
 readonly object sync=new();readonly IDopVolumeBackend backend;readonly string path;
 readonly Process guard;readonly Guid context=Guid.NewGuid();DopVolumeState state;
 bool active,disposed;double gain;public double Gain=>Volatile.Read(ref gain);
 public float AttenuationDb{get{lock(sync)return state.RestoreDb;}}
 public float CarrierDb{get{lock(sync)return backend.Db;}}
 static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","dop-volume");
 internal static void Save(string path,DopVolumeState state){Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,JsonSerializer.Serialize(state));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 internal static DopVolumeState Load(string path){using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<DopVolumeState>(file)??throw new IOException("Invalid DoP recovery record");}
 internal static bool OwnerAlive(DopVolumeState state){if(state.OwnerPid<=0)return false;try{using var owner=Process.GetProcessById(state.OwnerPid);return !owner.HasExited&&owner.StartTime.ToUniversalTime().Ticks==state.OwnerStarted;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}}
 internal static bool Restore(IDopVolumeBackend backend,DopVolumeState state){
  // Preserve a later, explicit external change. Restore only the unity value we own.
  if(!state.UncertainWrite&&Math.Abs(backend.Db)>.05)return false;backend.Db=state.RestoreDb;
  if(Math.Abs(backend.Db-state.RestoreDb)>.1)throw new IOException("DoP output volume restoration readback failed");return true;
 }
 public static void Recover(string id){string path=Path.Combine(DirectoryPath,NativeApo.EndpointGuid(id)+".json");
  if(!File.Exists(path))return;var state=Load(path);
  if(OwnerAlive(state))throw new InvalidOperationException("DSD endpoint volume is owned by another live OpenDSP process");
  using var backend=new DopEndpointVolume(id);Restore(backend,state);File.Delete(path);
 }
 public DopVolumeLease(string id):this(new DopEndpointVolume(id),Path.Combine(DirectoryPath,NativeApo.EndpointGuid(id)+".json"),id,false){}
 internal DopVolumeLease(IDopVolumeBackend backend,string path,string id,bool fake){
  this.backend=backend;this.path=Path.GetFullPath(path);guard=null!;state=null!;bool ownsRecord=false;
  try{
   if(File.Exists(this.path))throw new IOException("A previous DoP volume transaction still needs recovery");
   if(!backend.SupportsUnity)throw new InvalidOperationException("Device cannot provide a unity-gain DoP carrier");
   float original=backend.Db;if(!float.IsFinite(original)||original>0)throw new InvalidOperationException("DoP output requires a finite endpoint attenuation at or below 0 dB");
   using var self=Process.GetCurrentProcess();state=new(id,self.Id,self.StartTime.ToUniversalTime().Ticks,Guid.NewGuid().ToString("N"),original,fake);gain=Math.Pow(10,original/20d);
   Save(this.path,state);ownsRecord=true;
   var start=SelfProcess.StartInfo("--dop-volume-guard",this.path,state.Token);
   guard=Process.Start(start)??throw new IOException("Could not launch DoP volume recovery guard");
   string ready=this.path+"."+state.Token+".ready";var wait=Stopwatch.StartNew();
   while(!File.Exists(ready)&&!guard.HasExited&&wait.ElapsedMilliseconds<5000)Thread.Sleep(20);
   if(!File.Exists(ready))throw new IOException("DoP recovery guard did not become ready; endpoint volume was not changed");
   File.Delete(ready);
   if(backend is DopEndpointVolume endpoint){endpoint.Volume.NotificationGuid=context;endpoint.Volume.OnVolumeNotification+=VolumeChanged;}
  }catch{if(ownsRecord&&File.Exists(this.path))File.Delete(this.path);guard?.Dispose();backend.Dispose();throw;}
 }
 public void Activate(){lock(sync){ObjectDisposedException.ThrowIf(disposed,this);
  if(Math.Abs(backend.Db-state.RestoreDb)>.1)throw new IOException("Endpoint volume changed during DoP initialization; no unity change was applied");
  // Persist before touching the driver. A crash between a partial write and its
  // readback must still restore the original attenuation, even if it is not 0 dB.
  state=state with{UncertainWrite=true};Save(path,state);active=true;
  try{backend.Db=0;if(Math.Abs(backend.Db)>.05)throw new IOException("Driver did not preserve unity gain for DoP");state=state with{UncertainWrite=false};Save(path,state);}
  catch{try{backend.Db=state.RestoreDb;state=state with{UncertainWrite=false};Save(path,state);active=false;}catch{Save(path,state with{OwnerPid=0,OwnerStarted=0});}throw;}
 }}
 void VolumeChanged(AudioVolumeNotificationData notification){
  if(notification.EventContext==context)return;
  ThreadPool.QueueUserWorkItem(_=>{lock(sync){if(!active||disposed)return;try{
   float desired=backend.Db;if(Math.Abs(desired)<.05)return;
   // Apply attenuation to PCM first, persist the desired restoration, then put
   // the encoded carrier back at unity. No preset is changed by this operation.
   state=state with{RestoreDb=Math.Min(desired,0),UncertainWrite=true};Save(path,state);Volatile.Write(ref gain,Math.Pow(10,state.RestoreDb/20d));backend.Db=0;
   if(Math.Abs(backend.Db)>.05)throw new IOException("Driver changed DoP unity gain");state=state with{UncertainWrite=false};Save(path,state);
  }catch(Exception ex){Faulted?.Invoke("DoP volume routing failed: "+ex.Message);}}});
 }
 public event Action<string>? Faulted;
 public void Dispose(){lock(sync){if(disposed)return;disposed=true;
  if(backend is DopEndpointVolume endpoint)endpoint.Volume.OnVolumeNotification-=VolumeChanged;
  try{if(active)Restore(backend,state);if(File.Exists(path))File.Delete(path);}
  catch{Save(path,state with{OwnerPid=0,OwnerStarted=0});throw;}
  finally{active=false;backend.Dispose();guard.Dispose();}
 }}
 internal static int Guard(string path,string token){
  path=Path.GetFullPath(path);DopVolumeState? state=null;
  try{
   if(!File.Exists(path))return 0;state=Load(path);if(state.Token!=token)return 0;
   // Opening the backend before acknowledging readiness also validates recovery access.
   using IDopVolumeBackend backend=state.Fake?new DopFileVolume(state.DeviceId):new DopEndpointVolume(state.DeviceId);
   File.WriteAllText(path+"."+token+".ready","ready");
   while(File.Exists(path)){
    var latest=Load(path);if(latest.Token!=token)return 0;state=latest;
    if(!OwnerAlive(state)){Restore(backend,state);File.Delete(path);return 0;}
    Thread.Sleep(100);
   }return 0;
  }catch(FileNotFoundException){return 0;}catch(Exception ex){File.WriteAllText(path+".guard-error.txt",ex.ToString());return 1;}
 }
}

internal static class SelfProcess {
 internal static ProcessStartInfo StartInfo(params string[] args){var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
  if(string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath),"dotnet",StringComparison.OrdinalIgnoreCase))start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"OpenDSP.dll"));
  foreach(var arg in args)start.ArgumentList.Add(arg);return start;
 }
}
// Disk-backed volume is used only by isolated crash-recovery tests, never by audio output.
internal sealed class DopFileVolume(string path):IDopVolumeBackend {
 public float Db{get=>float.Parse(File.ReadAllText(path),System.Globalization.CultureInfo.InvariantCulture);set=>File.WriteAllText(path,value.ToString("R",System.Globalization.CultureInfo.InvariantCulture));}
 public bool SupportsUnity=>true;public void Dispose(){}
}
internal sealed class DopGainProvider(ISampleProvider source,Func<double> gain):ISampleProvider {
 double current=gain();public WaveFormat WaveFormat=>source.WaveFormat;
 public int Read(float[] buffer,int offset,int count){int n=source.Read(buffer,offset,count);double target=gain();
  double step=(target-current)/Math.Max(1,n/2);for(int i=offset;i+1<offset+n;i+=2){current+=step;buffer[i]*=(float)current;buffer[i+1]*=(float)current;}current=target;return n;
 }
}
