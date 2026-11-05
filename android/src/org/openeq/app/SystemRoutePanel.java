package org.openeq.app;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.os.Build;
import android.provider.Settings;
import android.net.Uri;
import android.view.View;
import android.widget.*;
import org.json.*;
import java.util.*;
final class SystemRoutePanel {
 final MainActivity a;final AudioWorkbench workbench;TextView state,route,backend,detail;Button start,stop;
 SystemRoutePanel(MainActivity a,AudioWorkbench workbench){this.a=a;this.workbench=workbench;}
 void clear(){state=route=backend=detail=null;start=stop=null;}
 void page(){
  LinearLayout top=a.card();state=a.text("系统耳机 · 待机",22,a.INK);top.addView(state);route=a.text(a.deviceLabel,13,a.MUTED);top.addView(route);
  LinearLayout row=new LinearLayout(a);start=a.button("开始处理",this::start);stop=a.button("停止处理",()->HeadphoneService.stop(a));
  row.addView(start,new LinearLayout.LayoutParams(0,-2,1));row.addView(stop,new LinearLayout.LayoutParams(0,-2,1));top.addView(row);
  backend=a.text("开始后读取设备支持的系统音效能力",14,a.INK);top.addView(backend);detail=a.text("",12,a.MUTED);top.addView(detail);
  top.addView(a.button("编辑当前生效 EQ",this::edit));
  LinearLayout source=a.card();source.addView(a.text("音源应用快捷入口",17,a.INK));
  workbench.sourcePick=a.usb.spinner(source,"系统音效作用于系统支持的媒体路径，不按此选项限定应用",CaptureSources.LABELS);workbench.sourcePick.setSelection(CaptureSources.index(a.usb.sourcePackage));
  workbench.sourcePick.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onNothingSelected(android.widget.AdapterView<?> p){}public void onItemSelected(android.widget.AdapterView<?> p,View v,int n,long id){a.usb.sourcePackage=CaptureSources.PACKAGES[n];a.usb.saveWorkbench();}});
  source.addView(a.button("打开音源应用",()->{if(a.usb.sourcePackage.isEmpty()){a.toast("请自行打开媒体应用");return;}Intent launch=a.getPackageManager().getLaunchIntentForPackage(a.usb.sourcePackage);if(launch==null){a.toast("应用未安装");return;}a.startActivity(launch);}));
  LinearLayout routing=a.card();routing.addView(a.text("自动切换 / 后台",17,a.INK));
  Switch memory=a.toggle(routing,"按耳机保存与恢复设置",a.getPreferences(0).getBoolean("autoDeviceProfiles",true));memory.setOnCheckedChangeListener((v,on)->a.getPreferences(0).edit().putBoolean("autoDeviceProfiles",on).apply());
  Switch pause=a.toggle(routing,"耳机断开时请求暂停播放器",a.getSharedPreferences("headphone-routing",0).getBoolean("pause_on_disconnect",true));pause.setOnCheckedChangeListener((v,on)->a.getSharedPreferences("headphone-routing",0).edit().putBoolean("pause_on_disconnect",on).apply());
  routing.addView(a.text("蓝牙、有线和系统USB随安卓媒体路由切换。断开后等待耳机、重连后恢复效果，不自动恢复播放。暂停请求是否被播放器执行以实际表现为准。",12,a.MUTED));
  routing.addView(a.button("后台 / 电池设置",()->a.startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+a.getPackageName())))));
  routing.addView(a.text("由前台服务与常驻通知维持，界面退后台不停止；系统回收后尝试恢复用户启用状态。强制停止、系统限制或关机不能保证自动恢复。可在应用电池设置选择不受限制。",12,a.MUTED));
  LinearLayout info=a.card();info.addView(a.text("实际处理方式",17,a.INK));info.addView(a.text("优先使用系统DynamicsProcessing：将主EQ幅频近似到系统64频段，并应用前级/输出增益与平衡。系统不支持时提供设备原生频段编辑。\n系统路径不回放一份捕获音频，因此不会由OpenDSP再复制一遍原声。空间、延时、反相和自研压缩不在此路径生效；完整DSP请使用USB独占或可选Root路径。\n蓝牙采样率与编码由系统/耳机协商，不冒称384kHz蓝牙输出。",12,a.MUTED));
  info.addView(a.button("高级路由 / 诊断",()->{a.page="设备";a.render();}));refresh();
 }
 void start(){
  if(a.usb.activeOrPending()||HeadphoneService.stopping){a.toast("请先停止其他链路并等待释放");return;}
  ArrayList<String> permissions=new ArrayList<>();
  if(Build.VERSION.SDK_INT>=31&&a.checkSelfPermission(android.Manifest.permission.BLUETOOTH_CONNECT)!=PackageManager.PERMISSION_GRANTED)permissions.add(android.Manifest.permission.BLUETOOTH_CONNECT);
  if(Build.VERSION.SDK_INT>=33&&a.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED)permissions.add(android.Manifest.permission.POST_NOTIFICATIONS);
  if(!permissions.isEmpty()){a.requestPermissions(permissions.toArray(new String[0]),79);return;}startGranted();
 }
 void startGranted(){try{HeadphoneService.start(a);}catch(Exception e){a.error(e);}}
 void refresh(){
  if(state==null)return;
  String phase=HeadphoneService.phase;
  AudioWorkbench.text(state,HeadphoneService.running?(phase.equals("active")?"系统耳机处理运行中":phase.equals("waiting_headphones")?"等待耳机 / 路由确认":phase.equals("no_control")?"音效控制权已转移":phase.equals("error")?"系统效果暂不可用":"正在启动"):"系统耳机 · 待机");
  AudioWorkbench.text(route,HeadphoneService.running?HeadphoneService.routeLabel:a.deviceLabel);
  AudioWorkbench.text(backend,HeadphoneService.running&&!HeadphoneService.backendName.isEmpty()?HeadphoneService.backendName:"开始后读取系统音效能力");
  String limitation="";try{limitation=new JSONObject(HeadphoneService.diagnostics).optString("limitation","");}catch(Exception ignored){}
  AudioWorkbench.text(detail,HeadphoneService.display+"\n"+limitation+"\n系统效果不提供本应用的输入电平读数，覆盖范围仍取决于系统和播放器。");
  start.setEnabled(!HeadphoneService.running&&!HeadphoneService.stopping&&!a.usb.activeOrPending());stop.setEnabled(HeadphoneService.running);
 }
 void edit(){
  if(!HeadphoneService.running){a.toast("请先开始系统耳机处理");return;}
  if(HeadphoneService.mapped){if(!a.page.equals("均衡")){a.page="均衡";a.render();}else a.toast("直接编辑下方主EQ，系统幅频会同步更新");return;}
  try{
   JSONObject state=new JSONObject(HeadphoneService.diagnostics);JSONArray bands=state.getJSONArray("bands_readback");String key=HeadphoneService.activeKey;
   LinearLayout panel=a.col();panel.setPadding(a.dp(18),0,a.dp(18),0);EditText[] edits=new EditText[bands.length()];
   for(int i=0;i<edits.length;i++){JSONObject band=bands.getJSONObject(i);edits[i]=a.field(panel,(band.getInt("frequency_mhz")/1000)+"Hz · 系统频段 dB",Double.toString(band.getInt("level_mb")/100d),true);}
   ScrollView scroll=new ScrollView(a);scroll.addView(panel);AlertDialog dialog=new AlertDialog.Builder(a).setTitle("原生频段 · 以系统回读为准").setView(scroll).setNegativeButton("取消",null).setPositiveButton("保存",null).create();
   dialog.setOnShowListener(v->dialog.getButton(-1).setOnClickListener(w->{try{int[] levels=new int[edits.length];for(int i=0;i<levels.length;i++)levels[i]=(int)Math.round(Preset.bounded(Double.parseDouble(edits[i].getText().toString()),state.optInt("minimum_mb",-1500)/100d,state.optInt("maximum_mb",1500)/100d,"系统频段")*100);HeadphoneService.setNativeProfile(key,levels);dialog.dismiss();}catch(Exception e){a.error(e);}}));dialog.show();
  }catch(Exception e){a.toast("请等待耳机与原生频段初始化");}
 }
}
