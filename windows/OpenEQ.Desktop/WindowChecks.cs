using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
namespace OpenEQ;
internal static class WindowChecks {
 public static int RunPerformance(string path){
  System.Windows.Media.RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
  var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
  app.Startup+=async(s,e)=>{
   ModernWindow? window=null;
   try{
    window=new ModernWindow(true,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"ui-performance-isolated"));
    var ui=window.CheckUiPerformance();var polling=await window.CheckBackgroundPolling();
    File.WriteAllText(path,JsonSerializer.Serialize(new{result="PASS",ui,polling,scope="Hidden isolated window, full layout/settings interaction, fake blocked device query, no playback"},new JsonSerializerOptions{WriteIndented=true}));result=0;
   }catch(Exception ex){File.WriteAllText(path,JsonSerializer.Serialize(new{result="FAIL",exception=ex.ToString()}));}
   finally{window?.RequestExit();app.Shutdown();}
  };
  app.Run();return result;
 }
 public static int RunAi(string path){
  System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
  ModernWindow? window=null;
  try{
   window=new ModernWindow(true,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"ai-control-isolated"));
   File.WriteAllText(path,JsonSerializer.Serialize(new{result="PASS",checks=window.CheckAiControl(),scope="Isolated, hidden preview window; no devices, playback or user configuration writes"},new JsonSerializerOptions{WriteIndented=true}));
   return 0;
  }catch(Exception ex){File.WriteAllText(path,JsonSerializer.Serialize(new{result="FAIL",exception=ex.ToString()}));return 1;}
  finally{window?.RequestExit();}
 }
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
 public static object Caption(ModernWindow window){var handle=new WindowInteropHelper(window).Handle;
  int Hit(FrameworkElement element,double x,double y){var p=element.PointToScreen(new System.Windows.Point(x,y));long location=((long)(ushort)(short)p.Y<<16)|(ushort)(short)p.X;return SendMessage(handle,0x84,IntPtr.Zero,(IntPtr)location).ToInt32();}
  var header=(FrameworkElement)window.FindName("HeaderBar");var actions=(FrameworkElement)window.FindName("HeaderActions");int blank=Hit(header,header.ActualWidth*.5,header.ActualHeight*.5),buttons=Hit(actions,15,actions.ActualHeight*.5);
  if(blank!=2||buttons!=1)throw new Exception($"Native caption hit test failed: blank={blank}, buttons={buttons}");return new{blank_titlebar_hit=blank,header_controls_hit=buttons,native_drag_and_double_click="standard Windows caption handling",scope="Real WM_NCHITTEST queries; does not inject mouse input."};
 }
 public static int Run(string path,bool native=false){var app=new System.Windows.Application();var window=new ModernWindow(true);int result=1;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1400)};timer.Tick+=(s,e)=>{timer.Stop();try{var formats=window.CheckDeviceFormatUi();var nativeFormat=window.CheckNativeFormatDraftUi();var settings=window.CheckSettingsUi();var nativeUi=native?window.CheckNativeUi():null;File.WriteAllText(path,JsonSerializer.Serialize(new{result="PASS",caption=Caption(window),formats,native_format_draft=nativeFormat,settings,native_ui=nativeUi,ui=window.CheckUi()},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));if(native)window.CapturePreview(path+".png");result=0;}catch(Exception ex){File.WriteAllText(path,JsonSerializer.Serialize(new{result="FAIL",exception=ex.ToString()}));}finally{window.RequestExit();}};window.Loaded+=(s,e)=>timer.Start();app.Run(window);return result;}
}
