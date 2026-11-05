using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OpenEQ;
public sealed record DeviceOption(string Id,string Name,string Transport="系统"){public override string ToString()=>Name;}
public sealed class AudioEngine:IDisposable {
 MMDevice? input,output;WasapiCapture? capture;IOutputSession? renderer;WasapiOut? keeper;DefaultRoute? route;
 BufferedWaveProvider? buffer;DspProvider? dsp;TimedRenderProvider? renderTiming;volatile bool running;int generation;public string? LastError;
 ClockedResampler? clock;public double ClockPpm=>clock?.CorrectionPpm??0;
 readonly string instanceId=Guid.NewGuid().ToString("N");
 public string StreamIdentity=>instanceId+":"+Volatile.Read(ref generation);
 public bool StrictPcmProbe{get;set;}public bool StandardDsdTransport{get;set;}
 DopIntegrityProvider? dopIntegrity;public long DopVerifiedFrames=>dopIntegrity?.Verified??0;public long DopInvalidFrames=>dopIntegrity?.Invalid??0;
 DsdProvider? dsdEncoder;
 public object DsdDiagnostics=>new{active=dsdEncoder!=null,modulator_headroom_db=dsdEncoder!=null?(double?)DsdProvider.HeadroomDb:null,limited_bit_samples=dsdEncoder?.LimitedSamples??0};
 DopSynthesisQueue? synthesis;public double? DsdQueuedMs=>synthesis?.QueuedMilliseconds;public long? DsdQueueWaits=>synthesis?.Waits;public double? DsdSynthesisMaxMs=>synthesis?.MaxSynthesisMs;
 VolumeRoute? volume;DopVolumeLease? dopVolume;
 public float InputVolumePercent=>volume?.InputPercent??0;public string VolumeMode=>dopVolume!=null?$"DoP 载体 0 dB，封包前衰减 {dopVolume.AttenuationDb:F1} dB"+(dsdEncoder!=null?"，DSD 调制余量 6.02 dB":""):volume?.Mode??"保留手动路由的音量";
 WindowsListen? listen;public int ListeningMicrophones=>listen?.RedirectedCount??0;
 public long CapturedBytes,OverflowEvents,StarvedReads,UnderflowBytes;public double BufferedMilliseconds=>buffer?.BufferedDuration.TotalMilliseconds??0;
 public double MaxRenderReadMs=>renderTiming?.MaxReadMs??0;public long SlowRenderReads=>renderTiming?.SlowReads??0;
 public int ProcessingSampleRate=>dsp?.WaveFormat.SampleRate??0;
 public double? MaxRunningFillMs=>(renderer as StrictExclusiveOut)?.MaxRunningFillMs;public double? MaxPumpGapMs=>(renderer as StrictExclusiveOut)?.MaxPumpGapMs;public long? LateEvents=>(renderer as StrictExclusiveOut)?.LateEvents;
 public long OutputUnderruns=>(renderer as StrictExclusiveOut)?.Underruns??0;public bool OutputMmcss=>(renderer as StrictExclusiveOut)?.MmcssRegistered??false;public double? OutputBufferMs=>(renderer as StrictExclusiveOut)?.BufferMilliseconds;
 public long RenderedBytes{get{try{return renderer?.GetPosition()??0;}catch(System.Runtime.InteropServices.COMException){return 0;}catch(ObjectDisposedException){return 0;}}}
 public float? DopAttenuationDb=>dopVolume?.AttenuationDb;public float? DopCarrierDb=>dopVolume?.CarrierDb;
 public long Frames=>dsp?.Frames??0;public double PeakIn=>dsp?.PeakIn??0;public double PeakOut=>dsp?.PeakOut??0;public long Clipped=>dsp?.Clipped??0;
 public long? InvalidInputSamples=>dsp?.InvalidSamples;
 public double EnergyBefore=>dsp?.EnergyBefore??0;public double EnergyAfter=>dsp?.EnergyAfter??0;
 public double CompGainDb=>dsp?.CompGainDb??0;public double EnergyInLeft=>dsp?.EnergyInLeft??0;public double EnergyInRight=>dsp?.EnergyInRight??0;public double EnergyOutLeft=>dsp?.EnergyOutLeft??0;public double EnergyOutRight=>dsp?.EnergyOutRight??0;
 public string PathLabel=>$"系统入口 {CaptureFormat?.SampleRate:N0} Hz / {CaptureFormat?.BitsPerSample}-bit → {(SameRatePassthrough?"EQ 同率直通（无重采样）":$"EQ / 重采样 {CaptureFormat?.SampleRate:N0}→{FormatLabel?.Split(' ')[0]??"?"}（转接链路，音质不推荐）")} → {FormatLabel}";
 public WaveFormat? CaptureFormat=>capture?.WaveFormat;public bool Running=>running;public bool ExclusiveConfirmed{get;private set;}public bool SameRatePassthrough{get;private set;}
 public string FormatLabel{get;private set;}="";public event Action<string>? Failed;
 public static List<DeviceOption> Devices(){using var e=new MMDeviceEnumerator();return e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active).Select(d=>{var result=new DeviceOption(d.ID,d.FriendlyName,DeviceTransport.Detect(d.ID,d.FriendlyName));d.Dispose();return result;}).ToList();}
 // VB-Cable 家族等虚拟线缆的正确取声方式：播到配对的 Input 端，从配对的 Output（采集端）取流；
  // 回环采集在部分线缆驱动上为静音。找不到配对采集端时回退为渲染回环（Steam/网易等引擎入口）。
  static WasapiCapture CreateEntryCapture(MMDevice renderEntry){
   // 统一走渲染端 loopback：采集点在引擎浮点层（32bit），虚拟线缆的 24bit 引脚不在信号路径里；
   // 兼容所有虚拟声卡（VAC/VB 系），且不吃驱动的采集引脚怪癖。
   return new WasapiLoopbackCapture(renderEntry);}
 public void Start(string inputId,string outputId,Preset preset,int rate,bool exclusive,bool setDefault,int bits=24,bool polling=false,string dsdMode=Dsd.Off,bool followEntry=false,int dsdCarrierBits=0){
  if(running)throw new InvalidOperationException("已经在运行");if(input!=null||output!=null)Dispose();int streamGeneration=Interlocked.Increment(ref generation);if(inputId==outputId)throw new ArgumentException("入口和输出不能是同一设备，否则会产生反馈");preset.Validate();
  if((dsdMode!=Dsd.Off||StrictPcmProbe)&&!exclusive)throw new ArgumentException("DoP requires exclusive output; shared mixing would corrupt the DSD frames");
  LastError=null;ExclusiveConfirmed=false;CapturedBytes=OverflowEvents=StarvedReads=UnderflowBytes=0;AudioRestartGuard.EnsureReady(outputId);DopVolumeLease.Recover(outputId);
  try{using var enumerator=new MMDeviceEnumerator();input=enumerator.GetDevice(inputId);output=enumerator.GetDevice(outputId);capture=CreateEntryCapture(input);
   if(capture.WaveFormat.Channels!=2)throw new ArgumentException("请将虚拟入口的 Windows 声音格式设为双声道");
   // 输出按用户选择的目标采样率运行（可满血 384k）；入口与目标同率时零转换直通，
   // 不同率时仅此一处转换（如实标注），绝不做多级转换。
   int entryRate=capture.WaveFormat.SampleRate;// 零转换语义：跟随的是实际采集到的流速率（配对采集端），不是渲染端的混音格式
   if(followEntry&&dsdMode==Dsd.Off)rate=entryRate;WaveFormat requested;
   if(!exclusive){requested=output.AudioClient.MixFormat;rate=requested.SampleRate;bits=requested.BitsPerSample;}
   else if(dsdMode!=Dsd.Off){rate=Dsd.FrameRate(dsdMode);
    if(dsdCarrierBits is 24 or 32){bits=dsdCarrierBits;requested=DeviceFormats.Require(outputId,rate,bits);}
    else{try{requested=DeviceFormats.Require(outputId,rate,24);bits=24;}catch(ArgumentException){requested=DeviceFormats.Require(outputId,rate,32);bits=32;}}
   }else requested=DeviceFormats.Require(outputId,rate,bits);
   buffer=new BufferedWaveProvider(capture.WaveFormat){BufferDuration=TimeSpan.FromMilliseconds(1500),DiscardOnBufferOverflow=false,ReadFully=true};
   ISampleProvider source=new PaddedCaptureProvider(buffer,missing=>{Interlocked.Increment(ref StarvedReads);Interlocked.Add(ref UnderflowBytes,missing);}).ToSampleProvider();
   // Same-rate entry: pass samples through untouched (no ASRC, no clock servo).
   // The FIFO absorbs ppm-level clock skew; different-rate entries still need conversion.
   SameRatePassthrough=capture.WaveFormat.SampleRate==rate;
   if(!SameRatePassthrough){clock=new ClockedResampler(source,rate,()=>buffer.BufferedDuration.TotalSeconds);source=clock;}
   dsp=new DspProvider(source,preset);
   ISampleProvider encodedSource=dsp;
   if(dsdMode!=Dsd.Off||StrictPcmProbe){dopVolume=new DopVolumeLease(outputId);dopVolume.Faulted+=message=>Fail(new IOException(message));var lease=dopVolume;encodedSource=new DopGainProvider(dsp,()=>lease.Gain);}
   WindowsListen.Recover();listen=new WindowsListen(inputId,outputId);listen.Refresh();
   if(listen.RedirectedCount>0)Thread.Sleep(120);
   void OpenRenderer(){renderer=(dsdMode!=Dsd.Off&&!StandardDsdTransport)||StrictPcmProbe?new StrictExclusiveOut(output,!polling):new StandardOutputSession(output,exclusive?AudioClientShareMode.Exclusive:AudioClientShareMode.Shared,!polling,80);
    IWaveProvider wire=dsdMode!=Dsd.Off?(dsdEncoder=new DsdProvider(encodedSource,dsdMode,requested)):!exclusive&&FloatProvider.Supports(requested)?new FloatProvider(encodedSource,requested):new PcmProvider(encodedSource,bits,requested);
    if(dsdMode!=Dsd.Off||StrictPcmProbe){synthesis=new DopSynthesisQueue(wire,()=>buffer.BufferedDuration.TotalSeconds,160);wire=synthesis;}
    if(dsdMode!=Dsd.Off){dopIntegrity=new DopIntegrityProvider(wire);wire=dopIntegrity;}
    renderTiming=new TimedRenderProvider(wire);renderer.Init(renderTiming);}
   if(exclusive)ExclusiveOpenRetry.Run(OpenRenderer,()=>{renderer?.Dispose();renderer=null;synthesis?.Dispose();synthesis=null;dopIntegrity=null;});else OpenRenderer();
   if(renderer==null)throw new InvalidOperationException("Output renderer was not initialized");
   if(exclusive&&(renderer.OutputWaveFormat.SampleRate!=rate||renderer.OutputWaveFormat.BitsPerSample!=bits||renderer.OutputWaveFormat.Channels!=2))throw new Exception("输出后端变更了请求格式，已拒绝启动");
   renderer.PlaybackStopped+=(s,a)=>{if(streamGeneration==Volatile.Read(ref generation)&&running&&a.Exception!=null)Fail(a.Exception);};
   keeper=new WasapiOut(input,AudioClientShareMode.Shared,true,60);// keeper 必须用渲染端自己的混音格式：部分虚拟声卡（VAC）拒绝非精确格式提案
   keeper.Init(new SilenceProvider(input.AudioClient.MixFormat));
   capture.DataAvailable+=(s,a)=>{if(streamGeneration!=Volatile.Read(ref generation)||!running)return;try{buffer.AddSamples(a.Buffer,0,a.BytesRecorded);Interlocked.Add(ref CapturedBytes,a.BytesRecorded);}catch(Exception ex){Interlocked.Increment(ref OverflowEvents);Fail(ex);}};
   capture.RecordingStopped+=(s,a)=>{if(streamGeneration==Volatile.Read(ref generation)&&running&&a.Exception!=null)Fail(a.Exception);};
   if(setDefault){VolumeRoute.Recover();volume=VolumeRoute.TryCreate(input,output,exclusive);route=new DefaultRoute();route.Set(inputId);}
   dopVolume?.Activate();
   double outputBurst=(renderer as StrictExclusiveOut)?.BufferMilliseconds??0;
   // Exclusive event callbacks consume a complete hardware buffer in one Read.
   // Keep a full callback plus capture-packet jitter ahead of that burst. A
   // fixed 100 ms servo target is insufficient for a 163 ms callback.
   if(clock!=null)clock.TargetBufferSeconds=synthesis!=null?.1:.1+(!polling?outputBurst/1000:0);
   double startupBuffer=synthesis!=null?synthesis.CapacityMilliseconds+100:Math.Max(100,outputBurst+Math.Max(40,(!polling?outputBurst+100:0)));
   running=true;capture.StartRecording();keeper.Play();var wait=System.Diagnostics.Stopwatch.StartNew();while(buffer.BufferedDuration.TotalMilliseconds<startupBuffer&&wait.ElapsedMilliseconds<1200&&LastError==null)Thread.Sleep(5);
   if(LastError!=null)throw new Exception(LastError);synthesis?.Start();renderer.Play();if(exclusive){ExclusiveOwnership.Confirm(outputId);ExclusiveConfirmed=true;}bool floating=renderer.OutputWaveFormat.Encoding==WaveFormatEncoding.IeeeFloat||(renderer.OutputWaveFormat is WaveFormatExtensible ext&&ext.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71"));FormatLabel=$"{renderer.OutputWaveFormat.SampleRate:N0} Hz · {renderer.OutputWaveFormat.BitsPerSample}-bit {(floating?"浮点":"PCM")} · 双声道 · "+(exclusive?(polling?"WASAPI 独占 · 定时缓冲":"WASAPI 独占 · 事件缓冲"):"WASAPI 共享")+(dsdMode==Dsd.Dop64?" · DSD64 调制(DoP)":dsdMode==Dsd.Dop128?" · DSD128 调制(DoP)":"");
  }catch{Dispose();throw;}
 }
 void Fail(Exception ex){if(LastError!=null)return;LastError=ex.Message;running=false;Failed?.Invoke(ex.Message);}
 public void Update(Preset p){p.Validate();dsp?.Update(p);}
 public void RefreshWindowsListening(IReadOnlyList<WindowsListen.ListenInfo>? snapshot=null){if(running)listen?.Refresh(snapshot);}
 public int ActiveInputSessions(){if(input==null)return 0;var list=input.AudioSessionManager.Sessions;int count=0;for(int n=0;n<list.Count;n++)if(list[n].State==NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)count++;return count;}
 public void Dispose(){Interlocked.Increment(ref generation);running=false;ExclusiveConfirmed=false;try{capture?.StopRecording();}catch{}try{renderer?.Stop();}catch{}try{keeper?.Stop();}catch{}
  try{synthesis?.Dispose();}catch{}synthesis=null;try{capture?.Dispose();}catch{}try{renderer?.Dispose();}catch{}try{keeper?.Dispose();}catch{}capture=null;renderer=null;keeper=null;clock=null;dopIntegrity=null;dsdEncoder=null;
  try{dopVolume?.Dispose();}catch(Exception ex){LastError="DoP 声卡音量恢复失败，独立恢复进程已接管："+ex.Message;}dopVolume=null;
  try{volume?.Dispose();}catch{}volume=null;input?.Dispose();output?.Dispose();input=null;output=null;route?.Dispose();route=null;listen?.Dispose();listen=null;
 }
}
