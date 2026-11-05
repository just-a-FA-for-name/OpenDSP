package org.openeq.app;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.hardware.usb.*;
import android.media.projection.MediaProjectionManager;
import android.media.projection.MediaProjectionConfig;
import android.os.*;
import android.widget.*;
import java.util.*;
import org.json.*;

/** Permission/UI layer; never opens USB before a user action. */
final class UsbPanel {
 final MainActivity activity;final UsbManager manager;final AudioWorkbench workbench;
 String device,action,sourcePackage="com.netease.cloudmusic",requestSource="",message="";
 int mode,rate=384000,bits=32,requestRate,requestBits,requestMode,flow;boolean pending;
 TextView status;static volatile boolean probing;
 static final String PERMISSION="org.openeq.app.USB_PERMISSION";
 final BroadcastReceiver permission=new BroadcastReceiver(){public void onReceive(Context c,Intent i){
  if(!PERMISSION.equals(i.getAction())||!pending||i.getIntExtra("flow",-1)!=flow)return;
  UsbDevice selected=i.getParcelableExtra(UsbManager.EXTRA_DEVICE);
  if(selected==null||!selected.getDeviceName().equals(device))return;
  if(i.getBooleanExtra(UsbManager.EXTRA_PERMISSION_GRANTED,false))continueAction();else{pending=false;message="USB授权未获允许，未开始处理";activity.toast(message);}
 }};
 UsbPanel(MainActivity activity,Bundle state){this.activity=activity;manager=(UsbManager)activity.getSystemService(Context.USB_SERVICE);workbench=new AudioWorkbench(activity,this);
  IntentFilter filter=new IntentFilter(PERMISSION);if(Build.VERSION.SDK_INT>=33)activity.registerReceiver(permission,filter,Context.RECEIVER_NOT_EXPORTED);else activity.registerReceiver(permission,filter);
  rate=activity.getPreferences(0).getInt("usb.rate",384000);bits=activity.getPreferences(0).getInt("usb.bits",32);mode=activity.getPreferences(0).getInt("usb.mode",0);
  sourcePackage=activity.getPreferences(0).getString("capture.package","com.netease.cloudmusic");try{CaptureSources.index(sourcePackage);}catch(Exception e){sourcePackage="com.netease.cloudmusic";}
  device=activity.getPreferences(0).getString("usb.device",null);
  if(state!=null){pending=state.getBoolean("usb.pending");action=state.getString("usb.action");device=state.getString("usb.device",device);requestSource=state.getString("usb.requestSource","");requestRate=state.getInt("usb.requestRate",rate);requestBits=state.getInt("usb.requestBits",bits);requestMode=state.getInt("usb.requestMode",0);flow=state.getInt("usb.flow");}
 }
 void saveState(Bundle state){state.putBoolean("usb.pending",pending);state.putString("usb.action",action);state.putString("usb.device",device);state.putString("usb.requestSource",requestSource);state.putInt("usb.requestRate",requestRate);state.putInt("usb.requestBits",requestBits);state.putInt("usb.requestMode",requestMode);state.putInt("usb.flow",flow);}
 void saveWorkbench(){activity.getPreferences(0).edit().putString("capture.package",sourcePackage).putInt("usb.rate",rate).putInt("usb.bits",bits).putString("usb.device",device).apply();}
 boolean activeOrPending(){return pending||probing||UsbAudioService.busy();}
 void cancelPending(){boolean wasPending=pending;pending=false;action=null;message=wasPending?"已取消待启动请求":"";}
 void beginCapture(){
  if(HeadphoneService.running||HeadphoneService.stopping){activity.toast("请先停止系统耳机模式，等待释放后再开始USB");return;}
  if(activeOrPending()){activity.toast("当前正在处理或等待授权");return;}
  try{CaptureSources.uid(activity,sourcePackage);}catch(Exception e){message="音源应用未安装或不可用";refresh();activity.toast(message);return;}
  try{UsbCapturePreflight.check(activity);}catch(Exception e){message=e.getMessage();refresh();new AlertDialog.Builder(activity).setTitle("先排除USB路由争用").setMessage(message).setNegativeButton("取消",null).setPositiveButton("开发者选项",(d,w)->{try{activity.startActivity(new Intent(android.provider.Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS));}catch(Exception error){activity.error(error);}}).show();return;}
  choose("capture");
 }
 ArrayList<UsbDevice> audioDevices(){
  ArrayList<UsbDevice> devices=new ArrayList<>();for(UsbDevice candidate:manager.getDeviceList().values())for(int i=0;i<candidate.getInterfaceCount();i++)if(candidate.getInterface(i).getInterfaceClass()==UsbConstants.USB_CLASS_AUDIO){devices.add(candidate);break;}return devices;
 }
 UsbDevice selectedDevice(){ArrayList<UsbDevice> devices=audioDevices();for(UsbDevice d:devices)if(d.getDeviceName().equals(device))return d;if(devices.size()==1){device=devices.get(0).getDeviceName();return devices.get(0);}return null;}
 void selectDac(){
  if(activeOrPending()){activity.toast("请先停止处理");return;}ArrayList<UsbDevice> devices=audioDevices();if(devices.isEmpty()){message="未检测到USB DAC，请通过OTG连接";refresh();return;}
  String[] names=new String[devices.size()];for(int i=0;i<names.length;i++)names[i]=devices.get(i).getProductName()+" · "+devices.get(i).getDeviceName();
  new AlertDialog.Builder(activity).setTitle("选择USB输出").setItems(names,(d,n)->{device=devices.get(n).getDeviceName();saveWorkbench();workbench.nextDevices=0;refresh();}).setNegativeButton("取消",null).show();
 }
 void close(){activity.unregisterReceiver(permission);}
 void resetViews(){status=null;workbench.resetViews();}
 void refresh(){if(status!=null)status.setText(UsbAudioService.display);workbench.refresh();}
 void header(LinearLayout parent){
  workbench.header(parent);
 }
 void page(){workbench.page();}
 void tools(){
  LinearLayout card=activity.card();card.addView(activity.text("辅助工具 · 本地文件 / USB诊断",21,activity.INK));status=activity.text(UsbAudioService.display,12,activity.MUTED);card.addView(status);
  card.addView(activity.button("返回应用调音工作台",()->{activity.page="工作台";activity.render();}));
  card.addView(activity.text("直接驱动DAC，绕过AudioTrack混音；接管期间系统USB音频可能暂停。实际格式必须由DAC接受并回读，不会悄悄降到48k。首次请用舒适音量。",12,activity.MUTED));
  Spinner modes=spinner(card,"输出方式",new String[]{"PCM + 完整DSP","DSD64 DSF直送 / DoP","DSD128 DSF直送 / DoP"});modes.setSelection(mode);
  final int[] rates={44100,48000,88200,96000,176400,192000,352800,384000};String[] labels=new String[rates.length];int selected=7;
  for(int i=0;i<rates.length;i++){labels[i]=rates[i]+" Hz";if(rates[i]==rate)selected=i;}
  Spinner sampleRate=spinner(card,"PCM目标采样率",labels);sampleRate.setSelection(selected);
  final int[] widths={16,24,32};Spinner width=spinner(card,"PCM位数 / DoP载体位数",new String[]{"16-bit","24-bit","32-bit"});width.setSelection(bits==16?0:bits==24?1:2);
  Runnable save=()->{mode=modes.getSelectedItemPosition();rate=rates[sampleRate.getSelectedItemPosition()];bits=widths[width.getSelectedItemPosition()];activity.getPreferences(0).edit().putInt("usb.mode",mode).putInt("usb.rate",rate).putInt("usb.bits",bits).apply();};
  activity.rowButtons(card,"选文件 → USB播放",()->{save.run();choose("file");},"停止并释放USB",()->UsbAudioService.stop(activity));
  activity.rowButtons(card,"接管并查询USB格式",()->{save.run();choose("probe");},"复制诊断",()->{android.content.ClipboardManager clip=(android.content.ClipboardManager)activity.getSystemService(Context.CLIPBOARD_SERVICE);clip.setPrimaryClip(ClipData.newPlainText("OpenDSP USB",UsbAudioService.diagnostics));activity.toast("诊断已复制");});
  card.addView(activity.text("PCM：WAV 16/24/32-bit及浮点；FLAC/AAC/MP3等由手机解码器提供，源精度以解码回读为准。异率使用64抽头/256相位sinc转换；384k输出不等于音源本身是384k。\nDSD：当前仅双声道DSF DSD64/128，经DoP直送且绕过全部DSP；尚不支持手机PCM实时转DSD。\nUSB等时传输与反馈仍需在具体手机/DAC上验收。",12,activity.MUTED));
  LinearLayout info=activity.card();info.addView(activity.text("已连接USB设备",17,activity.INK));
  for(UsbDevice d:manager.getDeviceList().values())info.addView(activity.text(d.getProductName()+String.format(Locale.US," · %04x:%04x",d.getVendorId(),d.getProductId()),13,activity.MUTED));
  if(manager.getDeviceList().isEmpty())info.addView(activity.text("请将DAC通过OTG接入手机；调试建议使用无线ADB。",13,activity.MUTED));
 }
 Spinner spinner(LinearLayout parent,String label,String[] options){parent.addView(activity.text(label,12,activity.MUTED));Spinner spinner=new Spinner(activity);spinner.setAdapter(new ArrayAdapter<>(activity,android.R.layout.simple_spinner_dropdown_item,options));parent.addView(spinner);return spinner;}
 void choose(String operation){
  if(HeadphoneService.running||HeadphoneService.stopping){activity.toast("请先停止系统耳机模式并等待释放");return;}
  if(activeOrPending()){activity.toast("请先停止当前USB会话，等待释放完成");return;}
  if(RootEqService.processing){RootEqService.stop(activity);activity.toast("已请求停止Root链路，待其停止后再开启USB");return;}
  ArrayList<UsbDevice> devices=audioDevices();
  if(devices.isEmpty()){message="没有检测到USB DAC，未开始处理";activity.toast(message);refresh();return;}
  action=operation;requestSource="capture".equals(operation)?sourcePackage:"";requestRate=rate;requestBits=bits;requestMode="capture".equals(operation)?0:mode;pending=true;flow++;message="请完成USB及系统音频授权";
  UsbDevice preferred=selectedDevice();if(preferred!=null){authorize(preferred);return;}
  String[] names=new String[devices.size()];for(int i=0;i<names.length;i++)names[i]=devices.get(i).getProductName()+" · "+devices.get(i).getDeviceName();
  new AlertDialog.Builder(activity).setTitle("接管哪个USB DAC？").setItems(names,(d,index)->{
   authorize(devices.get(index));
  }).setNegativeButton("取消",(d,w)->cancelPending()).setOnCancelListener(d->cancelPending()).show();
 }
 void authorize(UsbDevice selected){device=selected.getDeviceName();saveWorkbench();
  if(manager.hasPermission(selected))continueAction();
  else manager.requestPermission(selected,PendingIntent.getBroadcast(activity,701,new Intent(PERMISSION).setPackage(activity.getPackageName()).putExtra("flow",flow),PendingIntent.FLAG_UPDATE_CURRENT|(Build.VERSION.SDK_INT>=31?PendingIntent.FLAG_MUTABLE:0)));
 }
 void continueAction(){
  if(!pending||action==null)return;
  if("probe".equals(action)){pending=false;probe();return;}
  if("file".equals(action)){activity.open(31,"*/*");return;}
  if(Build.VERSION.SDK_INT<29){pending=false;message="应用音频捕获需要Android 10以上";activity.toast(message);return;}
  if(activity.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO)!=PackageManager.PERMISSION_GRANTED){
   activity.requestPermissions(Build.VERSION.SDK_INT>=33?new String[]{android.Manifest.permission.RECORD_AUDIO,android.Manifest.permission.POST_NOTIFICATIONS}:new String[]{android.Manifest.permission.RECORD_AUDIO},78);return;
  }
  MediaProjectionManager manager=activity.getSystemService(MediaProjectionManager.class);
  Intent consent=Build.VERSION.SDK_INT>=34?manager.createScreenCaptureIntent(MediaProjectionConfig.createConfigForDefaultDisplay()):manager.createScreenCaptureIntent();
  activity.startActivityForResult(consent,32);
 }
 void recordPermission(boolean granted){if(!pending)return;if(granted)continueAction();else{pending=false;message="录音权限未获允许，未开始处理";activity.toast(message);}}
 boolean result(int request,int result,Intent data){
  if(request!=31&&request!=32)return false;if(!pending)return true;
  if(result!=Activity.RESULT_OK||data==null){pending=false;message="系统授权或文件选择已取消，未开始处理";refresh();return true;}
  try{
   activity.player.stop();activity.stopService(new Intent(activity,NativeEqService.class));
   Intent start=new Intent(activity,UsbAudioService.class).putExtra("device",device).putExtra("mode",requestMode).putExtra("rate",requestRate).putExtra("bits",requestBits).putExtra("preset",activity.preset.encode());
   if(request==31){if(data.getData()==null){cancelPending();return true;}start.setData(data.getData()).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);}
   else start.putExtra("capture",true).putExtra("source_package",requestSource).putExtra("projection_result",result).putExtra("projection_data",data);
   pending=false;message="";activity.startForegroundService(start);activity.page="工作台";activity.render();
  }catch(Exception e){pending=false;message="未开始处理："+e.getMessage();activity.error(e);}return true;
 }
 void probe(){
  probing=true;final String selectedName=device;
  new Thread(()->{UsbDeviceConnection connection=null;ArrayList<UsbInterface> claimed=new ArrayList<>();
   try{UsbDevice selected=manager.getDeviceList().get(selectedName);if(selected==null)throw new Exception("设备已断开");connection=manager.openDevice(selected);if(connection==null)throw new Exception("无法打开设备");
    claimed=UsbAudioService.claim(selected,connection);String report=UsbNative.probe(connection.getFileDescriptor());UsbAudioService.diagnostics=report;
    activity.runOnUiThread(()->{if(!activity.isFinishing()&&!activity.isDestroyed())new AlertDialog.Builder(activity).setTitle("USB声明能力（不是传输验收）").setMessage(report).setPositiveButton("关闭",null).show();});
   }catch(Throwable e){activity.runOnUiThread(()->activity.toast("USB查询失败："+e.getMessage()));}
   finally{if(connection!=null){for(UsbInterface item:claimed){connection.releaseInterface(item);UsbNative.attach(connection.getFileDescriptor(),item.getId());}connection.close();}probing=false;}
  },"OpenDSP-USB-probe").start();
 }
}
