using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
namespace OpenEQ;

internal interface IOutputSession:IDisposable {
 WaveFormat OutputWaveFormat{get;}
 void Init(IWaveProvider source);void Play();void Stop();long GetPosition();
 event EventHandler<StoppedEventArgs>? PlaybackStopped;
}
internal sealed class StandardOutputSession: IOutputSession {
 readonly WasapiOut output;
 public StandardOutputSession(MMDevice device,AudioClientShareMode mode,bool events,int latency)=>output=new WasapiOut(device,mode,events,latency);
 public WaveFormat OutputWaveFormat=>output.OutputWaveFormat;
 public event EventHandler<StoppedEventArgs>? PlaybackStopped{add=>output.PlaybackStopped+=value;remove=>output.PlaybackStopped-=value;}
 public void Init(IWaveProvider source)=>output.Init(source);public void Play()=>output.Play();public void Stop()=>output.Stop();public long GetPosition()=>output.GetPosition();public void Dispose()=>output.Dispose();
}

/// <summary>Exact-format DoP transport: raw byte copies, MMCSS and a well-filled queue.</summary>
internal sealed class StrictExclusiveOut: IOutputSession {
 readonly MMDevice device;readonly bool events;readonly int latencyMs;readonly SynchronizationContext? context;
 AudioClient? client;AudioRenderClient? render;IWaveProvider? source;byte[] bytes=Array.Empty<byte>();
 readonly ManualResetEvent quit=new(false),started=new(false);EventWaitHandle? available;Thread? thread;
 Exception? failure;bool disposed;volatile bool playing;int capacity;long underruns,submitted;bool empty,clientStarted;
 public bool MmcssRegistered{get;private set;}public long Underruns=>Interlocked.Read(ref underruns);public long SubmittedBytes=>Interlocked.Read(ref submitted);
 public double BufferMilliseconds=>1000d*capacity/OutputWaveFormat.SampleRate;
 public double MaxRunningFillMs{get;private set;}public double MaxPumpGapMs{get;private set;}public long LateEvents{get;private set;}
 public WaveFormat OutputWaveFormat{get;private set;}=new WaveFormat();
 public event EventHandler<StoppedEventArgs>? PlaybackStopped;
 public StrictExclusiveOut(MMDevice device,bool events,int latencyMs=160){this.device=device;this.events=events;this.latencyMs=latencyMs;context=SynchronizationContext.Current;}
 public void Init(IWaveProvider provider){
  ObjectDisposedException.ThrowIf(disposed,this);if(client!=null)throw new InvalidOperationException("Output already initialized");
  source=provider;OutputWaveFormat=provider.WaveFormat;client=device.AudioClient;
  if(!client.IsFormatSupported(AudioClientShareMode.Exclusive,OutputWaveFormat))throw new NotSupportedException("Exact DoP carrier format is unsupported; conversion is forbidden");
  long duration=latencyMs*10000L;var flags=events?AudioClientStreamFlags.EventCallback:AudioClientStreamFlags.None;
  try{client.Initialize(AudioClientShareMode.Exclusive,flags,duration,events?duration:0,OutputWaveFormat,Guid.Empty);}
  catch(COMException ex)when(ex.HResult==unchecked((int)0x88890019)){
   long aligned=(long)Math.Round(10000000d*client.BufferSize/OutputWaveFormat.SampleRate);client.Dispose();client=device.AudioClient;
   client.Initialize(AudioClientShareMode.Exclusive,flags,aligned,events?aligned:0,OutputWaveFormat,Guid.Empty);
  }
  capacity=client.BufferSize;bytes=new byte[checked(capacity*OutputWaveFormat.BlockAlign)];render=client.AudioRenderClient;
  if(events){available=new EventWaitHandle(false,EventResetMode.AutoReset);client.SetEventHandle(available.SafeWaitHandle.DangerousGetHandle());}
 }
 public void Play(){
  if(client==null)throw new InvalidOperationException("Output not initialized");if(playing)return;failure=null;quit.Reset();started.Reset();
  thread=new Thread(Pump){IsBackground=true,Name="OpenDSP.DoP.Exclusive",Priority=ThreadPriority.AboveNormal};thread.SetApartmentState(ApartmentState.MTA);playing=true;thread.Start();
  if(!started.WaitOne(5000)){Stop();throw new TimeoutException("Exclusive output did not start");}if(failure!=null)throw new IOException("Exclusive output startup failed",failure);
 }
 void CountEmpty(int padding){if(padding==0){if(!empty)Interlocked.Increment(ref underruns);empty=true;}else empty=false;}
 bool Fill(int frames){
  long fillStart=System.Diagnostics.Stopwatch.GetTimestamp();
  int wanted=frames*OutputWaveFormat.BlockAlign,offset=0;
  while(offset<wanted){int read=source!.Read(bytes,offset,wanted-offset);if(read<=0)throw new IOException("DoP source ended; zero padding would corrupt the carrier");if(read>wanted-offset||read%OutputWaveFormat.BlockAlign!=0)throw new IOException("Invalid DoP frame length");offset+=read;}
  if(quit.WaitOne(0))return false;
  if(clientStarted)MaxRunningFillMs=Math.Max(MaxRunningFillMs,System.Diagnostics.Stopwatch.GetElapsedTime(fillStart).TotalMilliseconds);
  // Synthesis can exhaust the queue even when it was nonempty before Read.
  if(clientStarted&&!events)CountEmpty(client!.CurrentPadding);
  IntPtr target=render!.GetBuffer(frames);try{Marshal.Copy(bytes,0,target,wanted);}finally{render.ReleaseBuffer(frames,AudioClientBufferFlags.None);}
  Interlocked.Add(ref submitted,wanted);empty=false;return true;
 }
 void Pump(){IntPtr task=IntPtr.Zero;try{
   uint index=0;task=AvSetMmThreadCharacteristics("Pro Audio",ref index);MmcssRegistered=task!=IntPtr.Zero;if(MmcssRegistered)AvSetMmThreadPriority(task,1);
   clientStarted=false;if(!Fill(capacity))return;client!.Start();clientStarted=true;started.Set();
   long last=System.Diagnostics.Stopwatch.GetTimestamp();
   void ObserveGap(){long now=System.Diagnostics.Stopwatch.GetTimestamp();double gap=System.Diagnostics.Stopwatch.GetElapsedTime(last,now).TotalMilliseconds;MaxPumpGapMs=Math.Max(MaxPumpGapMs,gap);if(events&&gap>BufferMilliseconds*1.5)LateEvents++;last=now;}
   if(events){var waits=new WaitHandle[]{quit,available!};while(!quit.WaitOne(0)){
    int signal=WaitHandle.WaitAny(waits,Math.Max(500,latencyMs*4));if(signal==0)break;if(signal==WaitHandle.WaitTimeout)throw new TimeoutException("DoP output callback stopped");ObserveGap();if(!Fill(capacity))break;
   }}else{
    // Poll much faster than a half-buffer sleep. Keep the queue close to full
    // so synthesis plus scheduler delays cannot consume the entire safety margin.
    int minimum=Math.Max(1,OutputWaveFormat.SampleRate/500);
    while(!quit.WaitOne(1)){ObserveGap();int padding=client.CurrentPadding;CountEmpty(padding);int free=capacity-padding;if(free>=minimum&&!Fill(free))break;}
   }
  }catch(Exception ex){failure=ex;}finally{
   try{client?.Stop();client?.Reset();}catch{}playing=false;started.Set();if(task!=IntPtr.Zero)AvRevertMmThreadCharacteristics(task);
   var handler=PlaybackStopped;if(handler!=null){var args=new StoppedEventArgs(failure);if(context!=null)context.Post(_=>handler(this,args),null);else handler(this,args);}
  }
 }
 public long GetPosition(){if(client==null||!playing)return 0;var clock=client.AudioClockClient;clock.GetPosition(out ulong position,out _);return (long)(position*(ulong)OutputWaveFormat.AverageBytesPerSecond/clock.Frequency);}
 public void Stop(){quit.Set();if(thread!=null&&thread!=Thread.CurrentThread){if(!thread.Join(5000)){try{client?.Stop();}catch{}throw new TimeoutException("DoP render thread did not stop");}thread=null;}playing=false;}
 public void Dispose(){if(disposed)return;Stop();disposed=true;client?.Dispose();client=null;render=null;available?.Dispose();quit.Dispose();started.Dispose();}
 [DllImport("avrt.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr AvSetMmThreadCharacteristics(string name,ref uint taskIndex);
 [DllImport("avrt.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);
 [DllImport("avrt.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool AvSetMmThreadPriority(IntPtr handle,int priority);
}
