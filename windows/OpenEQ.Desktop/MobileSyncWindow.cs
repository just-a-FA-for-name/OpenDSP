using System.IO;
using System.Windows;
using System.Windows.Controls;
using Application=System.Windows.Application;
using Button=System.Windows.Controls.Button;
using Clipboard=System.Windows.Clipboard;
namespace OpenEQ;
internal static class MobileSyncWindow {
 public static int Run(string[] args){
  using var server=new MobileSyncServer();var app=new Application();var window=new Window{Title="OpenDSP · 手机设置同步",Width=560,Height=430,WindowStartupLocation=WindowStartupLocation.CenterScreen};
  var panel=new StackPanel{Margin=new Thickness(24)};window.Content=panel;
  panel.Children.Add(new TextBlock{Text="PC ↔ Android 设置同步",FontSize=23,Margin=new Thickness(0,0,0,16)});
  panel.Children.Add(new TextBlock{Text="仅同步通用调音预设，不同步播放设备、系统音量、授权或开始播放。\n手机通过已授权ADB通道连接；服务只监听本机回环地址。\n两端同时修改时停止自动覆盖，交由你选择保留哪端。",TextWrapping=TextWrapping.Wrap});
  panel.Children.Add(new TextBlock{Text=$"连接命令：adb -s 手机设备 reverse tcp:{server.Port} tcp:{server.Port}",Margin=new Thickness(0,16,0,12),TextWrapping=TextWrapping.Wrap});
  var save=new Button{Content="导出本次配对文件…",Padding=new Thickness(10),Margin=new Thickness(0,4,0,4)};
  save.Click+=(_,_)=>{var dialog=new Microsoft.Win32.SaveFileDialog{FileName="OpenDSP-sync-pair.json",Filter="配对文件|*.json"};if(dialog.ShowDialog(window)==true)File.WriteAllText(dialog.FileName,server.PairingJson());};panel.Children.Add(save);
  var copy=new Button{Content="复制本次配对配置",Padding=new Thickness(10),Margin=new Thickness(0,4,0,12)};copy.Click+=(_,_)=>Clipboard.SetText(server.PairingJson());panel.Children.Add(copy);
  panel.Children.Add(new TextBlock{Text="配对文件含临时访问令牌，请只交给自己的手机。关闭此窗口即撤销本次连接。\n手机首次同步需选择来源；同步产生的修改可撤销。",TextWrapping=TextWrapping.Wrap});
  var status=new TextBlock{Margin=new Thickness(0,16,0,0)};panel.Children.Add(status);var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=(_,_)=>status.Text=$"服务运行中 · 已处理 {Interlocked.Read(ref server.Requests)} 次请求";timer.Start();
  if(args.Length>1)File.WriteAllText(args[1],server.PairingJson());
  app.Run(window);timer.Stop();return 0;
 }
}
