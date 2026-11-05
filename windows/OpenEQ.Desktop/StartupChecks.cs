using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
namespace OpenEQ;
internal static class StartupChecks {
 public static int Run(string path){
  var app=new System.Windows.Application();var window=new ModernWindow(true,backgroundStart:true);app.MainWindow=window;int result=1;
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
  app.Startup+=(_,_)=>{window.InitializeRuntime();timer.Start();};
  timer.Tick+=(_,_)=>{timer.Stop();try{
   void Check(bool pass,string label){if(!pass)throw new Exception(label);}
   Check(!window.IsVisible,"Background startup must never show a window");
   Check(window.RuntimeInitialized,"Hidden startup must initialize device monitoring and timers");
   Check(((FrameworkElement)window.FindName("Splash")).Visibility==Visibility.Collapsed,"Background startup must suppress splash");
   Check(StartupRegistration.Command(@"C:\Apps With Spaces\OpenDSP.exe")=="\"C:\\Apps With Spaces\\OpenDSP.exe\" --start --background","Quoted startup command");
   window.ShowFromTray();Check(window.IsVisible,"Manual activation must show the existing window");
   window.Hide();Check(!window.IsVisible,"Window must hide again");
   var recovery=RecoveryChecks.Run();
   File.WriteAllText(path,JsonSerializer.Serialize(new{result="PASS",hidden_initialized=true,splash_suppressed=true,manual_activation=true,quoted_startup_command=true,recovery,scope="Isolated WPF lifecycle and recovery; no audio processing or registry writes"},new JsonSerializerOptions{WriteIndented=true}));result=0;
  }catch(Exception ex){File.WriteAllText(path,JsonSerializer.Serialize(new{result="FAIL",exception=ex.ToString()}));}finally{window.RequestExit();}};
  app.Run();return result;
 }
}
