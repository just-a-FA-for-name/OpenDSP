using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
namespace OpenEQ;
/// <summary>Precompute DoP away from the device submission thread. Raw frames
/// retain their order and markers; an empty queue never inserts PCM zeros.</summary>
internal sealed class DopSynthesisQueue:IWaveProvider,IDisposable {
 readonly IWaveProvider source;readonly Func<double> captureSeconds;readonly byte[] ring,block;
 readonly object gate=new();readonly ManualResetEvent quit=new(false);readonly ManualResetEvent ready=new(false);
 Thread? worker;Exception? failure;int readPosition,writePosition,queued;bool disposed;long waits;
 public WaveFormat WaveFormat=>source.WaveFormat;
 public double CapacityMilliseconds=>1000d*ring.Length/WaveFormat.AverageBytesPerSecond;
 public double QueuedMilliseconds{get{lock(gate)return 1000d*queued/WaveFormat.AverageBytesPerSecond;}}
 public long Waits=>Interlocked.Read(ref waits);public double MaxSynthesisMs{get;private set;}
 public DopSynthesisQueue(IWaveProvider source,Func<double> captureSeconds,double outputBufferMs){
  this.source=source;this.captureSeconds=captureSeconds;int frames=Math.Max(1,source.WaveFormat.SampleRate/100);
  block=new byte[frames*WaveFormat.BlockAlign];int blocks=(int)Math.Ceiling((outputBufferMs*2+40)/10);ring=new byte[checked(block.Length*Math.Max(4,blocks))];
 }
 public void Start(){
  ObjectDisposedException.ThrowIf(disposed,this);if(worker!=null)throw new InvalidOperationException("Synthesis already started");
  worker=new Thread(Produce){IsBackground=true,Name="OpenDSP.DoP.Synthesis",Priority=ThreadPriority.AboveNormal};worker.SetApartmentState(ApartmentState.MTA);worker.Start();
  if(!ready.WaitOne(5000))throw new TimeoutException("DSD synthesis prebuffer timed out");if(failure!=null)throw new IOException("DSD synthesis failed",failure);
 }
 void Produce(){IntPtr task=IntPtr.Zero;try{
  uint index=0;task=AvSetMmThreadCharacteristics("Pro Audio",ref index);
  while(!quit.WaitOne(0)){
   lock(gate){while(ring.Length-queued<block.Length&&!quit.WaitOne(0))Monitor.Wait(gate,20);}
   if(quit.WaitOne(0))break;
   // A worker may run ahead of both hardware and loopback capture. Wait for
   // real captured samples instead of manufacturing a zero-filled DoP burst.
   if(captureSeconds()<.02+1.002*block.Length/WaveFormat.AverageBytesPerSecond){if(quit.WaitOne(1))break;continue;}
   long start=Stopwatch.GetTimestamp();int offset=0;while(offset<block.Length){int n=source.Read(block,offset,block.Length-offset);if(n<=0||n%WaveFormat.BlockAlign!=0)throw new IOException("Invalid DSD synthesis frame count");offset+=n;}
   MaxSynthesisMs=Math.Max(MaxSynthesisMs,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
   lock(gate){int first=Math.Min(block.Length,ring.Length-writePosition);Buffer.BlockCopy(block,0,ring,writePosition,first);Buffer.BlockCopy(block,first,ring,0,block.Length-first);writePosition=(writePosition+block.Length)%ring.Length;queued+=block.Length;if(queued==ring.Length)ready.Set();Monitor.PulseAll(gate);}
  }
 }catch(Exception ex){lock(gate){failure=ex;Monitor.PulseAll(gate);}ready.Set();}finally{if(task!=IntPtr.Zero)AvRevertMmThreadCharacteristics(task);}}
 public int Read(byte[] bytes,int offset,int count){
  if(offset<0||count<0||offset>bytes.Length-count||count%WaveFormat.BlockAlign!=0)throw new ArgumentOutOfRangeException();
  int copied=0;long start=Stopwatch.GetTimestamp();bool counted=false;
  while(copied<count){lock(gate){
   while(queued==0){if(failure!=null)throw new IOException("DSD worker stopped",failure);if(quit.WaitOne(0))throw new OperationCanceledException("DSD synthesis stopped");if(Stopwatch.GetElapsedTime(start).TotalMilliseconds>500)throw new IOException("DSD queue exhausted; refusing corrupt zero padding");if(!counted){Interlocked.Increment(ref waits);counted=true;}Monitor.Wait(gate,10);}
   int take=Math.Min(count-copied,queued),first=Math.Min(take,ring.Length-readPosition);Buffer.BlockCopy(ring,readPosition,bytes,offset+copied,first);Buffer.BlockCopy(ring,0,bytes,offset+copied+first,take-first);readPosition=(readPosition+take)%ring.Length;queued-=take;copied+=take;Monitor.PulseAll(gate);
  }}return copied;
 }
 public void Dispose(){if(disposed)return;disposed=true;quit.Set();lock(gate)Monitor.PulseAll(gate);if(worker!=null&&!worker.Join(5000))throw new TimeoutException("DSD worker did not stop");quit.Dispose();ready.Dispose();}
 [DllImport("avrt.dll",CharSet=CharSet.Unicode)]static extern IntPtr AvSetMmThreadCharacteristics(string name,ref uint taskIndex);
 [DllImport("avrt.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
