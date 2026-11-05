using System.Text.Json;
namespace OpenEQ;
internal static class DesktopHost {
 public static int Run(string[] args){const string quitName="Local\\OpenEQ.Fa.Quit",activateName="Local\\OpenEQ.Fa.Activate";
  if(args.Length>0&&args[0]=="--shutdown"){try{using var signal=EventWaitHandle.OpenExisting(quitName);signal.Set();}catch(WaitHandleCannotBeOpenedException){}return 0;}
  bool preview=args.Length>0&&args[0]=="--preview";using var gate=preview?null:new Mutex(true,"Local\\OpenEQ.Fa.Desktop",out _);
  bool activateNew=true;using var activate=preview?null:new EventWaitHandle(false,EventResetMode.AutoReset,activateName,out activateNew);
  bool background=args.Contains("--background");
  if(!preview&&!activateNew){if(!background)activate!.Set();return 0;}
  using var quit=preview?null:new EventWaitHandle(false,EventResetMode.AutoReset,quitName);
  var app=new System.Windows.Application();var window=new ModernWindow(preview,backgroundStart:background);
  AppDomain.CurrentDomain.UnhandledException+=(_,e)=>LogCrash("AppDomain",e.ExceptionObject as Exception);
  app.DispatcherUnhandledException+=(_,e)=>{LogCrash("Dispatcher",e.Exception);e.Handled=true;};
  System.Threading.Tasks.TaskScheduler.UnobservedTaskException+=(_,e)=>LogCrash("Task",e.Exception);
  void Initialize(){window.InitializeRuntime();if(args.Contains("--start"))window.Dispatcher.BeginInvoke(new Action(()=>{try{window.StartForTest();}catch(Exception ex){LogCrash("Start",ex);}}),System.Windows.Threading.DispatcherPriority.Background);}
  if(background)app.Startup+=(_,_)=>Initialize();else window.Loaded+=(_,_)=>Initialize();
  RegisteredWaitHandle? q=null,a=null;
  if(!preview){q=ThreadPool.RegisterWaitForSingleObject(quit!,(_,_)=>app.Dispatcher.BeginInvoke(new Action(window.RequestExit)),null,Timeout.Infinite,false);a=ThreadPool.RegisterWaitForSingleObject(activate!,(_,_)=>app.Dispatcher.BeginInvoke(new Action(window.ShowFromTray)),null,Timeout.Infinite,false);}
  if(preview){var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(args.Length>2?int.Parse(args[2]):1100)};timer.Tick+=(s,e)=>{timer.Stop();try{File.WriteAllText(args[1]+".state.json",JsonSerializer.Serialize(window.CheckUi(),new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));window.CapturePreview(args[1]);}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());}finally{window.Close();}};window.Loaded+=(s,e)=>timer.Start();}
  app.MainWindow=window;
  try{if(background)app.Run();else app.Run(window);return 0;}finally{q?.Unregister(null);a?.Unregister(null);}
 }
 static void LogCrash(string source,Exception? e){try{var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ");Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,"errors.log"),$"{DateTimeOffset.Now:O} [{source}] {e?.ToString()??"unknown"}{Environment.NewLine}");}catch{}}
}
