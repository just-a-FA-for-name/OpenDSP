using System.Text.Json;
using System.Windows.Threading;
namespace OpenEQ;
internal static class DeviceCycleTest {
 public static int Run(string path){var app=new System.Windows.Application();var window=new ModernWindow(true);var report=new Dictionary<string,object>();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1500)};int phase=0;string? endpoint=null;bool restored=false;int result=1;
  void Finish(Exception? error){timer.Stop();try{if(endpoint!=null)AudioEndpointControl.Visibility(endpoint,true);restored=true;}catch(Exception ex){report["visibility_restore_error"]=ex.ToString();}window.RequestExit();if(error!=null){report["result"]="FAIL";report["exception"]=error.ToString();}report["endpoint_visibility_restored"]=restored;File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}
  timer.Tick+=(s,e)=>{try{switch(phase++){
   case 0:window.StartForTest();endpoint=window.SelectedOutputId;report["before"]=window.DeviceHealth();AudioEndpointControl.Visibility(endpoint!,false);break;
   case 1:report["removed"]=window.DeviceHealth();if(window.Processing)throw new Exception("Output continued after endpoint was deactivated");AudioEndpointControl.Visibility(endpoint!,true);timer.Interval=TimeSpan.FromMilliseconds(3000);break;
   case 2:report["reconnected"]=window.DeviceHealth();if(!window.Processing)throw new Exception("Output did not resume after endpoint reactivation");report["result"]="PASS";report["scope"]="Real Windows endpoint deactivation/reactivation notifications; not a physical USB cable test.";result=0;Finish(null);break;
  }}catch(Exception ex){Finish(ex);}};window.Loaded+=(s,e)=>timer.Start();try{app.Run(window);}finally{if(endpoint!=null&&!restored)try{AudioEndpointControl.Visibility(endpoint,true);}catch{}}return result;
 }
}
