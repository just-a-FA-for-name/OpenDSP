using System.Text.Json;
using System.Windows.Threading;
namespace OpenEQ;
internal static class ProfileSwitchTest {
 public static int Run(string path){var app=new System.Windows.Application();string state=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"ui-profile-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(state);var window=new ModernWindow(false,state);int result=1;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1600)};timer.Tick+=(s,e)=>{timer.Stop();try{var report=window.CheckRealDeviceProfiles();File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));result=0;}catch(Exception ex){File.WriteAllText(path,JsonSerializer.Serialize(new{result="FAIL",exception=ex.ToString()}));}finally{window.RequestExit();}};window.Loaded+=(s,e)=>timer.Start();app.Run(window);return result;}
}
