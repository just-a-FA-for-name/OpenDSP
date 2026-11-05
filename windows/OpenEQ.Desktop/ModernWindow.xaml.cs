using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Button=System.Windows.Controls.Button;
using CheckBox=System.Windows.Controls.CheckBox;
using RadioButton=System.Windows.Controls.RadioButton;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
using Color=System.Windows.Media.Color;
using ColorConverter=System.Windows.Media.ColorConverter;
using Orientation=System.Windows.Controls.Orientation;

namespace OpenEQ;
public partial class ModernWindow:System.Windows.Window {
 Preset preset=Preset.Blank();readonly AudioEngine engine=new();NativeApo? nativeApo;bool nativeMode=true;readonly DeviceSelection selection=new();readonly List<string> undo=new();
 readonly DispatcherTimer meterTimer=new(){Interval=TimeSpan.FromMilliseconds(250)},deviceTimer=new(){Interval=TimeSpan.FromSeconds(2)},debounce=new(){Interval=TimeSpan.FromMilliseconds(300)};
 readonly DispatcherTimer editTimer=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(33)};
 readonly DispatcherTimer presetSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(300)};
 readonly System.Collections.ObjectModel.ObservableCollection<BandTile> bandTiles=new();
 bool pendingEdit,pendingSave,pendingFields;
 int editPublications;
 DeviceMonitor? monitor;List<DeviceOption> devices=new();readonly string directory,presetPath,uiPath;readonly bool preview;
 readonly Dictionary<string,DeviceFormats> formatCache=new();Dictionary<string,FormatPreference> formatPreferences=new();string? formatDeviceId;
 readonly DeviceProfiles profiles;bool autoDeviceSwitch=true;bool preferredExclusive=true;bool pollingOutput=true;bool resumeWhenConnected;
 System.Windows.Forms.NotifyIcon? tray;bool exiting;bool closeToTray=true;
  bool updating=true,closed,initialRefresh=true;int selected;string? compareA,compareB;long notificationCount;string dsdMode=Dsd.Off;int dsdCarrierBits;ControlServer? control;
 bool runtimeInitialized,backgroundStart;
 bool deviceScanRunning,deviceScanRequested;
 int deviceRefreshVersion,deviceSnapshotsApplied;
 sealed record DeviceSnapshot(List<DeviceOption> Devices,string? DefaultId,List<WindowsListen.ListenInfo>? Listening,string StreamId);
 Func<DeviceSnapshot>? deviceSnapshotOverride;
 public ModernWindow(bool preview=false,string? stateDirectory=null,bool backgroundStart=false){this.backgroundStart=backgroundStart;this.preview=preview;directory=stateDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ");presetPath=Path.Combine(directory,"desktop-preset.json");uiPath=Path.Combine(directory,"desktop-ui.json");profiles=new DeviceProfiles(directory);if(preview)profiles.Enabled=false;
  InitializeComponent();nativeMode=!preview;NativeMode.IsChecked=nativeMode;MaxHeight=SystemParameters.WorkArea.Height;MaxWidth=SystemParameters.WorkArea.Width;Height=Math.Min(1000,SystemParameters.WorkArea.Height-16);Width=Math.Min(1440,SystemParameters.WorkArea.Width-16);
  if(!preview)try{if(File.Exists(presetPath)){preset=Preset.Parse(File.ReadAllText(presetPath));if(DeviceProfiles.UpgradeLegacyStarter(preset)){if(!File.Exists(presetPath+".before-volume-fix"))File.Copy(presetPath,presetPath+".before-volume-fix");File.WriteAllText(presetPath,preset.Encode());}}}catch(Exception e){RouteStatus.Text="配置读取失败，已加载平直预设："+e.Message;}
  SampleRate.ItemsSource=new[]{48000};SampleRate.SelectedItem=48000;BitDepth.ItemsSource=new[]{16,24,32};BitDepth.SelectedItem=24;FilterType.ItemsSource=Preset.Labels;Channel.ItemsSource=new[]{"双声道","左声道","右声道"};
  if(!preview)try{if(File.Exists(uiPath)){var s=JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(uiPath));if(s!=null){selection.InputId=s.Input;selection.OutputId=s.Output;if(s.Rate>=8000&&s.Rate<=1536000){SampleRate.ItemsSource=new[]{s.Rate};SampleRate.SelectedItem=s.Rate;}formatPreferences=s.Formats??new();if(new[]{16,24,32}.Contains(s.Bits))BitDepth.SelectedItem=s.Bits;Exclusive.IsChecked=s.Exclusive;preferredExclusive=s.Exclusive;RouteDefault.IsChecked=s.Route;profiles.Enabled=s.AutoProfiles;autoDeviceSwitch=s.AutoSwitch;closeToTray=s.CloseToTray;pollingOutput=s.PollingOutput;nativeMode=s.NativeMode;NativeMode.IsChecked=nativeMode;dsdMode=s.DsdMode??"off";dsdCarrierBits=s.DsdCarrierBits is 24 or 32?s.DsdCarrierBits:0;autoStart=s.AutoStart;AutoStartChk.IsChecked=autoStart;if(s.RatePolicy=="target")RateMax.IsChecked=true;else RateFollow.IsChecked=true;}}}catch{}
  Frequency.Changed+=value=>EditBand(b=>b.Frequency=value);Gain.Changed+=value=>EditBand(b=>b.Gain=value);Quality.Changed+=value=>EditBand(b=>b.Q=value);Preamp.Changed+=value=>{if(updating)return;Remember();preset.Preamp=value;Changed();};
  NativeMode.Click+=(s,e)=>{if(Processing)StopEngine();nativeMode=NativeMode.IsChecked==true;ConfigureTransport();RefreshDevices(true);SaveUi();};
  Exclusive.Click+=(s,e)=>{if(Processing)StopEngine();preferredExclusive=Exclusive.IsChecked==true;ConfigureTransport();RefreshDevices(true);SaveUi();};RouteDefault.Click+=(s,e)=>SaveUi();SampleRate.SelectionChanged+=(s,e)=>{if(!updating){RefreshBitDepths();SaveUi();}};SampleRate.KeyDown+=(s,e)=>{if(e.Key==Key.Enter){ValidateCustomRate();e.Handled=true;}};SampleRate.LostKeyboardFocus+=(s,e)=>{if(!updating&&SampleRate.IsEditable)ValidateCustomRate();};BitDepth.SelectionChanged+=(s,e)=>{if(!updating)SaveUi();};
  if(!preview){try{compareA=File.Exists(Path.Combine(directory,"compare-a.json"))?File.ReadAllText(Path.Combine(directory,"compare-a.json")):null;compareB=File.Exists(Path.Combine(directory,"compare-b.json"))?File.ReadAllText(Path.Combine(directory,"compare-b.json")):null;}catch{}}
  Graph.SelectedChanged+=n=>{selected=n;LoadSelected();Bands.SelectedIndex=n;};Graph.EditStarted+=Remember;Graph.EditFinished+=()=>Changed();Graph.NodeMoved+=(n,hz,gain)=>{if(n>=preset.Bands.Count)return;var band=preset.Bands[n];if(band.Frequency==hz&&band.Gain==gain)return;band.Frequency=hz;band.Gain=gain;QueueEdited(true,true);};Graph.QStepped+=(n,direction)=>{selected=n;Remember();preset.Bands[n].Q=Math.Clamp((double)((decimal)preset.Bands[n].Q+direction*.1m),.1,30);QueueEdited(true,true);};Graph.AddRequested+=(hz,gain)=>AddBand(hz,gain);
  engine.Failed+=message=>Dispatcher.BeginInvoke(new Action(()=>{if(closed||engine.Running||engine.LastError!=message)return;StopEngine();resumeWhenConnected=true;SetStatus("输出中断",message,true);}));
  debounce.Tick+=(s,e)=>{debounce.Stop();QueueDeviceRefresh(true);};deviceTimer.Tick+=(s,e)=>QueueDeviceRefresh();meterTimer.Tick+=(s,e)=>UpdateMeter();
  editTimer.Tick+=(s,e)=>{try{FlushEdited();}catch(Exception ex){SetStatus("参数更新失败",ex.Message,true);}};
  presetSaveTimer.Tick+=(s,e)=>{presetSaveTimer.Stop();try{FlushEdited();if(pendingSave){PersistPreset();pendingSave=false;}}catch(Exception ex){SetStatus("预设保存失败",ex.Message,true);}};
  Loaded+=(s,e)=>InitializeRuntime();
  if(!preview){System.Drawing.Icon trayIcon;try{using var iconStream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/opendsp.ico")).Stream;trayIcon=new System.Drawing.Icon(iconStream);}catch{try{trayIcon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath??AppContext.BaseDirectory+"OpenDSP.exe")??System.Drawing.SystemIcons.Application;}catch{trayIcon=System.Drawing.SystemIcons.Application;}}tray=new System.Windows.Forms.NotifyIcon{Icon=trayIcon,Text="OpenDSP · 开源 HiFi DSP 套件",Visible=true};var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add("打开 OpenDSP",null,(s,e)=>Dispatcher.Invoke(()=>{ShowFromTray();}));menu.Items.Add("开始 / 停止处理",null,(s,e)=>Dispatcher.Invoke(()=>StartStopClick(this,new RoutedEventArgs())));menu.Items.Add("退出",null,(s,e)=>Dispatcher.Invoke(()=>{exiting=true;Close();}));tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(()=>{ShowFromTray();});}
  Closing+=(s,e)=>{if(!preview&&closeToTray&&!exiting){e.Cancel=true;Hide();return;}try{FlushEdited();if(pendingSave){PersistPreset();pendingSave=false;}}catch{}closed=true;editTimer.Stop();presetSaveTimer.Stop();meterTimer.Stop();deviceTimer.Stop();debounce.Stop();try{monitor?.Dispose();}catch{}control?.Dispose();nativeApo?.Dispose();nativeApo=null;engine.Dispose();tray?.Dispose();};
  updating=false;BuildCurveCards();InitRackFields();InitSpatialFields();RefreshPreset();RefreshPresetList();if(!preview){ApplyAutoStart();EnsureDesktopShortcut();control=new ControlServer(HandleControl);control.Start();}
 }
 internal void InitializeRuntime(){
  if(runtimeInitialized)return;runtimeInitialized=true;
  try{monitor=new DeviceMonitor();monitor.Changed+=reason=>{Interlocked.Increment(ref notificationCount);Dispatcher.BeginInvoke(new Action(()=>{if(closed)return;debounce.Stop();debounce.Start();}));};deviceTimer.Interval=TimeSpan.FromSeconds(10);}catch(Exception ex){DeviceStatus.Text="使用定时检测 · "+ex.Message;}
  RefreshDevices();deviceTimer.Start();meterTimer.Start();
  if(preview||backgroundStart)Splash.Visibility=Visibility.Collapsed;else Intro();
 }
 public void ShowFromTray(){backgroundStart=false;Show();WindowState=WindowState.Normal;Activate();}
 internal bool RuntimeInitialized=>runtimeInitialized;
 void Intro(){foreach(var item in new[]{Wave1,Wave2,Wave3,Wave4,Wave5}.Select((bar,n)=>(bar,n))){item.bar.RenderTransformOrigin=new System.Windows.Point(.5,.5);var scale=new ScaleTransform(1,1);item.bar.RenderTransform=scale;var a=new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(240+item.n*35)){AutoReverse=true,RepeatBehavior=new RepeatBehavior(2),BeginTime=TimeSpan.FromMilliseconds(item.n*50),EasingFunction=new SineEase()};scale.BeginAnimation(ScaleTransform.ScaleYProperty,a);}
  SplashAvatar.RenderTransformOrigin=new System.Windows.Point(.5,.5);var avatar=new ScaleTransform(.85,.85);SplashAvatar.RenderTransform=avatar;var grow=new DoubleAnimation(.85,1,TimeSpan.FromMilliseconds(650)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};avatar.BeginAnimation(ScaleTransform.ScaleXProperty,grow);avatar.BeginAnimation(ScaleTransform.ScaleYProperty,grow);
  var fade=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(280)){BeginTime=TimeSpan.FromMilliseconds(1800)};fade.Completed+=(s,e)=>Splash.Visibility=Visibility.Collapsed;Splash.BeginAnimation(OpacityProperty,fade);
 }
 void HeaderDrag(object sender,MouseButtonEventArgs e){if(e.OriginalSource is FrameworkElement f&&FindParent<Button>(f)!=null)return;if(e.ClickCount==2){MaximizeClick(sender,e);return;}if(e.ButtonState==MouseButtonState.Pressed)DragMove();}
 static T? FindParent<T>(DependencyObject node)where T:DependencyObject{while(node!=null){if(node is T t)return t;node=VisualTreeHelper.GetParent(node);}return null;}
 void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
 void MaximizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
 void CloseClick(object sender,RoutedEventArgs e)=>Close();
 public void RequestExit(){exiting=true;Close();}
 public bool Processing=>engine.Running||nativeApo!=null;
 public string? SelectedOutputId=>selection.OutputId;
 public void StartForTest(){if(!Processing)StartStopClick(this,new RoutedEventArgs());if(!Processing)throw new Exception(RouteStatus.Text);}
 internal object CheckRealDeviceProfiles(){
  void Check(bool yes,string text){if(!yes)throw new Exception(text);}
  var usb=devices.First(d=>d.Name.Contains("Tears DSP"));var bt=devices.First(d=>d.Transport=="蓝牙");
  OutputDevices.SelectedItem=usb;preset=DeviceProfiles.Factory()[1];RefreshPreset();Changed();
  OutputDevices.SelectedItem=bt;Check(!Exclusive.IsChecked!.Value&&!Exclusive.IsEnabled,"Bluetooth must use shared mode");preset=DeviceProfiles.Factory()[2];RefreshPreset();Changed();
  OutputDevices.SelectedItem=usb;Check(preset.Name==DeviceProfiles.Factory()[1].Name,"USB profile not automatically restored");Check(Exclusive.IsChecked==true&&Exclusive.IsEnabled,"USB exclusive preference not restored");
  OutputDevices.SelectedItem=bt;Check(preset.Name==DeviceProfiles.Factory()[2].Name,"Bluetooth profile not automatically restored");
  var reopened=new DeviceProfiles(directory);Check(reopened.Load(usb.Id)!.Name==DeviceProfiles.Factory()[1].Name&&reopened.Load(bt.Id)!.Name==DeviceProfiles.Factory()[2].Name,"Bindings did not persist");
  return new{result="PASS",usb=usb.Name,bluetooth=bt.Name,usb_preset=DeviceProfiles.Factory()[1].Name,bluetooth_preset=preset.Name,transport_mode_switch="PASS",storage="isolated test directory; user profiles unchanged"};
 }
 public object DeviceHealth()=>new{running=Processing,output=selection.OutputId,output_available=OutputDevices.SelectedItem!=null,device_count=devices.Count,notifications=notificationCount,status=StateTitle.Text,frames=nativeApo?.Frames??engine.Frames};
 static DeviceSnapshot ReadDeviceSnapshot(bool first,bool listening,string streamId){
  var list=AudioEngine.Devices();string? current=null;
  if(first)try{using var en=new MMDeviceEnumerator();using var d=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);current=d.ID;}catch{}
  return new(list,current,listening?WindowsListen.ReadAll():null,streamId);
 }
 async void QueueDeviceRefresh(bool notification=false){
  if(closed)return;
  if(deviceScanRunning){if(notification){deviceScanRequested=true;deviceRefreshVersion++;}return;}
  deviceScanRunning=true;int version=++deviceRefreshVersion;
  bool first=initialRefresh,listening=engine.Running;string stream=engine.StreamIdentity;
  var reader=deviceSnapshotOverride;
  try{
   var snapshot=await Task.Run(()=>reader!=null?reader():ReadDeviceSnapshot(first,listening,stream));
   if(closed||version!=deviceRefreshVersion)return;
   if(snapshot.Listening!=null&&engine.Running&&snapshot.StreamId==engine.StreamIdentity)
    engine.RefreshWindowsListening(snapshot.Listening);
   ApplyDeviceSnapshot(snapshot.Devices,false,snapshot.DefaultId);
   deviceSnapshotsApplied++;
  }catch(Exception ex){if(!closed&&version==deviceRefreshVersion)DeviceRefreshFailed(ex);}
  finally{deviceScanRunning=false;if(deviceScanRequested&&!closed){deviceScanRequested=false;QueueDeviceRefresh();}}
 }
 void RefreshDevices(bool force=false){if(closed)return;deviceRefreshVersion++;try{
  var snapshot=ReadDeviceSnapshot(initialRefresh,engine.Running,engine.StreamIdentity);
  if(snapshot.Listening!=null)engine.RefreshWindowsListening(snapshot.Listening);
  ApplyDeviceSnapshot(snapshot.Devices,force,snapshot.DefaultId);
 }catch(Exception ex){DeviceRefreshFailed(ex);}}
 void ApplyDeviceSnapshot(List<DeviceOption> list,bool force,string? current){
   bool changed=selection.Apply(list,initialRefresh,current);bool bridgeUI=Exclusive.IsChecked==true;if(!bridgeUI&&list.Any(d=>string.Equals(d.Id,selection.OutputId,StringComparison.OrdinalIgnoreCase)&&d.Transport=="虚拟"))selection.OutputId=(list.FirstOrDefault(d=>d.Transport=="USB")??list.FirstOrDefault(d=>d.Transport!="虚拟"))?.Id;bool first=initialRefresh;initialRefresh=false;if(!changed&&!first&&!force){TryResume();return;}
   var newlyConnected=list.Where(d=>(d.Transport=="USB"||d.Transport=="蓝牙")&&!devices.Any(old=>old.Id==d.Id)).ToList();bool restart=false;string? previousOutput=selection.OutputId;
   if(!first&&autoDeviceSwitch&&newlyConnected.Count==1){if(!preview&&previousOutput!=null)profiles.Save(previousOutput,preset);restart=Processing||resumeWhenConnected||engine.LastError!=null;if(Processing)StopEngine();selection.OutputId=newlyConnected[0].Id;}
   devices=list;if(Processing&&((Exclusive.IsChecked==true&&!selection.InputAvailable(list))||!selection.OutputAvailable(list))){StopEngine();resumeWhenConnected=true;SetStatus("连接断开","音频设备已断开，处理已停止，等待耳机重新连接。",true);}
   updating=true;InputDevices.ItemsSource=bridgeUI?list:new[]{new DeviceOption("native","Windows 原生音频路径（无需虚拟入口）")};OutputDevices.ItemsSource=bridgeUI?list:list.Where(d=>d.Transport!="虚拟").ToList();InputDevices.SelectedItem=bridgeUI?list.FirstOrDefault(d=>string.Equals(d.Id,selection.InputId,StringComparison.OrdinalIgnoreCase)):InputDevices.Items[0];InputDevices.IsEnabled=bridgeUI&&!Processing;OutputDevices.SelectedItem=list.FirstOrDefault(d=>string.Equals(d.Id,selection.OutputId,StringComparison.OrdinalIgnoreCase));updating=false;
   DeviceStatus.Text=$"自动识别 · {list.Count} 个输出设备";StartButton.IsEnabled=Processing||((bridgeUI?selection.InputAvailable(list)&&selection.InputId!=selection.OutputId:true)&&selection.OutputAvailable(list));
   ConfigureTransport();if(!preview&&(first||previousOutput!=selection.OutputId))LoadDeviceProfile(first);if(restart&&StartButton.IsEnabled)StartStopClick(this,new RoutedEventArgs());else TryResume();
   if(!first&&!preview){Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"device-events.log"),$"{DateTimeOffset.Now:O} endpoints={list.Count} notifications={notificationCount} input={selection.InputAvailable(list)} output={selection.OutputAvailable(list)}{Environment.NewLine}");}
 }
 void DeviceRefreshFailed(Exception ex){updating=false;DeviceStatus.Text="设备检测暂时失败，稍后重试";if(!engine.Running)RouteStatus.Text=ex.Message;try{Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"device-events.log"),$"{DateTimeOffset.Now:O} refresh-failed {ex}{Environment.NewLine}");}catch{}}
 void TryResume(){if(resumeWhenConnected&&!Processing&&(Exclusive.IsChecked!=true||selection.InputAvailable(devices))&&selection.OutputAvailable(devices)&&(Exclusive.IsChecked!=true||selection.InputId!=selection.OutputId))StartStopClick(this,new RoutedEventArgs());}
 void DeviceSelected(object sender,SelectionChangedEventArgs e){if(updating)return;deviceRefreshVersion++;if(Exclusive.IsChecked==true&&sender==InputDevices&&InputDevices.SelectedItem is DeviceOption i)selection.InputId=i.Id;if(sender==OutputDevices&&OutputDevices.SelectedItem is DeviceOption o){if(!preview&&selection.OutputId!=null)profiles.Save(selection.OutputId,preset);RememberFormat();selection.OutputId=o.Id;ConfigureTransport();LoadDeviceProfile(false);}StartButton.IsEnabled=Processing||(OutputDevices.SelectedItem!=null&&(Exclusive.IsChecked!=true||(InputDevices.SelectedItem!=null&&selection.InputId!=selection.OutputId)));SaveUi();}
 void ConfigureTransport(){bool bridge=Exclusive.IsChecked==true;bool bluetooth=(OutputDevices.SelectedItem as DeviceOption)?.Transport=="蓝牙";updating=true;
  if(bluetooth)Exclusive.IsChecked=false;
  // EQ 与传输正交：原生系统 EQ 与 USB 独占可同开（同开时由独占链路内的完整 DSP 机架处理）。
  Exclusive.IsEnabled=!Processing&&!bluetooth;NativeMode.IsEnabled=!Processing;
  NativeFormatButton.IsEnabled=!Processing&&!bridge;
  if(bluetooth&&!engine.Running)RouteStatus.Text="蓝牙耳机 · 系统共享输出与完整 EQ";
  updating=false;RefreshOutputFormats();}
 sealed class FormatPreference{public int Rate{get;set;}=48000;public int Bits{get;set;}=24;public bool Polling{get;set;}=true;}
 void RememberFormat(){if(selection.OutputId!=null&&Exclusive.IsChecked==true&&SampleRate.SelectedItem is int rate&&BitDepth.SelectedItem is int bits)formatPreferences[selection.OutputId]=new FormatPreference{Rate=rate,Bits=bits,Polling=pollingOutput};}
 void RefreshOutputFormats(bool force=false){if((Processing&&Exclusive.IsChecked==true)||OutputDevices.SelectedItem is not DeviceOption output)return;bool priorUpdating=updating;updating=true;try{
  int oldRate=SampleRate.SelectedItem is int sr?sr:48000,oldBits=BitDepth.SelectedItem is int sb?sb:24;if(formatDeviceId!=output.Id&&formatPreferences.TryGetValue(output.Id,out var saved)){oldRate=saved.Rate;oldBits=saved.Bits;pollingOutput=saved.Polling;}else if(formatDeviceId!=null&&formatDeviceId!=output.Id){oldRate=48000;oldBits=24;pollingOutput=true;}
  if(force||!formatCache.TryGetValue(output.Id,out _))formatCache[output.Id]=DeviceFormats.Read(output.Id,oldRate);var caps=formatCache[output.Id];int nativeBits=Exclusive.IsChecked!=true?NativeFormatState.Describe(NativeDeviceFormat.Read(output.Id).Endpoint).Bits:caps.MixBits;formatDeviceId=output.Id;
  bool exclusive=Exclusive.IsChecked==true;bool selectable=exclusive||NativeMode.IsChecked==true;bool bridge=exclusive;if(!exclusive){oldRate=caps.MixRate;oldBits=nativeBits;}Graph.SampleRate=!exclusive?caps.MixRate:(SampleRate.SelectedItem is int graphRate?graphRate:48000);Graph.InvalidateVisual();var rates=selectable?caps.Exclusive.Select(f=>f.Rate).Distinct().Order().ToArray():new[]{caps.MixRate};SampleRate.ItemsSource=rates;SampleRate.SelectedItem=rates.Contains(oldRate)?oldRate:bridge?rates[^1]:rates.Contains(48000)?48000:rates.Cast<int?>().FirstOrDefault();SampleRate.IsEditable=exclusive;SampleRate.IsEnabled=selectable;BitDepth.IsEnabled=selectable;
  int chosen=SampleRate.SelectedItem is int r?r:0;var bits=selectable?caps.Exclusive.Where(f=>f.Rate==chosen).Select(f=>f.Bits).Distinct().Order().ToArray():new[]{Exclusive.IsChecked!=true?nativeBits:caps.MixBits};BitDepth.ItemsSource=bits;BitDepth.SelectedItem=bits.Contains(oldBits)?oldBits:bits.Contains(24)?24:bits.Cast<int?>().LastOrDefault();
  SampleRate.ToolTip=Exclusive.IsChecked!=true?"选择驱动支持的采样率，再点击同步到系统；实际 EQ 在同步前仍按当前系统格式运行":exclusive?$"声卡驱动确认的常用 PCM 采样率；可输入其他采样率并按 Enter 查询。查询耗时 {caps.QueryMilliseconds} ms":"共享输出使用 Windows 当前混音格式";BitDepth.ToolTip=Exclusive.IsChecked!=true?"选择设备支持的 PCM 位深，点击同步到系统后生效；状态栏 float32 为 Windows 内部混音精度":"仅列出该采样率下驱动支持的 PCM 位深；传输位深不等于 DAC 有效精度";
 }finally{updating=priorUpdating;}}
 void RefreshBitDepths(){if(Exclusive.IsChecked==true&&SampleRate.SelectedItem is int graphRate){Graph.SampleRate=graphRate;Graph.InvalidateVisual();}if(updating||formatDeviceId==null||!formatCache.TryGetValue(formatDeviceId,out var caps)||SampleRate.SelectedItem is not int rate)return;int nativeBits=Exclusive.IsChecked!=true?NativeFormatState.Describe(NativeDeviceFormat.Read(formatDeviceId).Endpoint).Bits:caps.MixBits;int oldBits=BitDepth.SelectedItem is int b?b:24;updating=true;try{var bits=(Exclusive.IsChecked==true||NativeMode.IsChecked==true)?caps.Exclusive.Where(f=>f.Rate==rate).Select(f=>f.Bits).Distinct().Order().ToArray():new[]{Exclusive.IsChecked!=true?nativeBits:caps.MixBits};BitDepth.ItemsSource=bits;BitDepth.SelectedItem=bits.Contains(oldBits)?oldBits:bits.Contains(24)?24:bits.Cast<int?>().LastOrDefault();}finally{updating=false;}}
 bool ValidateCustomRate(){if(updating||engine.Running||Exclusive.IsChecked!=true||OutputDevices.SelectedItem is not DeviceOption output)return true;
  if(!int.TryParse(SampleRate.Text,out int requested)||requested<8000||requested>1536000){SetStatus("采样率无效","请输入 8000 至 1536000 Hz，实际支持范围以驱动查询为准",true);return false;}
  var caps=formatCache.GetValueOrDefault(output.Id);if(caps==null||!caps.Exclusive.Any(f=>f.Rate==requested)){caps=DeviceFormats.Read(output.Id,requested);formatCache[output.Id]=caps;if(!caps.Exclusive.Any(f=>f.Rate==requested)){SetStatus("不支持的采样率",$"声卡驱动未接受 {requested:N0} Hz 双声道 PCM 独占",true);return false;}}
  bool changed=SampleRate.SelectedItem is not int old||old!=requested;updating=true;SampleRate.ItemsSource=caps.Exclusive.Select(f=>f.Rate).Distinct().Order().ToArray();SampleRate.SelectedItem=requested;updating=false;if(changed){RefreshBitDepths();SaveUi();}return true;
 }
 void LoadDeviceProfile(bool initial){if(preview||!profiles.Enabled||selection.OutputId==null)return;var bound=profiles.Load(selection.OutputId);if(bound!=null){Remember();preset=bound;selected=0;RefreshPreset();RouteStatus.Text="已自动加载设备预设："+preset.Name;}else{Remember();preset=Preset.Blank();preset.Name="平直 · 原声起点";selected=0;RefreshPreset();RouteStatus.Text="新设备已加载平直起点，可按听感调整并自动记忆";}profiles.Save(selection.OutputId,preset);}
 void StartStopClick(object sender,RoutedEventArgs e){if(Processing){StopEngine();return;}try{
   if(Exclusive.IsChecked==true){StartBridge();return;}
  if(NativeMode.IsChecked==true){NativeApo.EnsureSupported(preset);StartNative();return;}
   throw new Exception("请至少开启原生系统 EQ 或 USB 独占输出");
  }catch(Exception ex){nativeApo?.Dispose();nativeApo=null;engine.Dispose();SetSelectors(true);try{Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"errors.log"),$"{DateTimeOffset.Now.ToString("O")} [StartFull] {ex}{Environment.NewLine}");}catch{}string hint=ex is System.Runtime.InteropServices.COMException com&&com.HResult==unchecked((int)0x800401F3)?"设备驱动接口异常（可能是音效注册残留或驱动状态损坏）：请在 设置 → 维护与恢复 点“恢复设备原有系统音效”，若仍失败请重装该设备驱动":ex.Message;SetStatus("启动失败",hint,true);}}
 void StartBridge(){if(InputDevices.SelectedItem is not DeviceOption input||OutputDevices.SelectedItem is not DeviceOption output)throw new Exception("请选择虚拟入口与输出设备");
  if(!ValidateCustomRate())return;if(File.Exists(Path.Combine(directory,"route-recovery.json")))DefaultRoute.Recover();
  int targetRate=(int)SampleRate.SelectedItem;
  // 独占链路的 EQ 一律由 DSP 机架执行（64-bit 双精度、同率零转换）：第三方虚拟入口的
  // 端点音效（APO）不会被引擎锁定，安装了也不处理，因此不再安装，保持系统原样。
  engine.Start(input.Id,output.Id,preset,targetRate,true,RouteDefault.IsChecked==true,(int)BitDepth.SelectedItem,pollingOutput,dsdMode,RateFollow.IsChecked==true,dsdCarrierBits);
  resumeWhenConnected=false;StartButton.Content="■  停止处理";SetSelectors(false);
  SetStatus("USB 独占输出",engine.PathLabel+"。播放器换曲无声时，请将其输出明确设为系统音频入口，避免缓存旧设备");SaveUi();}
 void StartNative(){if(dsdMode!=Dsd.Off)throw new Exception("DoP 需要 USB 独占输出；原生共享模式请使用 PCM");if(backgroundStart&&(!NativeApo.UnsignedAllowed()||OutputDevices.SelectedItem is not DeviceOption startupOutput||!NativeApo.Installed(startupOutput.Id)||!NativeApo.ComponentCurrent()))throw new Exception("后台启动未执行首次安装或升级；请从托盘打开窗口后手动开始处理");if(OutputDevices.SelectedItem is not DeviceOption candidate||candidate.Transport=="虚拟")throw new Exception("请选择物理输出设备");AudioRestartGuard.EnsureReady(candidate.Id);bool refreshed=false,policyChanged=false;if(!NativeApo.UnsignedAllowed()){var answer=System.Windows.MessageBox.Show(this,"当前是未签名的开发版 APO。Windows 需要允许加载未签名音效，可能影响要求受保护音频路径的应用。原设置会备份，可在更多菜单恢复。是否允许并继续安装？","原生 APO 开发安装",MessageBoxButton.YesNo,MessageBoxImage.Information);if(answer!=MessageBoxResult.Yes)return;NativeApo.RequestPolicy(false);policyChanged=true;}if(OutputDevices.SelectedItem is not DeviceOption output||output.Transport=="虚拟")throw new Exception("请选择物理声卡或蓝牙输出");if(policyChanged||!NativeApo.Installed(output.Id)||!NativeApo.ComponentCurrent()){SetStatus("安装原生 EQ","正在绑定物理端点，Windows 会显示管理员安装提示");NativeApo.RequestInstall(output.Id);refreshed=true;}if(File.Exists(Path.Combine(directory,"route-recovery.json")))DefaultRoute.Recover();nativeApo=new NativeApo(output.Id,preset,RouteDefault.IsChecked==true);StartButton.Content="■  停止处理";SetSelectors(false);SetStatus("原生 EQ 就绪",refreshed?"首次安装/升级已刷新音频服务；请重新打开播放器后试听。后续参数调整和启停无需重启音频服务":"物理设备 APO 已绑定，等待播放流验证；无需虚拟入口或回环采集");SaveUi();}
 void NativeFormatClick(object sender,RoutedEventArgs e){bool wasProcessing=Processing;bool stopped=false;try{if(Exclusive.IsChecked==true||OutputDevices.SelectedItem is not DeviceOption output)throw new Exception("系统格式同步仅在共享原生路径可用；请先关闭 USB 独占输出");if(SampleRate.SelectedItem is not int rate||BitDepth.SelectedItem is not int bits)throw new Exception("请选择驱动支持的采样率和位深");AudioRestartGuard.EnsureReady(output.Id);AudioActivityGuard.EnsureIdle(output.Id);StopEngine();stopped=true;NativeDeviceFormat.Set(output.Id,rate,bits);RefreshOutputFormats(true);if(wasProcessing)StartStopClick(this,new RoutedEventArgs());SetStatus("已同步 Windows 格式",$"物理设备与系统混音已设为 {rate:N0} Hz，设备 PCM {bits}-bit；原始格式已备份。播放器若报告设备失效，请重新打开播放器");SaveUi();}catch(Exception ex){if(stopped){try{RefreshOutputFormats(true);if(wasProcessing&&!Processing&&ex is not AggregateException)StartStopClick(this,new RoutedEventArgs());}catch(Exception recovery){SetStatus("格式恢复需检查",ex.Message+"；"+recovery.Message,true);return;}}SetStatus("格式尚未同步",ex.Message,true);}}
 void StopEngine(){resumeWhenConnected=false;nativeApo?.Dispose();nativeApo=null;engine.Dispose();StartButton.Content="▶  开始处理";SetSelectors(true);SetStatus("待机","处理已停止，已尝试恢复此前的系统默认输出");}
 void SetSelectors(bool enabled){InputDevices.IsEnabled=enabled&&Exclusive.IsChecked==true;OutputDevices.IsEnabled=RouteDefault.IsEnabled=enabled;SampleRate.IsEnabled=BitDepth.IsEnabled=!Exclusive.IsChecked==true||enabled;Exclusive.IsEnabled=enabled&&(OutputDevices.SelectedItem as DeviceOption)?.Transport!="蓝牙";NativeMode.IsEnabled=enabled;}
 void SetStatus(string title,string message,bool error=false){StateTitle.Text=title;StateTitle.Foreground=Brush(error?"#BC6B54":Processing?"#168D7A":"#8390A6");StateBadge.Background=Brush(error?"#FFF2EB":Processing?"#E8F8F2":"#F0F3F9");RouteStatus.Text=message;RouteStatus.ToolTip=message;}
 void SaveUi(){if(preview)return;RememberFormat();if(Exclusive.IsChecked==true&&(OutputDevices.SelectedItem as DeviceOption)?.Transport!="蓝牙")preferredExclusive=true;Directory.CreateDirectory(directory);File.WriteAllText(uiPath,JsonSerializer.Serialize(new UiSettings{Input=selection.InputId,Output=selection.OutputId,Rate=SampleRate.SelectedItem is int r?r:48000,Bits=BitDepth.SelectedItem is int b?b:24,Exclusive=preferredExclusive,Route=RouteDefault.IsChecked==true,AutoProfiles=profiles.Enabled,AutoSwitch=autoDeviceSwitch,CloseToTray=closeToTray,PollingOutput=pollingOutput,NativeMode=nativeMode,DsdMode=dsdMode,DsdCarrierBits=dsdCarrierBits,AutoStart=autoStart,RatePolicy=RateFollow.IsChecked==true?"follow":"target",Formats=formatPreferences}));}
 void UpdateMeter(){if(!IsVisible||WindowState==WindowState.Minimized)return;if(nativeApo!=null){Meter.Text=$"原生 APO · {nativeApo.Rate:N0} Hz · {nativeApo.Channels} 声道 · 削波 {nativeApo.Clipped}";Meter.ToolTip="物理设备系统音频路径；无回环采集或重采样 · 音频进程 "+nativeApo.AudioProcessId;if(nativeApo.HasApplied&&nativeApo.Rate>0){if(Graph.SampleRate!=(int)nativeApo.Rate)UpdateNativeGraph((int)nativeApo.Rate);SetStatus("原生 EQ",$"Windows 原生混音 → OpenEQ APO → 物理设备 · {nativeApo.Rate:N0} Hz / float32");}}else if(engine.Running){if(engine.ProcessingSampleRate>0&&Graph.SampleRate!=engine.ProcessingSampleRate)UpdateNativeGraph(engine.ProcessingSampleRate);Meter.Text=$"{engine.FormatLabel} · {(engine.SameRatePassthrough?"同率，无重采样":"异率转换")} · 输出 {Db(engine.PeakOut):F1} dBFS · 软件 {(preset.Enabled?preset.Preamp:0)+preset.Output.Gain:F1} dB · 系统入口 {engine.InputVolumePercent:F0}% · 侦听 {engine.ListeningMicrophones} 路";Meter.ToolTip=$"{engine.VolumeMode} · 输入 {Db(engine.PeakIn):F1} dBFS · 缓冲 {engine.BufferedMilliseconds:F0} ms · 同步 {engine.ClockPpm:F1} ppm · 削波 {engine.Clipped} · 欠载 {engine.StarvedReads} 次 / {engine.UnderflowBytes} 字节 · 溢出 {engine.OverflowEvents} · 最慢生成 {engine.MaxRenderReadMs:F1} ms";}else Meter.Text="开源 HiFi DSP 套件 · Fa";
  var chips=new List<string>();if(preset.Output.Compressor?.Enabled==true)chips.Add($"压缩 {preset.Output.Compressor.Ratio:0.#}:1");if(preset.Output.DcBlock)chips.Add("直流滤波");if(preset.Output.Matrix!=null&&Math.Abs(preset.Output.Matrix.Width-1)>.01)chips.Add($"宽度 {preset.Output.Matrix.Width:0.#}");if(dsdMode!=Dsd.Off)chips.Add(dsdMode==Dsd.Dop64?"DSD64":"DSD128");if(Graph.Target!=null)chips.Add("目标曲线");if(chips.Count>0)Meter.Text+=" · "+string.Join(" · ",chips);
  GrMeter.Db=engine.Running?Math.Max(-60,engine.CompGainDb):-60;GrMeter.InvalidateVisual();}
 void UpdateNativeGraph(int actualRate){Graph.SampleRate=actualRate;Graph.InvalidateVisual();}
 static double Db(double v)=>20*Math.Log10(Math.Max(1e-12,v));
 static SolidColorBrush Brush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
 void Remember(){if(updating)return;if(undo.Count>=100)undo.RemoveAt(0);undo.Add(preset.Encode());}
 void PersistPreset(){if(preview)return;Directory.CreateDirectory(directory);File.WriteAllText(presetPath,preset.Encode());if(selection.OutputId!=null)profiles.Save(selection.OutputId,preset);}
 void QueueEdited(bool save=true,bool fields=false){
  pendingEdit=true;pendingSave|=save;pendingFields|=fields;
  Graph.Preset=preset;Graph.Selected=selected;Graph.InvalidateVisual();
  if(!editTimer.IsEnabled)editTimer.Start();
  if(save){presetSaveTimer.Stop();presetSaveTimer.Start();}
 }
 void FlushEdited(){
  editTimer.Stop();
  if(pendingEdit){pendingEdit=false;Changed(false);}
  if(pendingFields){pendingFields=false;RefreshTiles();LoadSelected();}
 }
 void Changed(bool save=true){editTimer.Stop();pendingEdit=false;preset.Validate();nativeApo?.Update(preset);engine.Update(preset);editPublications++;Graph.Preset=preset;Graph.Selected=selected;Graph.InvalidateVisual();BandCount.Text=$"{preset.Bands.Count} / 32 段";if(pendingFields){pendingFields=false;RefreshTiles();LoadSelected();}if(save){presetSaveTimer.Stop();PersistPreset();pendingSave=false;}}
 void RefreshPreset(bool apply=true){updating=true;EqEnabled.IsChecked=preset.Enabled;Preamp.SetSilently(preset.Preamp);SyncRack();updating=false;Graph.Preset=preset;RefreshTiles();LoadSelected();if(apply)Changed(false);}
 void RefreshTiles(){bool previous=updating;updating=true;try{if(!ReferenceEquals(Bands.ItemsSource,bandTiles))Bands.ItemsSource=bandTiles;
  for(int n=0;n<preset.Bands.Count;n++){var b=preset.Bands[n];var tile=new BandTile(n,$"{n+1:00}  {(b.Enabled?Preset.Labels[Array.IndexOf(Preset.Types,b.Type)].Split(' ')[0]:"旁路")}",b.Frequency>=1000?(b.Frequency/1000).ToString("0.##")+" kHz":b.Frequency.ToString("0.#")+" Hz");
   if(n>=bandTiles.Count)bandTiles.Add(tile);else if(bandTiles[n]!=tile)bandTiles[n]=tile;
  }
  while(bandTiles.Count>preset.Bands.Count)bandTiles.RemoveAt(bandTiles.Count-1);
  if(Bands.SelectedIndex!=selected)Bands.SelectedIndex=selected;
 }finally{updating=previous;}}
 void LoadSelected(){updating=true;bool available=preset.Bands.Count>0;FilterType.IsEnabled=Frequency.IsEnabled=Gain.IsEnabled=Quality.IsEnabled=Channel.IsEnabled=BandEnabled.IsEnabled=CopyBandButton.IsEnabled=DeleteBandButton.IsEnabled=available;if(available){selected=Math.Clamp(selected,0,preset.Bands.Count-1);var b=preset.Bands[selected];SelectedTitle.Text=$"频段 {selected+1:00}";FilterType.SelectedIndex=Array.IndexOf(Preset.Types,b.Type);Frequency.SetSilently(b.Frequency);Gain.SetSilently(b.Gain);Quality.SetSilently(b.Q);Channel.SelectedIndex=Array.IndexOf(new[]{"both","left","right"},b.Channel);BandEnabled.IsChecked=b.Enabled;bool gainUsed=b.Type=="peak"||b.Type.EndsWith("shelf");Gain.IsEnabled=gainUsed;BandHint.Text=gainUsed?"直接输入或按 ± 微调，每次 0.1。数值编辑后实时生效。":"此滤波类型不使用增益参数；频率和 Q 值可精确调整。";}else SelectedTitle.Text="添加第一个频段";Graph.Selected=available?selected:-1;Graph.InvalidateVisual();updating=false;}
 void BandSelected(object sender,SelectionChangedEventArgs e){if(updating||Bands.SelectedItem is not BandTile tile)return;selected=tile.Index;LoadSelected();}
 void EditBand(Action<Band> edit){if(updating||selected<0||selected>=preset.Bands.Count)return;Remember();edit(preset.Bands[selected]);Changed();RefreshTiles();}
 void EqToggle(object sender,RoutedEventArgs e){if(updating||EqEnabled==null)return;Remember();preset.Enabled=EqEnabled.IsChecked==true;Changed();}
 void BandToggle(object sender,RoutedEventArgs e){if(BandEnabled==null)return;EditBand(b=>b.Enabled=BandEnabled.IsChecked==true);}
 void FilterChanged(object sender,SelectionChangedEventArgs e){if(updating)return;EditBand(b=>{if(FilterType.SelectedIndex>=0)b.Type=Preset.Types[FilterType.SelectedIndex];if(Channel.SelectedIndex>=0)b.Channel=new[]{"both","left","right"}[Channel.SelectedIndex];});LoadSelected();}
 void AddBand(double hz=1000,double gain=0){if(preset.Bands.Count>=32){RouteStatus.Text="最多支持 32 段滤波器";return;}Remember();preset.Bands.Add(new Band{Frequency=hz,Gain=gain});selected=preset.Bands.Count-1;Changed();RefreshTiles();LoadSelected();}
 void AddBandClick(object sender,RoutedEventArgs e)=>AddBand();
 void DuplicateClick(object sender,RoutedEventArgs e){if(preset.Bands.Count==0||preset.Bands.Count>=32)return;var old=preset.Copy().Bands[selected];Remember();preset.Bands.Insert(selected+1,old);selected++;Changed();RefreshTiles();LoadSelected();}
 void DeleteBandClick(object sender,RoutedEventArgs e){if(preset.Bands.Count==0)return;Remember();preset.Bands.RemoveAt(selected);selected=Math.Clamp(selected,0,Math.Max(0,preset.Bands.Count-1));Changed();RefreshTiles();LoadSelected();}
 void UndoClick(object sender,RoutedEventArgs e){if(undo.Count==0)return;try{HandleControlCore(JsonSerializer.Serialize(new{cmd="undo_preset",expected_revision=AiTuning.Revision(preset,ControlContext)}));}catch(Exception ex){SetStatus("未能撤销",ex.Message,true);}}
 void ImportClick(object sender,RoutedEventArgs e){var dialog=new Microsoft.Win32.OpenFileDialog{Filter="OpenEQ 配置|*.json|所有文件|*.*"};if(dialog.ShowDialog(this)!=true)return;try{if(ApplyUserPreset(Preset.Parse(File.ReadAllText(dialog.FileName))))RouteStatus.Text="已导入预设："+preset.Name;}catch(Exception ex){SetStatus("导入失败",ex.Message,true);}}
 void ExportClick(object sender,RoutedEventArgs e){var dialog=new Microsoft.Win32.SaveFileDialog{Filter="OpenEQ 配置|*.json",FileName="OpenEQ-preset.json"};if(dialog.ShowDialog(this)==true)try{File.WriteAllText(dialog.FileName,preset.Encode());RouteStatus.Text="预设已导出；高级空间参数需支持该功能的OpenDSP版本。";}catch(Exception ex){SetStatus("导出失败",ex.Message,true);}}
 void ExportResponse(){
  var dialog=new Microsoft.Win32.SaveFileDialog{Filter="OpenDSP 线性响应分析|*.json",FileName="OpenDSP-response.json"};
  if(dialog.ShowDialog(this)!=true)return;
  try{
   File.WriteAllText(dialog.FileName,HandleControl("{\"cmd\":\"analyze_response\"}"));
   RouteStatus.Text="已导出四条声道路径的幅频、相位和群延迟；这是数字线性模型，不是耳机实测。";
  }catch(Exception ex){SetStatus("分析导出失败",ex.Message,true);}
 }
 void PresetClick(object sender,RoutedEventArgs e){var menu=new ContextMenu();void Item(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(s,a)=>action();menu.Items.Add(item);}Item("保存当前预设",()=>{var dir=Path.Combine(directory,"presets");Directory.CreateDirectory(dir);var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(preset.Name)));File.WriteAllText(Path.Combine(dir,hash+".json"),preset.Encode());RouteStatus.Text="预设已保存："+preset.Name;});Item("重命名…",Rename);
  var auto=new MenuItem{Header="自动记忆每个设备的 EQ",IsCheckable=true,IsChecked=profiles.Enabled};auto.Click+=(s,a)=>{profiles.Enabled=auto.IsChecked;if(profiles.Enabled&&selection.OutputId!=null)profiles.Save(selection.OutputId,preset);SaveUi();};menu.Items.Add(auto);var follow=new MenuItem{Header="新耳机接入时自动切换输出",IsCheckable=true,IsChecked=autoDeviceSwitch};follow.Click+=(s,a)=>{autoDeviceSwitch=follow.IsChecked;SaveUi();};menu.Items.Add(follow);menu.Items.Add(new Separator());foreach(var p in DeviceProfiles.Factory())Item(p.Name,()=>ApplyUserPreset(p.Copy()));
  Item("手机双端设置同步…",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!,"--mobile-sync"){UseShellExecute=true}));
 Item("打开预设文件夹",()=>{try{var dir=Path.Combine(directory,"presets");Directory.CreateDirectory(dir);System.Diagnostics.Process.Start("explorer.exe",dir);}catch(Exception ex){SetStatus("无法打开目录",ex.Message,true);}});
  var path=Path.Combine(directory,"presets");if(Directory.Exists(path)){menu.Items.Add(new Separator());foreach(var file in Directory.EnumerateFiles(path,"*.json").Take(100))try{var p=Preset.Parse(File.ReadAllText(file));Item(p.Name,()=>ApplyUserPreset(p.Copy()));}catch{}}menu.PlacementTarget=PresetButton;menu.IsOpen=true;}
 void RatePolicyChanged(object sender,RoutedEventArgs e){if(!updating)SaveUi();}
 void DsdChanged(object sender,SelectionChangedEventArgs e){if(updating)return;SetDsdMode(DsdBox.SelectedIndex==1?Dsd.Dop64:DsdBox.SelectedIndex==2?Dsd.Dop128:Dsd.Off);}
 void SetDsdMode(string mode){if(mode==dsdMode)return;if(Processing&&Exclusive.IsChecked!=true&&mode!=Dsd.Off){SyncRack();SetStatus("需要 USB 独占","DoP 不能通过 Windows 共享混音输出；当前 PCM 播放保持运行",true);return;}string previous=dsdMode;bool restart=Processing;dsdMode=mode;SyncRack();SaveUi();if(restart){StopEngine();StartStopClick(this,new RoutedEventArgs());if(!Processing){string failure=RouteStatus.Text;dsdMode=previous;SyncRack();SaveUi();StartStopClick(this,new RoutedEventArgs());SetStatus(Processing?"切换失败，已恢复原输出":"输出恢复失败",failure,true);}}}
 void InitRackFields(){
  OutGain.Changed+=v=>{if(!updating){preset.Output.Gain=v;Changed();}};OutBalance.Changed+=v=>{if(!updating){preset.Output.Balance=v;Changed();}};
  OutDelayL.Changed+=v=>{if(!updating){preset.Output.LeftDelay=v;Changed();}};OutDelayR.Changed+=v=>{if(!updating){preset.Output.RightDelay=v;Changed();}};OutCeiling.Changed+=v=>{if(!updating){preset.Output.Ceiling=v;Changed();}};
  ChkInvL.Click+=(s,e)=>{if(!updating){preset.Output.InvertLeft=ChkInvL.IsChecked==true;Changed();}};ChkInvR.Click+=(s,e)=>{if(!updating){preset.Output.InvertRight=ChkInvR.IsChecked==true;Changed();}};ChkProtect.Click+=(s,e)=>{if(!updating){preset.Output.Protection=ChkProtect.IsChecked==true;Changed();}};
  CompOn.Click+=(s,e)=>{if(!updating){(preset.Output.Compressor??=new CompConfig()).Enabled=CompOn.IsChecked==true;Changed();}};
  CompThr.Changed+=v=>{if(!updating){(preset.Output.Compressor??=new CompConfig()).ThresholdDb=v;Changed();}};CompRatio.Changed+=v=>{if(!updating){(preset.Output.Compressor??=new CompConfig()).Ratio=v;Changed();}};
  CompAtk.Changed+=v=>{if(!updating){(preset.Output.Compressor??=new CompConfig()).AttackMs=v;Changed();}};CompRel.Changed+=v=>{if(!updating){(preset.Output.Compressor??=new CompConfig()).ReleaseMs=v;Changed();}};
  CompKnee.Changed+=v=>{if(!updating){(preset.Output.Compressor??=new CompConfig()).KneeDb=v;Changed();}};CompMakeup.Changed+=v=>{if(!updating){(preset.Output.Compressor??=new CompConfig()).MakeupDb=v;Changed();}};
  MatLL.Changed+=v=>{if(!updating){(preset.Output.Matrix??=new MatrixConfig()).Ll=v;Changed();}};MatLR.Changed+=v=>{if(!updating){(preset.Output.Matrix??=new MatrixConfig()).Lr=v;Changed();}};
  MatRL.Changed+=v=>{if(!updating){(preset.Output.Matrix??=new MatrixConfig()).Rl=v;Changed();}};MatRR.Changed+=v=>{if(!updating){(preset.Output.Matrix??=new MatrixConfig()).Rr=v;Changed();}};
  WidthBox.Changed+=v=>{if(!updating){(preset.Output.Matrix??=new MatrixConfig()).Width=v;Changed();}};
  DcOn.Click+=(s,e)=>{if(!updating){preset.Output.DcBlock=DcOn.IsChecked==true;Changed();}};
 }
 void SyncRack(){bool prev=updating;updating=true;try{var o=preset.Output;var comp=o.Compressor??new CompConfig();var m=o.Matrix??new MatrixConfig();
  OutGain.SetSilently(o.Gain);OutBalance.SetSilently(o.Balance);OutDelayL.SetSilently(o.LeftDelay);OutDelayR.SetSilently(o.RightDelay);OutCeiling.SetSilently(o.Ceiling);
  ChkInvL.IsChecked=o.InvertLeft;ChkInvR.IsChecked=o.InvertRight;ChkProtect.IsChecked=o.Protection;
  CompOn.IsChecked=comp.Enabled;CompThr.SetSilently(comp.ThresholdDb);CompRatio.SetSilently(comp.Ratio);CompAtk.SetSilently(comp.AttackMs);CompRel.SetSilently(comp.ReleaseMs);CompKnee.SetSilently(comp.KneeDb);CompMakeup.SetSilently(comp.MakeupDb);
  MatLL.SetSilently(m.Ll);MatLR.SetSilently(m.Lr);MatRL.SetSilently(m.Rl);MatRR.SetSilently(m.Rr);WidthBox.SetSilently(m.Width);DcOn.IsChecked=o.DcBlock;
  DsdBox.SelectedIndex=dsdMode==Dsd.Dop64?1:dsdMode==Dsd.Dop128?2:0;
  SyncSpatial();
 }finally{updating=prev;}}
 int ControlSampleRate=>engine.Running?engine.ProcessingSampleRate:nativeApo is {Rate:>0}?checked((int)nativeApo.Rate):SampleRate.SelectedItem is int rate?rate:Graph.SampleRate;
 string ControlContext=>$"{selection.OutputId}|{ControlSampleRate}|{dsdMode}|{nativeApo!=null}|{Processing}|{tuningContextVersion}";
 void ApplyControlPreset(Preset candidate,bool remember=true){
  candidate.Validate();
  if(nativeApo!=null)NativeApo.EnsureSupported(candidate);
  string previous=preset.Encode();
  preset=candidate;selected=Math.Clamp(selected,0,Math.Max(0,preset.Bands.Count-1));
  RefreshPreset(false);Changed(); // one prepared DSP swap for the entire proposal
  if(remember){if(undo.Count>=100)undo.RemoveAt(0);undo.Add(previous);}
 }
 // Serialize snapshot, validation, commit and rollback as one UI transaction.
 // A failed request must not restore a snapshot taken before another request.
 string HandleControl(string json){
  using var document=JsonDocument.Parse(json);string? command=document.RootElement.GetProperty("cmd").GetString();
  if(command is "preview_target" or "apply_curve")return HandleTargetRequest(document.RootElement,command=="apply_curve");
  return Dispatcher.Invoke(()=>HandleControlCore(json));
 }
 internal object CheckAiControl(){
  if(!preview)throw new InvalidOperationException("AI control checks require an isolated preview window");
  var cases=new List<string>();
  void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
  JsonElement Call(object command){using var d=JsonDocument.Parse(HandleControl(JsonSerializer.Serialize(command)));return d.RootElement.Clone();}
  string original=preset.Encode();
  var initial=Call(new{cmd="get_preset"});
  var candidate=Preset.Blank();candidate.Bands.Add(new Band{Frequency=1000,Gain=6});
  var analysis=Call(new{cmd="preview_preset",preset=candidate});
  Check(preset.Encode()==original&&undo.Count==0,"preview_does_not_change_ui_or_undo_history");
  var applied=Call(new{cmd="apply_preset",preset=analysis.GetProperty("preset"),expected_revision=analysis.GetProperty("base_revision").GetString()});
  Check(preset.Bands.Count==1&&preset.Preamp<=-7&&undo.Count==1,"batch_apply_is_one_undoable_edit");
  string after=preset.Encode();bool rejected=false;
  try{Call(new{cmd="apply_preset",preset=candidate,expected_revision=initial.GetProperty("revision").GetString()});}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&preset.Encode()==after&&undo.Count==1,"stale_apply_retains_current_state_and_undo");
  Call(new{cmd="undo_preset",expected_revision=applied.GetProperty("revision").GetString()});
  Check(preset.Encode()==original&&undo.Count==0,"undo_restores_complete_original_preset");
  rejected=false;try{Call(new{cmd="set_preamp",db=100});}catch(ArgumentException){rejected=true;}
  Check(rejected&&preset.Encode()==original,"invalid_legacy_command_rolls_back");
  var response=Call(new{cmd="analyze_response"});
  Check(response.GetProperty("points").GetArrayLength()==97&&preset.Encode()==original,"response_analysis_is_read_only");
  rejected=false;try{Call(new{cmd="get_meter_snapshot"});}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&preset.Encode()==original,"inactive_meter_never_claims_playback_or_changes_settings");
  var changedOutput=preset.Copy();changedOutput.Output.Matrix.Width=1.4;changedOutput.Output.Gain=-8;
  ApplyControlPreset(changedOutput);var resetBefore=Call(new{cmd="get_preset"});string resetOriginal=preset.Encode();int undoBefore=undo.Count;
  var reset=Call(new{cmd="reset_output",section="matrix",expected_revision=resetBefore.GetProperty("revision").GetString()});
  Check(preset.Output.Matrix.Width==1&&preset.Output.Gain==-8&&undo.Count==undoBefore+1,"module_reset_is_one_scoped_undoable_transaction");
  Call(new{cmd="undo_preset",expected_revision=reset.GetProperty("revision").GetString()});
  Check(preset.Encode()==resetOriginal,"module_reset_undo_restores_previous_values");
  var catalog=Call(new{cmd="list_targets"});
  Check(catalog.GetProperty("targets").GetArrayLength()>=30,"ai_can_discover_target_metadata");
  var targetPreview=Call(new{cmd="preview_target",target_id="preference-7",band_count=12});
  Check(preset.Encode()==resetOriginal&&targetPreview.GetProperty("preset").GetProperty("preamp_db").GetDouble()==preset.Preamp,"target_preview_is_read_only_and_preserves_preamp");
  var targetApply=Call(new{cmd="apply_preset",preset=targetPreview.GetProperty("preset"),expected_revision=targetPreview.GetProperty("base_revision").GetString(),auto_headroom=false});
  Check(preset.Output.Gain==-8&&preset.Output.Matrix.Width==1.4,"target_apply_preserves_unrelated_rack_settings");
  Call(new{cmd="undo_preset",expected_revision=targetApply.GetProperty("revision").GetString()});
  Check(preset.Encode()==resetOriginal,"target_apply_is_fully_undoable");
  tuningContextVersion++;rejected=false;
  try{Call(new{cmd="apply_preset",preset=targetPreview.GetProperty("preset"),expected_revision=targetPreview.GetProperty("base_revision").GetString(),auto_headroom=false});}catch(InvalidOperationException){rejected=true;}
  Check(rejected&&preset.Encode()==resetOriginal,"changed_measurement_context_invalidates_old_target_proposal");
  var spatialBefore=Call(new{cmd="get_preset"});string mainBefore=JsonSerializer.Serialize(preset.Bands),outputBefore=preset.Encode();int spatialUndo=undo.Count;
  var scene=Call(new{cmd="apply_spatial_scene",scene_id="wide",expected_revision=spatialBefore.GetProperty("revision").GetString()});
  Check(preset.Output.Spatial.Enabled&&preset.Output.Spatial.HighWidth==1.65&&JsonSerializer.Serialize(preset.Bands)==mainBefore&&undo.Count==spatialUndo+1,"spatial_scene_is_one_edit_and_keeps_main_eq");
  Call(new{cmd="undo_preset",expected_revision=scene.GetProperty("revision").GetString()});
  Check(preset.Encode()==outputBefore,"spatial_scene_undo_restores_entire_preset");
  var invalidSpatial=new SpatialConfig{Enabled=true,Crossfeed=.9};rejected=false;
  try{Call(new{cmd="set_spatial",spatial=invalidSpatial,expected_revision=AiTuning.Revision(preset,ControlContext)});}catch(ArgumentException){rejected=true;}
  Check(rejected&&preset.Encode()==outputBefore,"invalid_spatial_configuration_does_not_change_live_state");
  Check(!Processing&&!IsVisible,"control_checks_do_not_start_audio_or_show_window");
  return new{result="PASS",cases};
 }
 string HandleControlCore(string json){
  using var doc=JsonDocument.Parse(json);var r=doc.RootElement;string cmd=r.GetProperty("cmd").GetString()??"";
  object? payload=null;var opts=new JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
  bool readOnly=cmd is "get_state" or "get_preset" or "get_preset_library" or "preview_preset" or "get_capabilities" or "get_dsd_diagnostics" or "get_audio_diagnostics" or "analyze_response" or "get_meter_snapshot" or "list_targets" or "get_target" or "get_tuning_context" or "list_spatial_scenes";
  var backup=readOnly?null:preset.Copy(); // reads never roll back a live preset
  try{
  void UI(Action a)=>Dispatcher.Invoke(a);
  switch(cmd){
   case "get_capabilities":payload=new{ok=true,capabilities=AiTuning.Capabilities()};break;
   case "list_targets":payload=new{ok=true,targets=TargetCurves.BuiltIn.Select(TargetWorkflow.Describe),notes="声学目标必须使用同体系原始测量；相对偏好不是声学校准。"};break;
   case "get_target":{var target=TargetCurves.Find(r.GetProperty("target_id").GetString()!);payload=new{ok=true,target=TargetWorkflow.Describe(target),points=Enumerable.Range(0,target.Points.GetLength(0)).Select(i=>new[]{target.Points[i,0],target.Points[i,1]}),normalization_hz=target.RequiresMeasurement?1000:0};break;}
   case "get_tuning_context":payload=new{ok=true,measurement=measuredProfile?.Select(p=>new[]{p.Hz,p.Db}),measurement_rig=SelectedMeasurementRig(),measurement_source=measurementSource,revision=AiTuning.Revision(preset,ControlContext),sample_rate=ControlSampleRate};break;
   case "reset_output":AiTuning.RequireRevision(r.GetProperty("expected_revision").GetString()!,preset,ControlContext);ApplyControlPreset(RackDefaults.Reset(preset,r.GetProperty("section").GetString()!));payload=new{ok=true,preset,revision=AiTuning.Revision(preset,ControlContext)};break;
   case "list_spatial_scenes":payload=new{ok=true,managed_path_only=true,scenes=SpatialScenes.All.Select(s=>new{id=s.Id,name=s.Name,description=s.Description,spatial=SpatialScenes.Create(s.Id)})};break;
   case "set_spatial":{
    AiTuning.RequireRevision(r.GetProperty("expected_revision").GetString()!,preset,ControlContext);
    var next=preset.Copy();next.Output.Spatial=JsonSerializer.Deserialize<SpatialConfig>(r.GetProperty("spatial").GetRawText())??throw new ArgumentException("Missing spatial configuration");
    ApplyControlPreset(next);payload=new{ok=true,preset,revision=AiTuning.Revision(preset,ControlContext)};break;
   }
   case "apply_spatial_scene":{
    AiTuning.RequireRevision(r.GetProperty("expected_revision").GetString()!,preset,ControlContext);
    var next=preset.Copy();next.Output.Spatial=SpatialScenes.Create(r.GetProperty("scene_id").GetString()!);
    ApplyControlPreset(next);payload=new{ok=true,preset,revision=AiTuning.Revision(preset,ControlContext)};break;
   }
   case "get_preset":payload=new{ok=true,preset,revision=AiTuning.Revision(preset,ControlContext),sample_rate=ControlSampleRate,sample_rate_source=Processing?"active":"selected"};break;
   case "get_preset_library":payload=new{ok=true,library=PresetLibrary.Read(directory)};break;
   case "get_meter_snapshot":
    if(!engine.Running)throw new InvalidOperationException("区间电平分析当前适用于中转/WASAPI链路；原生 APO 或停止状态没有同等计量数据。");
    payload=new{ok=true,stream_id=engine.StreamIdentity,revision=AiTuning.Revision(preset,ControlContext),sample_rate=engine.ProcessingSampleRate,frames=engine.Frames,input_left_energy=engine.EnergyInLeft,input_right_energy=engine.EnergyInRight,output_left_energy=engine.EnergyOutLeft,output_right_energy=engine.EnergyOutRight,lifetime_input_peak=engine.PeakIn,lifetime_output_peak=engine.PeakOut,capture_underflow_bytes=engine.UnderflowBytes,capture_overflows=engine.OverflowEvents,input_invalid_samples=engine.InvalidInputSamples};
    break;
   case "get_audio_diagnostics":
   case "get_dsd_diagnostics":payload=new{ok=true,dsd=dsdMode,encoder=engine.DsdDiagnostics,input_invalid_samples=engine.InvalidInputSamples,last_error=engine.LastError,verified_frames=engine.DopVerifiedFrames,invalid_frames=engine.DopInvalidFrames,queue_waits=engine.DsdQueueWaits,underruns=engine.OutputUnderruns};break;
   case "analyze_response":payload=new{ok=true,sample_rate=ControlSampleRate,revision=AiTuning.Revision(preset,ControlContext),phase_wrapped=true,points=LinearResponse.Analyze(preset,ControlSampleRate),scope="Theoretical linear EQ/DC/M-S EQ/multiband width/crossfeed/mix/matrix/balance/polarity/delay paths; excludes compressor, limiter, ASRC, DoP, buffers, DAC and headphone acoustics."};break;
   case "preview_preset":{
    var candidate=Preset.Parse(r.GetProperty("preset").GetRawText());
    var analysis=AiTuning.Preview(preset,candidate,ControlSampleRate,ControlContext,!r.TryGetProperty("auto_headroom",out var ah)||ah.GetBoolean());
    payload=new{ok=true,preset=analysis.Preset,base_revision=analysis.BaseRevision,sample_rate=analysis.SampleRate,changed_filters=analysis.ChangedFilters,suggested_preamp_db=analysis.SuggestedPreampDb,estimated_peak_gain_db=analysis.EstimatedPeakGainDb,eq_response_hz_left_right_db=analysis.EqResponse,warnings=analysis.Warnings};
    break;
   }
   case "apply_preset":{
    AiTuning.RequireRevision(r.GetProperty("expected_revision").GetString()!,preset,ControlContext);
    var candidate=Preset.Parse(r.GetProperty("preset").GetRawText());
    var analysis=AiTuning.Preview(preset,candidate,ControlSampleRate,ControlContext,!r.TryGetProperty("auto_headroom",out var ah)||ah.GetBoolean());
    ApplyControlPreset(analysis.Preset);
    payload=new{ok=true,preset,revision=AiTuning.Revision(preset,ControlContext),warnings=analysis.Warnings};
    break;
   }
   case "undo_preset":{
    AiTuning.RequireRevision(r.GetProperty("expected_revision").GetString()!,preset,ControlContext);
    if(undo.Count==0)throw new InvalidOperationException("没有可撤销的调音。");
    ApplyControlPreset(Preset.Parse(undo[^1]),false);undo.RemoveAt(undo.Count-1);
    payload=new{ok=true,preset,revision=AiTuning.Revision(preset,ControlContext)};
    break;
   }
   case "start":UI(()=>{if(!Processing)StartForTest();payload=new{ok=Processing,native=nativeApo?.Health(),format=engine.FormatLabel};});break;
   case "stop":UI(()=>{StopEngine();payload=new{ok=true};});break;
   case "set_format":UI(()=>{
    if(Processing)throw new InvalidOperationException("Stop processing before changing format");
    int rate=r.GetProperty("rate").GetInt32(),bits=r.GetProperty("bits").GetInt32();
    DeviceFormats.Require(selection.OutputId!,rate,bits);
    SampleRate.SelectedItem=rate;RefreshBitDepths();BitDepth.SelectedItem=bits;
    if(!Equals(SampleRate.SelectedItem,rate)||!Equals(BitDepth.SelectedItem,bits))throw new InvalidOperationException("Format unavailable in device selectors");
    RateMax.IsChecked=true;SaveUi();payload=new{ok=true,rate,bits};
   });break;
   case "set_transport":UI(()=>{
    if(Processing)throw new InvalidOperationException("Stop processing before changing transport");
    string outputId=r.GetProperty("output_id").GetString()!;
    if(!devices.Any(d=>d.Id==outputId))throw new ArgumentException("Output device unavailable");
    bool exclusive=r.GetProperty("exclusive").GetBoolean();
    updating=true;selection.OutputId=outputId;Exclusive.IsChecked=exclusive;preferredExclusive=exclusive;NativeMode.IsChecked=nativeMode=true;RouteDefault.IsChecked=r.TryGetProperty("route",out var route)&&route.GetBoolean();updating=false;
    RefreshDevices(true);SaveUi();payload=new{ok=true,output_id=selection.OutputId,exclusive};
   });break;
   case "get_state":UI(()=>payload=new{ok=true,name=preset.Name,enabled=preset.Enabled,preamp=preset.Preamp,bands=preset.Bands,output=preset.Output,processing=Processing,window_visible=IsVisible,runtime_initialized=runtimeInitialized,background_start=backgroundStart,native=nativeApo?.Health(),input_id=selection.InputId,output_id=selection.OutputId,output_format=engine.FormatLabel,exclusive_confirmed=engine.ExclusiveConfirmed,capture_format=engine.CaptureFormat?.ToString(),same_rate_passthrough=engine.SameRatePassthrough,dsd_carrier_bits=dsdCarrierBits,dsd=dsdMode,rate_policy=RateFollow.IsChecked==true?"follow":"target",target_overlay=Graph.Target!=null,engine=new{running=engine.Running,strict_pcm_probe=engine.StrictPcmProbe,standard_dsd_transport=engine.StandardDsdTransport,gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2),allocated_bytes=GC.GetTotalAllocatedBytes(false),output_underruns=engine.OutputUnderruns,dop_verified_frames=engine.DopVerifiedFrames,dop_invalid_frames=engine.DopInvalidFrames,dsd_queued_ms=engine.DsdQueuedMs,dsd_queue_waits=engine.DsdQueueWaits,dsd_synthesis_max_ms=engine.DsdSynthesisMaxMs,running_fill_max_ms=engine.MaxRunningFillMs,pump_gap_max_ms=engine.MaxPumpGapMs,late_events=engine.LateEvents,output_mmcss=engine.OutputMmcss,output_buffer_ms=engine.OutputBufferMs,dsp_sample_rate=engine.ProcessingSampleRate,dop_carrier_db=engine.DopCarrierDb,dop_attenuation_db=engine.DopAttenuationDb,buffer_ms=engine.BufferedMilliseconds,clock_ppm=engine.ClockPpm,rendered_bytes=engine.RenderedBytes,captured=engine.CapturedBytes,frames=engine.Frames,peak_out=engine.PeakOut,peak_in=engine.PeakIn,starved=engine.StarvedReads,underflow_bytes=engine.UnderflowBytes,max_render_read_ms=engine.MaxRenderReadMs,slow_render_reads=engine.SlowRenderReads,overflow=engine.OverflowEvents,energy_in=engine.EnergyBefore,energy_out=engine.EnergyAfter}});break;
   case "set_band":UI(()=>{int i=r.GetProperty("index").GetInt32();if(i<0||i>=preset.Bands.Count)throw new ArgumentException("band index out of range");var b=preset.Bands[i];if(r.TryGetProperty("type",out var t))b.Type=t.GetString()!;if(r.TryGetProperty("frequency_hz",out var f))b.Frequency=f.GetDouble();if(r.TryGetProperty("gain_db",out var g))b.Gain=g.GetDouble();if(r.TryGetProperty("q",out var q))b.Q=q.GetDouble();if(r.TryGetProperty("channel",out var c))b.Channel=c.GetString()!;if(r.TryGetProperty("enabled",out var e))b.Enabled=e.GetBoolean();selected=i;RefreshPreset();Changed();payload=new{ok=true};});break;
   case "add_band":UI(()=>{if(preset.Bands.Count>=32)throw new ArgumentException("最多 32 段");var b=new Band{Type=r.TryGetProperty("type",out var t)?t.GetString()!:"peak",Frequency=r.TryGetProperty("frequency_hz",out var f)?f.GetDouble():1000,Gain=r.TryGetProperty("gain_db",out var g)?g.GetDouble():0,Q=r.TryGetProperty("q",out var q)?q.GetDouble():.707,Channel=r.TryGetProperty("channel",out var c)?c.GetString()!:"both"};preset.Bands.Add(b);selected=preset.Bands.Count-1;RefreshPreset();Changed();payload=new{ok=true,index=selected};});break;
   case "remove_band":UI(()=>{int i=r.GetProperty("index").GetInt32();if(i<0||i>=preset.Bands.Count)throw new ArgumentException("band index out of range");preset.Bands.RemoveAt(i);selected=Math.Clamp(selected,0,Math.Max(0,preset.Bands.Count-1));RefreshPreset();Changed();payload=new{ok=true};});break;
   case "set_preamp":UI(()=>{preset.Preamp=r.GetProperty("db").GetDouble();RefreshPreset();Changed();payload=new{ok=true};});break;
   case "set_output":UI(()=>{var o=preset.Output;if(r.TryGetProperty("gain_db",out var a))o.Gain=a.GetDouble();if(r.TryGetProperty("balance",out var b))o.Balance=b.GetDouble();if(r.TryGetProperty("left_delay_ms",out var c))o.LeftDelay=c.GetDouble();if(r.TryGetProperty("right_delay_ms",out var d))o.RightDelay=d.GetDouble();if(r.TryGetProperty("invert_left",out var e))o.InvertLeft=e.GetBoolean();if(r.TryGetProperty("invert_right",out var f))o.InvertRight=f.GetBoolean();if(r.TryGetProperty("protection",out var g))o.Protection=g.GetBoolean();if(r.TryGetProperty("ceiling_db",out var h))o.Ceiling=h.GetDouble();RefreshPreset();Changed();payload=new{ok=true};});break;
   case "set_compressor":UI(()=>{var c=preset.Output.Compressor??=new CompConfig();if(r.TryGetProperty("enabled",out var e))c.Enabled=e.GetBoolean();if(r.TryGetProperty("threshold_db",out var a))c.ThresholdDb=a.GetDouble();if(r.TryGetProperty("ratio",out var b))c.Ratio=b.GetDouble();if(r.TryGetProperty("attack_ms",out var d))c.AttackMs=d.GetDouble();if(r.TryGetProperty("release_ms",out var f))c.ReleaseMs=f.GetDouble();if(r.TryGetProperty("knee_db",out var g))c.KneeDb=g.GetDouble();if(r.TryGetProperty("makeup_db",out var h))c.MakeupDb=h.GetDouble();RefreshPreset();Changed();payload=new{ok=true};});break;
   case "set_matrix":UI(()=>{var m=preset.Output.Matrix??=new MatrixConfig();if(r.TryGetProperty("ll",out var a))m.Ll=a.GetDouble();if(r.TryGetProperty("lr",out var b))m.Lr=b.GetDouble();if(r.TryGetProperty("rl",out var c))m.Rl=c.GetDouble();if(r.TryGetProperty("rr",out var d))m.Rr=d.GetDouble();if(r.TryGetProperty("width",out var e))m.Width=e.GetDouble();RefreshPreset();Changed();payload=new{ok=true};});break;
   case "set_dc_block":UI(()=>{preset.Output.DcBlock=r.GetProperty("enabled").GetBoolean();RefreshPreset();Changed();payload=new{ok=true};});break;
   case "set_eq_enabled":UI(()=>{preset.Enabled=r.GetProperty("enabled").GetBoolean();RefreshPreset();Changed();payload=new{ok=true};});break;
   case "set_dsd_carrier":UI(()=>{int bits=r.GetProperty("bits").GetInt32();if(bits is not (0 or 24 or 32))throw new ArgumentException("bits must be 0|24|32");if(Processing&&dsdMode!=Dsd.Off)throw new InvalidOperationException("Switch to PCM before changing DoP carrier");dsdCarrierBits=bits;SaveUi();payload=new{ok=true,dsd_carrier_bits=bits};});break;
   case "set_dsd":UI(()=>{string mode=r.GetProperty("mode").GetString()!;if(mode!="dop64"&&mode!="dop128"&&mode!="off")throw new ArgumentException("mode must be off|dop64|dop128");if(Processing&&Exclusive.IsChecked!=true&&mode!=Dsd.Off)throw new InvalidOperationException("DoP requires USB exclusive output");SetDsdMode(mode);payload=new{ok=dsdMode==mode,dsd=dsdMode,processing=Processing,status=StateTitle.Text,message=RouteStatus.Text};});break;
   case "set_standard_dsd_transport":UI(()=>{if(Processing)throw new InvalidOperationException("Stop before diagnostic transport selection");engine.StandardDsdTransport=r.GetProperty("enabled").GetBoolean();payload=new{ok=true,enabled=engine.StandardDsdTransport};});break;
   case "set_strict_pcm_probe":UI(()=>{if(Processing)throw new InvalidOperationException("Stop before diagnostic transport selection");engine.StrictPcmProbe=r.GetProperty("enabled").GetBoolean();payload=new{ok=true,enabled=engine.StrictPcmProbe};});break;
   case "set_output_timing":UI(()=>{SetPollingOutput(r.GetProperty("polling").GetBoolean());payload=new{ok=Processing,polling=pollingOutput,format=engine.FormatLabel};});break;
   case "save_preset":UI(()=>{if(r.TryGetProperty("name",out var n))preset.Name=n.GetString()!;Changed();payload=new{ok=true};});break;
   default:return "{\"ok\":false,\"error\":\"unknown cmd\"}";
  }
  return JsonSerializer.Serialize(payload??new{ok=true},opts);
  }catch{
   if(backup!=null){
    bool changed;try{changed=preset.Encode()!=backup.Encode();}catch{changed=true;}
    if(changed){preset=backup;RefreshPreset();}
   }
   throw;
  }
 }
 void Graphic31Click(object sender,RoutedEventArgs e){Remember();preset=PresetImport.Graphic31();selected=0;RefreshPreset();Changed();RefreshPresetList();}
 void RefreshPresetList(){updating=true;try{var names=new List<string>{"平直 · 原声起点"};foreach(var f in DeviceProfiles.Factory())if(!names.Contains(f.Name))names.Add(f.Name);var dir=Path.Combine(directory,"presets");if(Directory.Exists(dir))foreach(var file in Directory.EnumerateFiles(dir,"*.json"))try{var n=Preset.Parse(File.ReadAllText(file)).Name;if(!names.Contains(n))names.Add(n);}catch{}PresetPick.ItemsSource=names;PresetPick.SelectedItem=preset.Name;if(PresetPick.SelectedItem==null)PresetPick.SelectedIndex=0;}finally{updating=false;}}
 void PresetPickChanged(object sender,SelectionChangedEventArgs e){if(updating||PresetPick.SelectedItem is not string name)return;Preset candidate;
  if(name=="平直 · 原声起点"){candidate=Preset.Blank();candidate.Name=name;}
  else{var factory=DeviceProfiles.Factory().FirstOrDefault(f=>f.Name==name);if(factory!=null)candidate=factory.Copy();else{var dir=Path.Combine(directory,"presets");candidate=Directory.Exists(dir)?Directory.EnumerateFiles(dir,"*.json").Select(f=>{try{return Preset.Parse(File.ReadAllText(f));}catch{return null;}}).FirstOrDefault(p=>p?.Name==name)??Preset.Blank():Preset.Blank();candidate.Name=name;}}
  ApplyUserPreset(candidate);}
 void RenameClick(object sender,RoutedEventArgs e)=>Rename();
 bool autoStart=true;
 void AutoStartChanged(object sender,RoutedEventArgs e){if(updating)return;autoStart=AutoStartChk.IsChecked==true;ApplyAutoStart();SaveUi();}
 void ApplyAutoStart(){if(preview)return;try{using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);if(key==null)return;if(autoStart){var exe=Environment.ProcessPath??"";if(Path.GetFileNameWithoutExtension(exe).Equals("OpenDSP",StringComparison.OrdinalIgnoreCase))key.SetValue("OpenDSP",StartupRegistration.Command(exe));}else key.DeleteValue("OpenDSP",false);}catch{}}
 void Rename(){var d=Dialog("重命名预设",420,200);var text=new TextBox{Text=preset.Name,MaxLength=100,Margin=new Thickness(0,20,0,24)};var panel=(StackPanel)d.Content;panel.Children.Add(text);panel.Children.Add(DialogButton("保存",()=>{if(string.IsNullOrWhiteSpace(text.Text))return;Remember();preset.Name=text.Text.Trim();Changed();RefreshPresetList();d.Close();}));d.ShowDialog();}
 void EnsureDesktopShortcut(){try{var exe=Environment.ProcessPath??"";// 仅正式的 OpenDSP.exe 允许写快捷方式：测试经 dotnet 启动时不得劫持目标（否则图标变成 .NET）
  if(preview||!Path.GetFileNameWithoutExtension(exe).Equals("OpenDSP",StringComparison.OrdinalIgnoreCase))return;var t=Type.GetTypeFromProgID("WScript.Shell");if(t==null)return;dynamic shell=Activator.CreateInstance(t)!;string lnk=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"OpenDSP.lnk");dynamic sc=shell.CreateShortcut(lnk);sc.TargetPath=exe;sc.Arguments="--start";sc.WorkingDirectory=AppContext.BaseDirectory;sc.IconLocation=exe+",0";sc.Description="OpenDSP · 开源 HiFi DSP 套件";sc.Save();}catch{}}
 void CompareClick(object sender,RoutedEventArgs e){var menu=new ContextMenu();void Item(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(s,a)=>action();menu.Items.Add(item);}Item("将当前存为 A",()=>{compareA=preset.Encode();Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"compare-a.json"),compareA);RouteStatus.Text="已保存对比 A";});Item("将当前存为 B",()=>{compareB=preset.Encode();Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"compare-b.json"),compareB);RouteStatus.Text="已保存对比 B";});Item("试听 A",()=>LoadSlot(compareA));Item("试听 B",()=>LoadSlot(compareB));menu.PlacementTarget=(Button)sender;menu.IsOpen=true;}
 void MoreClick(object sender,RoutedEventArgs e){var menu=new ContextMenu();void Item(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(s,a)=>action();menu.Items.Add(item);}Item("自动计算防削波前级",()=>{Remember();preset.Preamp=PresetImport.Headroom(preset,Graph.SampleRate);RefreshPreset();Changed();});Item("新建 31 段图示 EQ",()=>{Remember();preset=PresetImport.Graphic31();selected=0;RefreshPreset();Changed();});Item("导入 Equalizer APO / AutoEQ 参数…",()=>{var d=new Microsoft.Win32.OpenFileDialog{Filter="EQ 参数文本|*.txt|所有文件|*.*"};if(d.ShowDialog(this)==true)try{var next=PresetImport.EqualizerApo(File.ReadAllText(d.FileName));Remember();preset=next;selected=0;RefreshPreset();Changed();}catch(Exception ex){SetStatus("参数导入失败",ex.Message,true);}});Item("恢复此设备修改前的音频格式",RestoreDeviceFormat);Item("恢复此设备原来的系统音效",RestoreDeviceApo);Item("恢复受保护音频策略",RestoreAudioPolicy);Item("恢复上次中断的音频路由",RecoverAudioRoute);
  var timing=new MenuItem{Header="USB 独占使用定时缓冲（兼容模式）",IsCheckable=true,IsChecked=pollingOutput};timing.Click+=(s,a)=>SetPollingOutput(timing.IsChecked==true);menu.Items.Add(timing);Item("重新查询声卡支持的格式",RequeryDeviceFormats);
  var background=new MenuItem{Header="关闭窗口时在托盘继续运行",IsCheckable=true,IsChecked=closeToTray};background.Click+=(s,a)=>SetCloseToTray(background.IsChecked==true);menu.Items.Add(background);Item("退出 OpenEQ",()=>{exiting=true;Close();});menu.PlacementTarget=(Button)sender;menu.IsOpen=true;}
 void RestoreDeviceFormat(){try{if(OutputDevices.SelectedItem is not DeviceOption output)return;StopEngine();NativeDeviceFormat.Restore(output.Id);RefreshOutputFormats(true);SetStatus("原始格式已恢复","已按持久备份恢复设备格式与混音格式，并回读确认；处理保持停止");}catch(Exception ex){SetStatus("格式恢复失败",ex.Message,true);}}
 void RestoreDeviceApo(){try{if(OutputDevices.SelectedItem is not DeviceOption output)return;StopEngine();NativeApo.RequestInstall(output.Id,true);SetStatus("已恢复系统音效","原生 APO 绑定已卸载，原设备音效设置已恢复");}catch(Exception ex){SetStatus("恢复失败",ex.Message,true);}}
 void RestoreAudioPolicy(){try{StopEngine();NativeApo.RequestPolicy(true);SetStatus("已恢复音频保护","已恢复安装前的 Windows 音频保护策略");}catch(Exception ex){SetStatus("恢复失败",ex.Message,true);}}
 void RecoverAudioRoute(){try{StopEngine();DefaultRoute.Recover();VolumeRoute.Recover();WindowsListen.Recover();RouteStatus.Text="已恢复可用设备的默认输出、主音量与 Windows 侦听路由";}catch(Exception ex){SetStatus("恢复失败",ex.Message,true);}}
 void RequeryDeviceFormats(){if(engine.Running){RouteStatus.Text="请先停止处理，再刷新驱动格式";return;}RefreshOutputFormats(true);}
 void SetPollingOutput(bool on){bool resume=engine.Running;if(resume)StopEngine();pollingOutput=on;SaveUi();if(resume)StartStopClick(this,new RoutedEventArgs());}
 void SetCloseToTray(bool on){closeToTray=on;SaveUi();}
 void SettingsClick(object sender,RoutedEventArgs e)=>BuildSettingsWindow().ShowDialog();
 System.Windows.Window BuildSettingsWindow(){var d=new System.Windows.Window{Title="设置",Owner=this,Width=580,Height=720,MinWidth=520,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.CanResize,Background=Brush("#FFFFFF"),FontFamily=FontFamily,FontSize=12,Resources=Resources};
  var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};var panel=new StackPanel{Margin=new Thickness(26,22,26,26)};scroll.Content=panel;d.Content=scroll;
  panel.Children.Add(new TextBlock{Text="设置",FontSize=20,FontWeight=FontWeights.SemiBold,Foreground=Brush("#24334E")});panel.Children.Add(new TextBlock{Text="此处汇总此前散落在各菜单里的开关，所有选项即时生效并自动记忆。",Foreground=Brush("#8B97AE"),FontSize=11,Margin=new Thickness(0,4,0,0),TextWrapping=TextWrapping.Wrap});
  void Group(string title){panel.Children.Add(new TextBlock{Text=title,Foreground=Brush("#8996AC"),Margin=new Thickness(0,22,0,10),FontSize=12,FontWeight=FontWeights.SemiBold});}
  CheckBox Toggle(string label,bool on,Action<bool> apply,string tip){var b=new CheckBox{Content=label,IsChecked=on,Style=(Style)FindResource("Switch"),Margin=new Thickness(0,0,0,13),FontSize=12,ToolTip=tip};b.Click+=(s,a)=>apply(b.IsChecked==true);return b;}
  Group("通用");
  panel.Children.Add(Toggle("开机自启（登录后在后台运行）",autoStart,v=>{autoStart=v;AutoStartChk.IsChecked=v;ApplyAutoStart();SaveUi();},"登录后自动开始处理并驻留托盘，不弹出主窗口；手动打开显示界面"));
  panel.Children.Add(Toggle("关闭窗口时最小化到托盘继续运行",closeToTray,SetCloseToTray,"关闭按钮只是隐藏窗口，处理不受影响"));
  panel.Children.Add(Toggle("自动记忆每个输出设备的 EQ",profiles.Enabled,v=>{profiles.Enabled=v;if(v&&selection.OutputId!=null)profiles.Save(selection.OutputId,preset);SaveUi();},"换设备自动取回该设备上次的调音"));
  panel.Children.Add(Toggle("新耳机接入时自动切换输出",autoDeviceSwitch,v=>{autoDeviceSwitch=v;SaveUi();},"检测到新的 USB / 蓝牙耳机自动接管"));
  panel.Children.Add(Toggle("接管系统默认输出（停止时恢复）",RouteDefault.IsChecked==true,v=>{RouteDefault.IsChecked=v;SaveUi();},"处理期间把 Windows 默认输出指向本链路"));
  Group("输出策略");panel.Children.Add(new TextBlock{Text="网易云等播放器若在换曲后进度继续但无声，请把播放器输出明确设为上方的系统音频入口（例如 Hi-Fi Cable Input），而非系统默认。播放器自身的独占应关闭，由 OpenDSP 统一独占 USB 输出。",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#8190A9"),FontSize=11,Margin=new Thickness(0,0,0,13)});
  var rFollow=new RadioButton{GroupName="setRate",Content="跟随入口 · 输出采用入口采样率（推荐）",FontSize=12,Margin=new Thickness(0,0,0,8),IsChecked=RateFollow.IsChecked==true};
  var rMax=new RadioButton{GroupName="setRate",Content="固定目标 · 使用所选采样率（异率时转换）",FontSize=12,Margin=new Thickness(0,0,0,13),IsChecked=RateMax.IsChecked==true};
  rFollow.Click+=(s,a)=>{RateFollow.IsChecked=true;SaveUi();};rMax.Click+=(s,a)=>{RateMax.IsChecked=true;SaveUi();};panel.Children.Add(rFollow);panel.Children.Add(rMax);
  panel.Children.Add(Toggle("USB 独占使用定时缓冲（兼容模式）",pollingOutput,SetPollingOutput,"个别声卡在事件驱动下爆音时开启；切换会重启处理链路"));
  var dsdRow=new StackPanel{Margin=new Thickness(0,0,0,13)};dsdRow.Children.Add(new TextBlock{Text="DSD 输出（USB 独占；音量在 DoP 封包前处理）",Foreground=Brush("#8996AC"),Margin=new Thickness(0,0,0,6)});
  var dsdBox=new ComboBox{Width=320,HorizontalAlignment=System.Windows.HorizontalAlignment.Left};dsdBox.ItemsSource=new[]{"PCM（原始位深）","DSD64 · DoP 176.4 kHz","DSD128 · DoP 352.8 kHz"};dsdBox.SelectedIndex=dsdMode==Dsd.Dop64?1:dsdMode==Dsd.Dop128?2:0;
  dsdBox.SelectionChanged+=(s,a)=>{if(dsdBox.SelectedIndex<0)return;SetDsdMode(dsdBox.SelectedIndex==1?Dsd.Dop64:dsdBox.SelectedIndex==2?Dsd.Dop128:Dsd.Off);};dsdRow.Children.Add(dsdBox);panel.Children.Add(dsdRow);
  var carrierBox=new ComboBox{Width=320,HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,0,0,13),ItemsSource=new[]{"DoP 载体：自动（24-bit 兼容优先）","DoP 载体：24-bit","DoP 载体：32-bit"},SelectedIndex=dsdCarrierBits==24?1:dsdCarrierBits==32?2:0};
  carrierBox.SelectionChanged+=(s,e)=>{if(Processing&&dsdMode!=Dsd.Off){carrierBox.SelectedIndex=dsdCarrierBits==24?1:dsdCarrierBits==32?2:0;return;}dsdCarrierBits=carrierBox.SelectedIndex==1?24:carrierBox.SelectedIndex==2?32:0;SaveUi();};panel.Children.Add(carrierBox);
  Group("维护与恢复");
  var tools=new WrapPanel();void Tool(string label,Action a){var b=new Button{Content=label,Margin=new Thickness(0,0,10,10)};b.Click+=(s,e2)=>a();tools.Children.Add(b);}
  Tool("导出频响 / 相位分析",ExportResponse);
  Tool("麦克风侦听设置",()=>{d.Close();ListenClick(this,new RoutedEventArgs());});Tool("重新查询声卡支持的格式",RequeryDeviceFormats);Tool("恢复设备原始音频格式",RestoreDeviceFormat);Tool("恢复设备原有系统音效",RestoreDeviceApo);Tool("恢复受保护音频策略",RestoreAudioPolicy);Tool("恢复中断的音频路由",RecoverAudioRoute);
  Tool("打开数据目录",()=>{try{System.Diagnostics.Process.Start("explorer.exe",directory);}catch(Exception ex){SetStatus("无法打开目录",ex.Message,true);}});panel.Children.Add(tools);
  panel.Children.Add(new TextBlock{Text="bilibili · Fa2517 · 开源免费",Foreground=Brush("#8B97AE"),FontSize=10,Margin=new Thickness(0,18,0,0)});return d;}
 internal object CheckSettingsUi(){void Check(bool ok,string text){if(!ok)throw new Exception("settings: "+text);}
  var d=BuildSettingsWindow();var all=new List<DependencyObject>();void Walk(DependencyObject n){all.Add(n);foreach(var c in LogicalTreeHelper.GetChildren(n).OfType<DependencyObject>())Walk(c);}Walk(d);
  var toggles=all.OfType<CheckBox>().ToList();var radios=all.OfType<RadioButton>().ToList();var combos=all.OfType<ComboBox>().ToList();var buttons=all.OfType<Button>().ToList();
  Check(d.Content is ScrollViewer{VerticalScrollBarVisibility:ScrollBarVisibility.Auto},"页面必须可滚动才看得全");
  Check(toggles.Count>=5,$"开关数量不足 {toggles.Count}");Check(radios.Count==2&&radios.All(r=>r.GroupName=="setRate"),"输出策略单选组缺失");Check(combos.Count==2,"DSD 模式或载体选择器缺失");Check(buttons.Count>=7,$"维护按钮不足 {buttons.Count}");
  var labels=toggles.Select(t=>t.Content?.ToString()??"").ToList();Check(labels.Any(l=>l.Contains("开机自启"))&&labels.Any(l=>l.Contains("托盘"))&&labels.Any(l=>l.Contains("定时缓冲"))&&labels.Any(l=>l.Contains("自动记忆"))&&labels.Any(l=>l.Contains("自动切换"))&&labels.Any(l=>l.Contains("默认输出")),"散落开关未全部汇总");
  var rMax=radios.First(r=>r.Content!.ToString()!.Contains("固定目标"));var rFollow=radios.First(r=>r.Content!.ToString()!.Contains("跟随入口"));
  Check((rMax.IsChecked==true)==(RateMax.IsChecked==true)&&(rFollow.IsChecked==true)==(RateFollow.IsChecked==true),"设置页未反映当前输出策略");
  rMax.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(RateMax.IsChecked==true,"输出策略联动失败（满血）");RateFollow.IsChecked=true;
  var dsdBox=combos[0];dsdBox.SelectedIndex=1;Check(dsdMode==Dsd.Dop64&&DsdBox.SelectedIndex==1,"DSD 联动失败（DSD64）");dsdBox.SelectedIndex=0;Check(dsdMode==Dsd.Off&&DsdBox.SelectedIndex==0,"DSD 联动失败（PCM）");
  d.Close();return new{result="PASS",scrollable=true,toggles=toggles.Count,rate_policy_wired=true,dsd_wired=true,maintenance_buttons=buttons.Count,consolidated=labels};}
 void LoadSlot(string? text){if(text==null){RouteStatus.Text="请先保存该对比槽";return;}try{ApplyUserPreset(Preset.Parse(text));}catch(Exception ex){SetStatus("对比槽未应用",ex.Message,true);}}
 System.Windows.Window Dialog(string title,double width,double height){var d=new System.Windows.Window{Title=title,Owner=this,Width=width,Height=height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Brush("#FFFFFF"),FontFamily=FontFamily,FontSize=12,Resources=Resources};var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text=title,FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=Brush("#24334E")});d.Content=panel;return d;}
 Button DialogButton(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(0,8,0,0),Style=(Style)FindResource("Primary")};b.Click+=(s,e)=>action();return b;}
 void AdvancedClick(object sender,RoutedEventArgs e){var d=Dialog("输出处理",520,950);var panel=(StackPanel)d.Content;NumberEditor Number(string label,double value,double min,double max){panel.Children.Add(new TextBlock{Text=label,Foreground=Brush("#8996AC"),Margin=new Thickness(0,14,0,6)});var n=new NumberEditor{Minimum=min,Maximum=max,Value=value,Decimals=1};panel.Children.Add(n);return n;}
  var gain=Number("输出增益 · dB",preset.Output.Gain,-60,0);var balance=Number("声道平衡 · −1 左 / +1 右",preset.Output.Balance,-1,1);var ld=Number("左声道延时 · ms",preset.Output.LeftDelay,0,100);var rd=Number("右声道延时 · ms",preset.Output.RightDelay,0,100);var ceiling=Number("峰值保护上限 · dBFS",preset.Output.Ceiling,-12,0);
  var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,18,0,8)};var il=new CheckBox{Content="左声道反相",IsChecked=preset.Output.InvertLeft,Margin=new Thickness(0,0,20,0)};var ir=new CheckBox{Content="右声道反相",IsChecked=preset.Output.InvertRight};row.Children.Add(il);row.Children.Add(ir);panel.Children.Add(row);var protect=new CheckBox{Content="启用立体声联动峰值保护",IsChecked=preset.Output.Protection};panel.Children.Add(protect);panel.Children.Add(new TextBlock{Text="sample-peak 保护，不是 true-peak 限幅器。",Foreground=Brush("#9BA7BB"),FontSize=10,Margin=new Thickness(0,8,0,10)});
  panel.Children.Add(new TextBlock{Text="DSD 输出模式（USB 独占链路；需 DAC 支持 DoP）",Foreground=Brush("#8996AC"),Margin=new Thickness(0,6,0,6)});
  var dsdBox=new ComboBox{Margin=new Thickness(0,0,0,12)};dsdBox.ItemsSource=new[]{"PCM（原始位深）","DSD64 · DoP 176.4 kHz","DSD128 · DoP 352.8 kHz"};dsdBox.SelectedIndex=dsdMode==Dsd.Dop64?1:dsdMode==Dsd.Dop128?2:0;panel.Children.Add(dsdBox);
  var compCfg=preset.Output.Compressor??new CompConfig();var mtxCfg=preset.Output.Matrix??new MatrixConfig();
  panel.Children.Add(new TextBlock{Text="动态处理 · 压缩器（立体声联动）",Foreground=Brush("#8996AC"),Margin=new Thickness(0,4,0,6)});
  var compOn=new CheckBox{Content="启用压缩器",IsChecked=compCfg.Enabled};panel.Children.Add(compOn);
  (NumberEditor,NumberEditor) Two(string la,double va,double mina,double maxa,string lb,double vb,double minb,double maxb){var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,0,2)};var c1=new StackPanel{Width=210};var c2=new StackPanel{Width=210};c1.Children.Add(new TextBlock{Text=la,Foreground=Brush("#8996AC")});var e1=new NumberEditor{Minimum=mina,Maximum=maxa,Value=va,Decimals=1};c1.Children.Add(e1);c2.Children.Add(new TextBlock{Text=lb,Foreground=Brush("#8996AC")});var e2=new NumberEditor{Minimum=minb,Maximum=maxb,Value=vb,Decimals=1};c2.Children.Add(e2);row.Children.Add(c1);row.Children.Add(c2);panel.Children.Add(row);return (e1,e2);}
  var (thr,ratio)=Two("阈值 dB",compCfg.ThresholdDb,-60,0,"压缩比",compCfg.Ratio,1,20);
  var (atk,rel)=Two("启动 ms",compCfg.AttackMs,.1,200,"释放 ms",compCfg.ReleaseMs,5,2000);
  var (knee,mk)=Two("拐点 dB",compCfg.KneeDb,0,24,"补偿 dB",compCfg.MakeupDb,-12,24);
  panel.Children.Add(new TextBlock{Text="声道矩阵与立体声宽度",Foreground=Brush("#8996AC"),Margin=new Thickness(0,8,0,6)});
  var (mll,mlr)=Two("矩阵 LL",mtxCfg.Ll,-2,2,"矩阵 LR",mtxCfg.Lr,-2,2);
  var (mrl,mrr)=Two("矩阵 RL",mtxCfg.Rl,-2,2,"矩阵 RR",mtxCfg.Rr,-2,2);
  var wd=Number("立体声宽度（0 单声道 / 1 原始 / 2 加宽）",mtxCfg.Width,0,2);
  var dcOn=new CheckBox{Content="直流滤波（20 Hz 高通，消除直流偏移）",IsChecked=preset.Output.DcBlock,Margin=new Thickness(0,8,0,8)};panel.Children.Add(dcOn);
  panel.Children.Add(new TextBlock{Text=Exclusive.IsChecked==true?"链路说明：USB 独占链路执行全部 DSP 机架与 DSD 输出。":"原生模式下 EQ / 压缩 / 矩阵 / 直流均由系统引擎内核处理；DSD 输出需开启 USB 独占输出。",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#C58A4A"),Margin=new Thickness(0,2,0,10)});
  panel.Children.Add(DialogButton("保存并应用",()=>{Remember();var o=preset.Output;o.Gain=gain.Value;o.Balance=balance.Value;o.LeftDelay=ld.Value;o.RightDelay=rd.Value;o.Ceiling=ceiling.Value;o.InvertLeft=il.IsChecked==true;o.InvertRight=ir.IsChecked==true;o.Protection=protect.IsChecked==true;
   compCfg.Enabled=compOn.IsChecked==true;compCfg.ThresholdDb=thr.Value;compCfg.Ratio=ratio.Value;compCfg.AttackMs=atk.Value;compCfg.ReleaseMs=rel.Value;compCfg.KneeDb=knee.Value;compCfg.MakeupDb=mk.Value;o.Compressor=compCfg;
   mtxCfg.Ll=mll.Value;mtxCfg.Lr=mlr.Value;mtxCfg.Rl=mrl.Value;mtxCfg.Rr=mrr.Value;mtxCfg.Width=wd.Value;o.Matrix=mtxCfg;o.DcBlock=dcOn.IsChecked==true;
   var requestedDsd=dsdBox.SelectedIndex==1?Dsd.Dop64:dsdBox.SelectedIndex==2?Dsd.Dop128:Dsd.Off;Changed();SaveUi();d.Close();SetDsdMode(requestedDsd);}));d.ShowDialog();}
 void AboutClick(object sender,RoutedEventArgs e){var d=Dialog("关于 OpenDSP",440,400);var p=(StackPanel)d.Content;var avatar=new System.Windows.Controls.Image{Source=new BitmapImage(new Uri("pack://application:,,,/Assets/fa-avatar.jpg")),Width=72,Height=72,HorizontalAlignment=System.Windows.HorizontalAlignment.Left,Margin=new Thickness(0,20,0,10)};p.Children.Add(avatar);p.Children.Add(new TextBlock{Text="作者 Fa",FontSize=18,FontWeight=FontWeights.SemiBold});p.Children.Add(new TextBlock{Text="开源 HiFi DSP 套件，目标是开源 HiFi 软件的标杆。\n\nWindows 为主，安卓随后\n版本 0.4 · 完整 DSP 套件建设中\n\n源代码与构建说明随项目提供。\n\nbilibili：Fa2517",Foreground=Brush("#8190A9"),Margin=new Thickness(0,12,0,0),TextWrapping=TextWrapping.Wrap});p.Children.Add(DialogButton("知道了",()=>d.Close()));d.ShowDialog();}
 void ListenClick(object sender,RoutedEventArgs e){try{var d=Dialog("Windows 麦克风侦听",510,450);var panel=(StackPanel)d.Content;panel.Children.Add(new TextBlock{Text="原生系统 EQ 模式直接处理物理设备，保留 Windows 的“侦听此设备”路径。仅在中转独占模式下，已启用的侦听会转入虚拟入口，停止后恢复原回放设备。",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#8B97AE"),Margin=new Thickness(0,16,0,14)});foreach(var info in WindowsListen.ReadAll()){var box=new CheckBox{Content=info.Name,IsChecked=info.Enabled,Margin=new Thickness(0,8,0,8)};box.Click+=(s,a)=>{try{WindowsListen.SetEnabled(info.Id,box.IsChecked==true);engine.RefreshWindowsListening();}catch(Exception ex){box.IsChecked=!box.IsChecked;SetStatus("侦听设置失败",ex.Message,true);}};panel.Children.Add(box);}panel.Children.Add(new TextBlock{Text="可在 Windows 录音设备属性中调整麦克风电平。",Foreground=Brush("#9BA7BB"),FontSize=11,Margin=new Thickness(0,12,0,12)});panel.Children.Add(DialogButton("完成",()=>d.Close()));d.ShowDialog();}catch(Exception ex){SetStatus("侦听检测失败",ex.Message,true);}}
 public void CapturePreview(string path){Splash.Visibility=Visibility.Collapsed;UpdateLayout();var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(path);encoder.Save(stream);}
 internal object CheckNativeUi(){void Check(bool ok,string text){if(!ok)throw new Exception(text);}var physical=devices.First(d=>d.Transport=="USB");OutputDevices.SelectedItem=physical;Exclusive.IsChecked=false;Exclusive.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  for(int n=0;n<3;n++){NativeMode.IsChecked=true;NativeMode.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(Exclusive.IsEnabled,"EQ toggle must not disable USB exclusive");Check(!InputDevices.IsEnabled&&InputDevices.Text.Contains("原生音频路径"),"native-only shows native entry label");Check(OutputDevices.Items.Cast<DeviceOption>().All(d=>d.Transport!="虚拟"),"virtual output in native-only mode");
   NativeMode.IsChecked=false;NativeMode.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(Exclusive.IsEnabled,"toggling EQ off must not disable USB exclusive");}
  Exclusive.IsChecked=true;Exclusive.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Check(InputDevices.IsEnabled,"USB exclusive shows entry selector");Check(NativeMode.IsEnabled,"USB exclusive must not disable EQ toggle");
  Exclusive.IsChecked=false;Exclusive.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));NativeMode.IsChecked=true;NativeMode.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));RouteDefault.IsChecked=false;
  Check(NativeApo.Installed(physical.Id),"Live device must already have native APO for UI test");StartForTest();Check(nativeApo!=null&&!engine.Running,"native-only start uses APO without bridge");Changed(false);StopEngine();
  return new{result="PASS",toggles_orthogonal=true,bridge_entry_selector=true,eq_available_with_exclusive=true,native_start_stop="PASS",physical_device=physical.Name};}
 internal object CheckNativeFormatDraftUi(){var device=devices.First(d=>d.Transport=="USB");OutputDevices.SelectedItem=device;var before=NativeDeviceFormat.Read(device.Id);
  Exclusive.IsChecked=false;Exclusive.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));NativeMode.IsChecked=true;NativeMode.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  var caps=formatCache[device.Id];if(!SampleRate.IsEnabled||!BitDepth.IsEnabled)throw new Exception("Native format selectors locked");SampleRate.SelectedItem=caps.Exclusive.Max(f=>f.Rate);int draft=(int)SampleRate.SelectedItem;int[] choices=((IEnumerable<int>)SampleRate.ItemsSource).ToArray();UpdateNativeGraph(44100);if((int)SampleRate.SelectedItem!=draft||!choices.SequenceEqual((IEnumerable<int>)SampleRate.ItemsSource))throw new Exception("Native telemetry overwrote the format draft");
  SetSelectors(false);if(!SampleRate.IsEnabled||!BitDepth.IsEnabled)throw new Exception("Native format selectors locked during processing");SetSelectors(true);
  Exclusive.IsChecked=true;Exclusive.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));SetSelectors(false);if(SampleRate.IsEnabled)throw new Exception("Bridge transport format must lock while processing");SetSelectors(true);Exclusive.IsChecked=false;Exclusive.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
  if(!before.SameAs(NativeDeviceFormat.Read(device.Id)))throw new Exception("Selecting draft unexpectedly changed Windows format");
  return new{result="PASS",native_draft_unlocked_while_processing=true,bridge_format_locked_while_processing=true,windows_changes_only_on_apply=true};}
 internal object CheckDeviceFormatUi(){
  void Check(bool yes,string message){if(!yes)throw new Exception(message);}
  var usb=devices.First(d=>d.Transport=="USB");OutputDevices.SelectedItem=usb;var caps=formatCache[usb.Id];int max=caps.Exclusive.Max(f=>f.Rate);SampleRate.SelectedItem=max;int maxBits=caps.Exclusive.Where(f=>f.Rate==max).Max(f=>f.Bits);BitDepth.SelectedItem=maxBits;RememberFormat();
  var other=devices.FirstOrDefault(d=>d.Id!=usb.Id&&d.Transport!="虚拟");if(other!=null){OutputDevices.SelectedItem=other;OutputDevices.SelectedItem=usb;Check((int)SampleRate.SelectedItem==max&&(int)BitDepth.SelectedItem==maxBits,"Per-device rate/bit preference was lost");}
  var rates=((IEnumerable<int>)SampleRate.ItemsSource).ToArray();Check(rates.SequenceEqual(caps.Exclusive.Select(f=>f.Rate).Distinct().Order()),"UI does not match driver queries");SampleRate.Text="8001";Check(!ValidateCustomRate(),"Unsupported custom sample rate accepted");SampleRate.SelectedItem=max;SampleRate.Text=max.ToString();
  return new{device=usb.Name,supported_rates=rates,max_rate=max,bits_at_max=caps.Exclusive.Where(f=>f.Rate==max).Select(f=>f.Bits).ToArray(),per_device_format_restore="PASS",unsupported_custom_rate="rejected",query_ms=caps.QueryMilliseconds};
 }
 public object CheckUi(){if(InputDevices.SelectedItem==null||OutputDevices.SelectedItem==null)throw new Exception("设备选择为空");var n=new NumberEditor{Minimum=-24,Maximum=24};for(int i=0;i<100;i++)n.Step(1);if(n.Value!=10)throw new Exception("0.1 步进累计误差");var model=new DeviceSelection{InputId="v",OutputId="u"};var list=new[]{new DeviceOption("v","Virtual"),new DeviceOption("u","USB")};model.Apply(list);model.Apply(new[]{list[0]});if(model.OutputAvailable(new[]{list[0]})||model.OutputId!="u")throw new Exception("拔出后选择未保留");model.Apply(new[]{list[0],list[1],new DeviceOption("new","New USB")});if(!model.OutputAvailable(list))throw new Exception("重新插入未恢复选择");return new{author="Fa",notice="本软件为开源免费项目",avatar="original provided image",framework="WPF",curve_drag=true,numeric_step=.1,device_notification_registered=monitor!=null,device_poll_interval_seconds=deviceTimer.Interval.TotalSeconds,device_selection_simulation="PASS",notifications=notificationCount,input=InputDevices.Text,output=OutputDevices.Text};}
 internal object CheckUiPerformance(){
  if(!preview)throw new InvalidOperationException("Performance checks require an isolated preview");
  runtimeInitialized=true;Splash.Visibility=Visibility.Collapsed;
  var checks=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
  preset=PresetImport.Graphic31();selected=0;RefreshPreset(false);Graph.SampleRate=384000;
  var source= Bands.ItemsSource;var unchanged=bandTiles[1];int publications=editPublications;
  for(int i=0;i<100;i++){preset.Bands[0].Frequency=30+i;QueueEdited(true,true);}
  Check(editPublications==publications,"input_burst_does_not_publish_100_dsp_updates");
  FlushEdited();Check(editPublications==publications+1&&preset.Bands[0].Frequency==129,"coalesced_edit_publishes_latest_value_once");
  Check(ReferenceEquals(source,Bands.ItemsSource)&&ReferenceEquals(unchanged,bandTiles[1]),"unmodified_band_tiles_and_collection_are_retained");
  Changed();Check(!pendingEdit&&!pendingSave&&!editTimer.IsEnabled&&!presetSaveTimer.IsEnabled,"final_commit_flushes_pending_edits");
  var resetButtons=new List<Button>();
  void FindResetButtons(DependencyObject node){if(node is Button button&&button.Tag is string section&&new[]{"output","compressor","matrix","spatial","all"}.Contains(section))resetButtons.Add(button);foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())FindResetButtons(child);}
  FindResetButtons(this);Check(resetButtons.Count==5,"all_five_reset_buttons_are_present");
  foreach(var button in resetButtons){
   preset.Output.Gain=-8;preset.Output.Compressor.Enabled=true;preset.Output.Matrix.Width=1.4;preset.Output.DcBlock=true;preset.Output.Spatial=new(){Enabled=true,HighWidth=1.5};RefreshPreset(false);
   string beforeReset=preset.Encode(),expectedReset=RackDefaults.Reset(preset,button.Tag.ToString()!).Encode();int history=undo.Count;
   button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
   Check(preset.Encode()==expectedReset&&undo.Count==history+1,"reset_button_"+button.Tag+"_updates_the_model_once");
   UndoClick(this,new RoutedEventArgs());Check(preset.Encode()==beforeReset,"reset_button_"+button.Tag+"_can_be_undone");
  }
  string spatialMain=JsonSerializer.Serialize(preset.Bands);SpatialScenePick.SelectedItem=SpatialScenes.All.First(s=>s.Id=="wide");
  SpatialSceneClick(this,new RoutedEventArgs());Check(preset.Output.Spatial.HighWidth==1.65&&SpatialEnabled.IsChecked==true&&JsonSerializer.Serialize(preset.Bands)==spatialMain,"spatial_scene_controls_sync_without_changing_main_eq");
  SpatialBypassClick(this,new RoutedEventArgs());Check(!preset.Output.Spatial.Enabled&&preset.Output.Spatial.HighWidth==1.65,"spatial_bypass_preserves_editable_parameters");
  string beforeBadPreset=preset.Encode();var badPreset=preset.Copy();badPreset.Output.Spatial.Crossfeed=.9;
  Check(!ApplyUserPreset(badPreset)&&preset.Encode()==beforeBadPreset,"preset_ui_rejects_invalid_advanced_settings_without_losing_current_state");
  var knob=new Knob{Minimum=-60,Maximum=0};knob.Value=-6;
  Check(knob.Value==-6,"knob_property_updates_backing_value");
  int notifications=0;knob.Changed+=_=>notifications++;
  knob.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=Mouse.MouseWheelEvent});
  Check(Math.Abs(knob.Value+5.4)<1e-9&&notifications==1,"knob_wheel_updates_value_and_notifies_once");
  var integerStep=new NumberEditor{Minimum=0,Maximum=10000,Decimals=0,Value=180};integerStep.Step(1);
  var fineStep=new NumberEditor{Minimum=0,Maximum=.8,Decimals=2,Value=.25};fineStep.Step(1);
  Check(integerStep.Value==181&&Math.Abs(fineStep.Value-.26)<1e-12,"spatial_numeric_steps_match_displayed_precision");
  var cache=new EqPlotCache();preset.Bands[1].Gain=4;preset.Bands[1].Channel="right";preset.Bands[2].Gain=-3;preset.Bands[2].Channel="left";
  cache.Update(preset,384000);double error=0;
  for(int i=0;i<EqPlotCache.Frequencies.Length;i++)foreach(bool left in new[]{true,false}){
   double expected=preset.Preamp;
   foreach(var b in preset.Bands.Where(b=>b.Enabled&&(b.Channel=="both"||b.Channel==(left?"left":"right"))))
    expected+=20*Math.Log10(Math.Max(1e-12,new Biquad(b,384000).Magnitude(EqPlotCache.Frequencies[i],384000)));
   error=Math.Max(error,Math.Abs(expected-(left?cache.LeftDb[i]:cache.RightDb[i])));
  }
  Check(error<1e-8,"cached_curve_matches_independent_biquad_response_on_both_channels");
  int builds=cache.Rebuilds;Check(!cache.Update(preset,384000)&&cache.Rebuilds==builds,"unchanged_response_is_reused");
  preset.Bands[1].Gain++;Check(cache.Update(preset,384000),"in_place_filter_edits_invalidate_response");
  preset.Enabled=false;cache.Update(preset,384000);
  Check(cache.LeftDb.All(v=>v==0)&&cache.RightDb.All(v=>v==0)&&cache.NodeDb.All(v=>v==0),"bypass_flattens_curves_and_draggable_nodes");
  preset.Enabled=true;Check(cache.Update(preset,48000),"sample_rate_changes_invalidate_response");
  Graph.Measure(new System.Windows.Size(900,400));Graph.Arrange(new Rect(0,0,900,400));
  var visual=new DrawingVisual();using(var dc=visual.RenderOpen())Graph.RenderForChecks(dc);
  int geometry=Graph.GeometryRebuilds,grid=Graph.GridRebuilds,response=Graph.ResponseRebuilds;
  Graph.Selected=1;using(var dc=visual.RenderOpen())Graph.RenderForChecks(dc);
  Check(Graph.GeometryRebuilds==geometry&&Graph.GridRebuilds==grid&&Graph.ResponseRebuilds==response,"selection_reuses_grid_and_response_geometry");
  Graph.Arrange(new Rect(0,0,1000,450));using(var dc=visual.RenderOpen())Graph.RenderForChecks(dc);
  Check(Graph.GeometryRebuilds==geometry+1&&Graph.GridRebuilds==grid+1&&Graph.ResponseRebuilds==response,"resize_rebuilds_geometry_without_recomputing_frequency_response");
  new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
  var root=(FrameworkElement)Content;var layoutWatch=System.Diagnostics.Stopwatch.StartNew();
  for(int i=0;i<12;i++){var size=new System.Windows.Size(1180+i*10,820+i*5);root.Measure(size);root.Arrange(new Rect(size));}
  layoutWatch.Stop();
  var settings=CheckSettingsUi();
  Check(!Processing&&!IsVisible,"whole_window_layout_and_settings_do_not_start_audio_or_show_private_data");
  return new{checks,response_error_db=error,whole_window_resize_iterations=12,whole_window_layout_ms=layoutWatch.Elapsed.TotalMilliseconds,settings};
 }
 internal async Task<object> CheckBackgroundPolling(){
  if(!preview)throw new InvalidOperationException("Polling checks require an isolated preview");
  using var release=new ManualResetEventSlim(false);
  int calls=0,worker=0,uiThread=Environment.CurrentManagedThreadId,applied=deviceSnapshotsApplied;
  bool heartbeat=false;
  deviceSnapshotOverride=()=>{Interlocked.Increment(ref calls);worker=Environment.CurrentManagedThreadId;release.Wait();return new(new(),null,null,"fake");};
  try{
   QueueDeviceRefresh();_=Dispatcher.BeginInvoke(new Action(()=>heartbeat=true));
   await Task.Delay(60);
   if(!heartbeat||worker==uiThread||calls!=1)throw new Exception("Device query blocked the UI dispatcher");
   for(int i=0;i<20;i++)QueueDeviceRefresh();
   if(calls!=1)throw new Exception("Overlapping polls were not coalesced");
   QueueDeviceRefresh(true);release.Set();
   for(int i=0;i<100&&deviceScanRunning;i++)await Task.Delay(10);
   if(deviceScanRunning||calls!=2||deviceSnapshotsApplied!=applied+1)throw new Exception("Stale device snapshot was applied or replacement was lost");
   return new{result="PASS",ui_heartbeat_while_driver_blocked=heartbeat,background_queries=calls,applied_snapshots=deviceSnapshotsApplied-applied,stale_snapshot_discarded=true};
  }finally{release.Set();deviceSnapshotOverride=null;}
 }
 sealed record BandTile(int Index,string Caption,string Frequency);
 sealed class UiSettings{public string? Input{get;set;}public string? Output{get;set;}public int Rate{get;set;}=48000;public int Bits{get;set;}=24;public bool Exclusive{get;set;}=true;public bool Route{get;set;}=true;public bool AutoProfiles{get;set;}=true;public bool AutoSwitch{get;set;}=true;public bool CloseToTray{get;set;}=true;public bool PollingOutput{get;set;}=true;public bool NativeMode{get;set;}=true;public int DsdCarrierBits{get;set;}public string DsdMode{get;set;}="off";public bool AutoStart{get;set;}=true;public string RatePolicy{get;set;}="follow";public Dictionary<string,FormatPreference>? Formats{get;set;}}
}
