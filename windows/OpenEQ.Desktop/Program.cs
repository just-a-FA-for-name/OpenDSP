using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace OpenEQ;
internal static class Program {
 [STAThread] static int Main(string[] args){
  if(args.Length>0&&args[0]=="--mobile-sync")return MobileSyncWindow.Run(args);
  if(args.Length>0&&args[0]=="--export-preset-library")return PresetLibrary.Export(args[1]);
  if(args.Length>0&&args[0]=="--mobile-feature-checks")return MobileFeatureChecks.Run(args[1]);
  if(args.Length>0&&args[0]=="--mobile-sync-fixture")return MobileFeatureChecks.Fixture(args[1]);
  try{if(args.Length>0&&args[0]=="--self-test")return SelfTest.Run(args.Length>1?args[1]:"windows-chain.json",args.Length>2?int.Parse(args[2]):3,args.Length>3?args[3]:"Tears DSP",args.Length>4?int.Parse(args[4]):48000,args.Length>5?int.Parse(args[5]):24);
   if(args.Length>0&&args[0]=="--apo-install")return NativeApo.Setup(args[1],args[2],false);
   if(args.Length>0&&args[0]=="--apo-uninstall")return NativeApo.Setup(args[1],args[2],true);
   if(args.Length>0&&args[0]=="--apo-allow-unsigned")return NativeApo.AudioPolicy(args[1],false);
   if(args.Length>0&&args[0]=="--apo-restore-policy")return NativeApo.AudioPolicy(args[1],true);
   if(args.Length>0&&args[0]=="--apo-live-check")return NativeApoChecks.Run(args[1],args.Length>2?args[2]:"Tears DSP",args.Length>3&&args[3]=="listen");
   if(args.Length>0&&args[0]=="--apo-multi-check")return NativeApoChecks.Run(args[1],args.Length>2?args[2]:"ACMEE",false,true);
   if(args.Length>0&&args[0]=="--apo-tone")return NativeApoChecks.PlayTone(args[1],int.Parse(args[2]),int.Parse(args[3]));
   if(args.Length>0&&args[0]=="--native-format")return NativeDeviceFormat.Run(args[1],args[2],int.Parse(args[3]),int.Parse(args[4]));
   if(args.Length>0&&args[0]=="--native-format-restore")return NativeDeviceFormat.RestoreCheck(args[1],args[2]);
   if(args.Length>0&&args[0]=="--format-recovery-checks")return FormatRecoveryChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--audio-health-report")return FormatRecoveryChecks.Health(args[1]);
   if(args.Length>0&&args[0]=="--apo-music-filter-check")return NativeMusicCheck.Run(args[1],args.Length>2?args[2]:"ACMEE",true,args.Length>3?double.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture):180,args.Length>4?double.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture):-6.1);
   if(args.Length>0&&args[0]=="--apo-music-check")return NativeMusicCheck.Run(args[1],args.Length>2?args[2]:"ACMEE");
   if(args.Length>0&&args[0]=="--audio-client-check")return NativeClientChecks.Run(args[1],args.Length>2?args[2]:"ACMEE");
   if(args.Length>0&&args[0]=="--apo-crash-owner")return NativeApoRecoveryChecks.Owner(args[1],args[2]);
   if(args.Length>0&&args[0]=="--apo-recovery-check")return NativeApoRecoveryChecks.Run(args[1],args.Length>2?args[2]:"ACMEE");
   if(args.Length>0&&args[0]=="--listen-report"){File.WriteAllText(args[1],JsonSerializer.Serialize(WindowsListen.ReadAll(),new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));return 0;}
   if(args.Length>0&&args[0]=="--default-route-report"){using var en=new MMDeviceEnumerator();var roles=new Dictionary<string,object>();foreach(var role in new[]{Role.Console,Role.Multimedia,Role.Communications}){try{using var d=en.GetDefaultAudioEndpoint(DataFlow.Render,role);roles[role.ToString()]=new{id=d.ID,name=d.FriendlyName,mute=d.AudioEndpointVolume.Mute,dB=d.AudioEndpointVolume.MasterVolumeLevel};}catch(Exception ex){roles[role.ToString()]=new{error=ex.Message,hr="0x"+((uint)ex.HResult).ToString("X8")};}}
    var all=new List<object>();foreach(var d in en.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)){try{all.Add(new{id=d.ID,name=d.FriendlyName,mute=d.AudioEndpointVolume.Mute,dB=d.AudioEndpointVolume.MasterVolumeLevel});}catch(Exception ex){all.Add(new{id=d.ID,name=d.FriendlyName,error=ex.Message,hr="0x"+((uint)ex.HResult).ToString("X8")});}d.Dispose();}roles["all_render"]=all;File.WriteAllText(args[1],JsonSerializer.Serialize(roles,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));return 0;}
   if(args.Length>0&&args[0]=="--fx-fix")return FxFix.Run(args[1],args[2]);
   if(args.Length>0&&args[0]=="--modes-fix")return FxFix.FixModes(args[1],args[2]);
   if(args.Length>0&&args[0]=="--format-clear")return FxFix.ClearFormat(args[1],args[2]);
   if(args.Length>0&&args[0]=="--format-blob")return FxFix.SetFormatBlob(args[2],int.Parse(args[3]),int.Parse(args[4]),args[5]=="float",args[1]);
   if(args.Length>0&&args[0]=="--exclusive-test"){try{using var en=new MMDeviceEnumerator();using var d=en.GetDevice(args[1]);bool shared=args[2]=="shared";var f=shared?d.AudioClient.MixFormat:DeviceFormats.Create(int.Parse(args[2]),int.Parse(args[3]),false);var w=new NAudio.Wave.WasapiOut(d,shared?AudioClientShareMode.Shared:AudioClientShareMode.Exclusive,false,80);w.Init(new NAudio.Wave.SilenceProvider(f));w.Dispose();File.WriteAllText(args.Length>4?args[4]:"/tmp/x.json","hr=OK "+f);return 0;}catch(Exception ex){File.WriteAllText(args.Length>4?args[4]:"/tmp/x.json","hr="+ex.Message);return 1;}}
   if(args.Length>0&&args[0]=="--prop-set-uint")return FxFix.SetUint(args[2],args[3]=="fx",Guid.Parse(args[4]),int.Parse(args[5]),uint.Parse(args[6]),args[1]);
   if(args.Length>0&&args[0]=="--set-default"){int hr=-1;var t=new Thread(()=>{var p=(IPolicyConfig)new PolicyConfigClient();foreach(int role in new[]{0,1,2})hr=p.SetDefaultEndpoint(args[1],role);System.Runtime.InteropServices.Marshal.ReleaseComObject(p);});t.SetApartmentState(ApartmentState.STA);t.Start();t.Join();return hr==0?0:1;}
   if(args.Length>0&&args[0]=="--device-format-raw"){try{int rate=int.Parse(args[3]),bits=int.Parse(args[4]);bool isFloat=args.Length>5&&args[5]=="float";IntPtr FmtPtr(){var data=new MemoryStream();using(var w=new BinaryWriter(data,System.Text.Encoding.UTF8,true)){w.Write((ushort)0xfffe);w.Write((ushort)2);w.Write(rate);w.Write(rate*2*bits/8);w.Write((ushort)(2*bits/8));w.Write((ushort)bits);w.Write((ushort)22);w.Write((ushort)bits);w.Write(3);w.Write((isFloat?new Guid("00000003-0000-0010-8000-00aa00389b71"):new Guid("00000001-0000-0010-8000-00aa00389b71")).ToByteArray());}var m=System.Runtime.InteropServices.Marshal.AllocHGlobal(40);System.Runtime.InteropServices.Marshal.Copy(data.ToArray(),0,m,40);return m;}var ep=FmtPtr();var mx=FmtPtr();var p=(IPolicyConfig)new PolicyConfigClient();var hr=p.SetDeviceFormat(args[2],ep,mx);System.Runtime.InteropServices.Marshal.FreeHGlobal(ep);System.Runtime.InteropServices.Marshal.FreeHGlobal(mx);System.Runtime.InteropServices.Marshal.ReleaseComObject(p);File.WriteAllText(args[1],"hr=0x"+((uint)hr).ToString("X8"));return hr==0?0:1;}catch(Exception ex){File.WriteAllText(args[1],ex.ToString());return 1;}}
   if(args.Length>0&&args[0]=="--device-probe"){using var en=new MMDeviceEnumerator();using var d=en.GetDevice(args[1]);var r=new Dictionary<string,object>{{"id",d.ID},{"name",d.FriendlyName}};
    try{r["mix_format"]=d.AudioClient.MixFormat.ToString();}catch(Exception ex){r["mix_format_error"]=ex.Message+" (0x"+((uint)ex.HResult).ToString("X8")+")";}
    try{var v=d.AudioEndpointVolume;r["volume_db"]=v.MasterVolumeLevel;r["mute"]=v.Mute;}catch(Exception ex){r["volume_error"]=ex.Message+" (0x"+((uint)ex.HResult).ToString("X8")+")";}
    File.WriteAllText(args[2],JsonSerializer.Serialize(r,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));return 0;}
   if(args.Length>0&&args[0]=="--apo-telemetry-watch"){var samples=new List<object>();int seconds=int.Parse(args[3]);using(var telemetry=new NativeTelemetryReader(NativeApo.ConfigPath(args[2]))){var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.Elapsed.TotalSeconds<seconds){Thread.Sleep(1000);samples.Add(new{second=(int)watch.Elapsed.TotalSeconds,frames=telemetry.Frames,rate=telemetry.Rate,clipped=telemetry.Clipped,input_energy=telemetry.InputEnergy,output_energy=telemetry.OutputEnergy});}}
    File.WriteAllText(args[1],JsonSerializer.Serialize(new{samples,scope="Read-only APO telemetry sampling through the shared mapping; no control takeover"},new JsonSerializerOptions{WriteIndented=true}));return 0;}
   if(args.Length>0&&args[0]=="--mic-test")return MicrophoneTest.Run(args[1],args.Length>2?int.Parse(args[2]):8);
   if(args.Length>0&&args[0]=="--play-file"){using var mf=new MediaFoundationReader(args[1]);using var en=new MMDeviceEnumerator();using var dev=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);using var wo=new WasapiOut(dev,AudioClientShareMode.Shared,true,60);wo.Init(mf);wo.Play();while(wo.PlaybackState==PlaybackState.Playing)Thread.Sleep(200);return 0;}
   if(args.Length>0&&args[0]=="--mic-listen")return MicrophoneListen.Run(args[1],args.Length>2?args[2]:"Tears",args.Length>3?int.Parse(args[3]):6);
   if(args.Length>0&&args[0]=="--rack-parity")return RackParity.Run(args[1]);
   if(args.Length>0&&args[0]=="--checks")return DesktopChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--ai-control-checks")return WindowChecks.RunAi(args[1]);
   if(args.Length>0&&args[0]=="--ui-performance-checks")return WindowChecks.RunPerformance(args[1]);
   if(args.Length>0&&args[0]=="--target-checks")return TargetFittingChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--spatial-checks")return SpatialChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--dsp-realtime-checks")return DspRealtimeChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--relay-transfer-checks")return RelayTransferChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--dsd-checks")return DsdOutputChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--startup-checks")return StartupChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--session-report")return AudioSessionReport.Run(args[1]);
   if(args.Length>0&&args[0]=="--soak-monitor")return AudioSoakMonitor.Run(args[1],args.Length>2?int.Parse(args[2]):1200);
   if(args.Length>0&&args[0]=="--dop-volume-guard")return DopVolumeLease.Guard(args[1],args[2]);
   if(args.Length>0&&args[0]=="--dop-volume-checks")return DopVolumeChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--dop-volume-fake-owner")return DopVolumeChecks.Owner(args[1],args[2],args[3]);
   if(args.Length>0&&args[0]=="--dop-volume-device-check")return DopVolumeChecks.Device(args[1],args[2]);
   if(args.Length>0&&args[0]=="--dsd-interop-checks")return DsdInteropChecks.Run(args[1],args[2],args.Length>3?int.Parse(args[3]):1);
   if(args.Length>0&&args[0]=="--unmute-output"){using var en=new MMDeviceEnumerator();using var device=en.GetDevice(args[1]);device.AudioEndpointVolume.Mute=false;return 0;}
   if(args.Length>0&&args[0]=="--dsp-rack-checks")return DspRackChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--exclusive-ownership-check")return ExclusiveOwnershipChecks.Run(args[1],args.Length>2?args[2]:"ACMEE");
   if(args.Length>0&&args[0]=="--resampler-checks")return ResamplerChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--adaptive-resampler-checks")return AdaptiveResamplerChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--dop-queue-checks")return DopQueueChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--dop-volume-watch")return DopVolumeWatch.Run(args[1],int.Parse(args[2]),args[3]);
   if(args.Length>0&&args[0]=="--dsd-reference-check")return DsdReferenceCheck.Run(args[1],args[2],args[3],args[4],int.Parse(args[5]));
   if(args.Length>0&&args[0]=="--bluetooth-test")return BluetoothTest.Run(args[1]);
   if(args.Length>0&&args[0]=="--hotplug-test")return DeviceCycleTest.Run(args[1]);
   if(args.Length>0&&args[0]=="--tone"){Tone(double.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture),int.Parse(args[2]),args.Length>3?int.Parse(args[3]):5);return 0;}
   if(args.Length>0&&args[0]=="--export-presets"){Directory.CreateDirectory(args[1]);int n=0;foreach(var p in DeviceProfiles.Factory())File.WriteAllText(Path.Combine(args[1],$"{n++:00}.json"),p.Encode());return 0;}
   if(args.Length>0&&args[0]=="--profile-switch-test")return ProfileSwitchTest.Run(args[1]);
   if(args.Length>0&&args[0]=="--device-formats")return DeviceFormats.Report(args[1]);
   if(args.Length>0&&args[0]=="--window-checks")return WindowChecks.Run(args[1]);
   if(args.Length>0&&args[0]=="--native-ui-check")return WindowChecks.Run(args[1],true);
   if(args.Length>0&&args[0]=="--endpoint-regression")return EndpointRegression.Run(args[1],args.Length>2?args[2]:"ACMEE",args.Length>3&&args[3]=="polling");
   return DesktopHost.Run(args);
  }catch(Exception e){var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ");Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"errors.log"),DateTimeOffset.Now.ToString("O")+" "+e+Environment.NewLine);return 1;}
 }
 static void Tone(double frequency,int channel,int seconds){using var en=new MMDeviceEnumerator();using var device=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);using var output=new WasapiOut(device,AudioClientShareMode.Shared,true,60);output.Init(new TestTone(frequency,channel).ToWaveProvider());output.Play();Thread.Sleep(seconds*1000);output.Stop();}
 internal sealed class TestTone:ISampleProvider {
  readonly double frequency;readonly int channel;long frame;public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
  public TestTone(double frequency,int channel){this.frequency=frequency;this.channel=channel;}
  public int Read(float[] b,int offset,int count){for(int i=offset;i+1<offset+count;i+=2){float x=(float)(.003*Math.Sin(2*Math.PI*frequency*frame++/48000));b[i]=channel==0?x:0;b[i+1]=channel==1?x:0;}return count;}
 }
}
internal static class SelfTest {
 static void Check(bool yes,string label){if(!yes)throw new Exception(label);}
 static void Near(double a,double b,double tolerance,string label){Check(Math.Abs(a-b)<tolerance,$"{label}: {a} vs {b}");}
 public static int Run(string path,int seconds,string outputName="Tears DSP",int outputRate=48000,int outputBits=24){var log=new Dictionary<string,object>();var engine=new AudioEngine();var children=new List<Process>();
  try{
   var preset=Preset.Flat();preset.Name="PC 真机链路测试";preset.Preamp=-6;preset.Output.Gain=-24;preset.Bands.Clear();preset.Bands.Add(new Band{Type="peak",Frequency=1000,Gain=-12,Q=2,Channel="left"});
   foreach(var rate in new[]{44100,48000,96000,192000}){var band=new Band{Frequency=1000,Gain=6,Q=2};var b=new Biquad(band,rate);Near(20*Math.Log10(b.Magnitude(1000,rate)),6,.000001,"peak gain");}
   var round=Preset.Parse(preset.Encode());Check(round.Bands[0].Channel=="left","JSON channel");
   var proc=new StereoProcessor(48000);var test=Preset.Flat();test.Bands.Clear();test.Preamp=0;test.Output.Gain=0;test.Output.InvertRight=true;proc.Configure(test);for(int i=0;i<20000;i++)proc.Process(.2,.2);Near(proc.Left,.2,.000001,"gain");Near(proc.Right,-.2,.000001,"polarity");
   log["offline_dsp"]="PASS";
   var vectors=new List<object>();foreach(var sampleRate in new[]{44100,48000,96000,192000})foreach(var kind in Preset.Types){var f=new Biquad(new Band{Type=kind,Frequency=1000,Gain=6,Q=.707},sampleRate);vectors.Add(new{type=kind,rate=sampleRate,coefficients=new[]{f.B0,f.B1,f.B2,f.A1,f.A2},impulse=Enumerable.Range(0,256).Select(n=>f.Process(n==0?1:0)).ToArray()});}File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"desktop-dsp-vectors.json"),JsonSerializer.Serialize(vectors));
   var devices=AudioEngine.Devices();log["devices"]=devices;
   var src=devices.FirstOrDefault(d=>d.Name.Contains("Steam Streaming Speakers"))??devices.FirstOrDefault(d=>d.Name.Contains("虚拟"))??throw new Exception("缺少虚拟入口");
   var dst=devices.Single(d=>d.Name.Contains(outputName,StringComparison.OrdinalIgnoreCase));log["input"]=src;log["output"]=dst;
   using var en=new MMDeviceEnumerator();using(var before=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))log["default_before"]=before.ID;
   log["endpoints_before"]=AudioDiagnostics.Read();engine.Start(src.Id,dst.Id,preset,outputRate,true,true,outputBits);log["endpoints_during"]=AudioDiagnostics.Read();log["format"]=engine.FormatLabel;log["capture_format"]=engine.CaptureFormat?.ToString()??"";
   using(var current=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))Check(current.ID==src.Id,"Default route not switched");
   for(int i=0;i<2;i++){var start=new ProcessStartInfo(Environment.ProcessPath!){CreateNoWindow=true,UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--tone");start.ArgumentList.Add(i==0?"1000":"3000");start.ArgumentList.Add(i.ToString());start.ArgumentList.Add((seconds+2).ToString());children.Add(Process.Start(start)??throw new Exception("Tone process failed"));}
   var samples=new List<object>();for(int n=0;n<seconds;n++){Thread.Sleep(1000);samples.Add(new{second=n+1,buffer_ms=engine.BufferedMilliseconds,clock_ppm=engine.ClockPpm,starved_reads=engine.StarvedReads,overflows=engine.OverflowEvents});if(engine.LastError!=null)throw new Exception(engine.LastError);}log["duration_seconds"]=seconds;log["buffer_history"]=samples;log["independent_tone_processes"]=children.Select(p=>p.Id).ToArray();log["active_input_sessions"]=engine.ActiveInputSessions();
   log["captured_bytes"]=engine.CapturedBytes;log["processed_frames"]=engine.Frames;log["energy_before"]=engine.EnergyBefore;log["energy_after"]=engine.EnergyAfter;log["peak_before"]=engine.PeakIn;log["peak_after"]=engine.PeakOut;log["buffer_ms"]=engine.BufferedMilliseconds;log["starved_reads"]=engine.StarvedReads;log["overflow_events"]=engine.OverflowEvents;log["clipped_frames"]=engine.Clipped;log["clock_correction_ppm"]=engine.ClockPpm;
   double measuredL=10*Math.Log10(engine.EnergyOutLeft/engine.EnergyInLeft),measuredR=10*Math.Log10(engine.EnergyOutRight/engine.EnergyInRight);
   log["left_1000hz_measured_db"]=measuredL;log["right_3000hz_measured_db"]=measuredR;log["left_expected_db"]=-42;log["right_expected_db"]=-30;
   Near(measuredL,-42,.25,"Real left-channel PEQ transfer");Near(measuredR,-30,.25,"Real right-channel transfer");
   Check(engine.CapturedBytes>48000*4,"No real loopback data");Check(engine.EnergyBefore>0.00001,"No test tone received");Check(engine.EnergyAfter>0,"No DSP output");Check(engine.ActiveInputSessions()>=3,"Two tone sessions plus silent clock not active");
   int? hr=ExclusiveOwnership.SharedInitializationError(dst.Id);bool blocked=hr==ExclusiveOwnership.DeviceInUse;
   log["shared_contender_blocked"]=blocked;log["contender_hresult"]=hr?.ToString("X8")??"none";Check(blocked,"Exclusive ownership was not proven by AUDCLNT_E_DEVICE_IN_USE");
   Check(engine.OverflowEvents==0,"Capture buffer overflow");Check(engine.Clipped==0,"Clipped output");
   File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"desktop-export.json"),preset.Encode());
   engine.Dispose();using(var after=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia)){log["default_after"]=after.ID;Check(after.ID==(string)log["default_before"],"Default route was not restored");}
   using(var device=en.GetDevice(dst.Id))using(var shared=new WasapiOut(device,AudioClientShareMode.Shared,true,60)){shared.Init(new SilenceProvider(device.AudioClient.MixFormat));shared.Play();Thread.Sleep(80);shared.Stop();}log["shared_reopened_after_stop"]=true;
   log["result"]="PASS";return 0;
  }catch(Exception e){log["result"]="FAIL";log["exception"]=e.ToString();return 1;}
  finally{foreach(var p in children){try{if(!p.WaitForExit(6000))p.Kill();}catch{}p.Dispose();}engine.Dispose();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}
 }
}
