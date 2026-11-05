package org.openeq.app;
import android.app.*;
import android.content.*;
import android.media.*;
import android.os.*;
import android.view.KeyEvent;
import org.json.*;
import java.io.*;

/** Foreground, notification-controlled system headphone EQ owner. No capture/replay. */
public final class HeadphoneService extends Service {
 static volatile HeadphoneService instance;static volatile boolean running,stopping;
 static volatile String phase="idle",display="系统耳机模式未启动",routeLabel="等待系统路由",activeKey="",backendName="",diagnostics="{}";
 static volatile int nativeBands;static volatile boolean mapped;static volatile long revision;static volatile Preset activePreset;
 final Handler main=new Handler(Looper.getMainLooper());HandlerThread thread;Handler control;DeviceRoutes routes;SystemEqBackend backend;
 volatile boolean closing;String key="";int type=-1;Preset preset;boolean hadHeadphones;
 static void start(Context context){
  if(stopping||UsbAudioService.busy()||RootEqService.processing||RootEqService.stopping)throw new IllegalStateException("请先停止当前链路并等待释放");
  context.getSharedPreferences("headphone-routing",0).edit().putBoolean("wanted",true).apply();
  context.stopService(new Intent(context,NativeEqService.class));context.startForegroundService(new Intent(context,HeadphoneService.class));
 }
 static void stop(Context context){context.getSharedPreferences("headphone-routing",0).edit().putBoolean("wanted",false).apply();context.stopService(new Intent(context,HeadphoneService.class));}
 static void update(Preset preset,String expectedDevice){HeadphoneService s=instance;if(s!=null&&!s.closing&&s.control!=null){Preset copy=preset.copy();s.control.post(()->{
  if(!s.key.equals(expectedDevice)){
   if(!DeviceRoutes.headphone(s.type)&&!activeKey.isEmpty()&&activeKey.equals(expectedDevice)){
    s.preset=copy;android.content.SharedPreferences.Editor edit=s.presets().edit().putString("current",copy.encode());if(s.presets().getBoolean("autoDeviceProfiles",true))edit.putString("device."+activeKey,copy.encode());edit.apply();activePreset=copy.copy();revision++;s.apply();return;
   }
   s.saveCurrent();return;
  }
  s.preset=copy;s.saveCurrent();s.apply();
 });}}
 static boolean wanted(Context context){return context.getSharedPreferences("headphone-routing",0).getBoolean("wanted",false);}
 boolean option(String name,boolean fallback){return getSharedPreferences("headphone-routing",0).getBoolean(name,fallback);}
 android.content.SharedPreferences presets(){return getSharedPreferences("MainActivity",0);}
 Notification notification(){
  PendingIntent open=PendingIntent.getActivity(this,31,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
  PendingIntent stop=PendingIntent.getService(this,32,new Intent(this,HeadphoneService.class).setAction("stop"),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
  return new Notification.Builder(this,"headphone-eq").setSmallIcon(android.R.drawable.ic_media_play).setContentTitle("OpenDSP · 自动耳机").setContentText(display).setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null,"停止处理",stop).build()).build();
 }
 public void onCreate(){
  super.onCreate();instance=this;running=true;phase="starting";display="正在检测系统耳机";mapped=false;nativeBands=0;backendName="";activeKey="";
  NotificationManager manager=getSystemService(NotificationManager.class);manager.createNotificationChannel(new NotificationChannel("headphone-eq","耳机与后台处理",NotificationManager.IMPORTANCE_LOW));startForeground(31,notification());
  thread=new HandlerThread("OpenDSP-system-effects");thread.start();control=new Handler(thread.getLooper());
  try{preset=Preset.parse(presets().getString("current",""));}catch(Exception e){preset=Preset.defaults();}
  routes=new DeviceRoutes(this,(next,label,route)->control.post(()->route(next,label,route)));
  control.postDelayed(health,5000);
 }
 void route(String next,String label,int nextType){
  if(closing)return;if(next.equals(key)&&nextType==type)return;
  boolean previous=DeviceRoutes.headphone(type),connected=DeviceRoutes.headphone(nextType);
  boolean first=key.isEmpty();saveCurrent();key=next;type=nextType;routeLabel=label;
  if(connected){
   hadHeadphones=true;
   if(presets().getBoolean("autoDeviceProfiles",true)&&!next.isEmpty()){
    String saved=presets().getString("device."+next,null);try{preset=saved==null?(first?preset:Preset.defaults()):Preset.parse(saved);}catch(Exception ignored){preset=Preset.defaults();}
   }
   activePreset=preset.copy();activeKey=next;revision++;
  }else if(previous&&nextType>=0&&option("pause_on_disconnect",true)){
   AudioManager audio=getSystemService(AudioManager.class);
   audio.dispatchMediaKeyEvent(new KeyEvent(KeyEvent.ACTION_DOWN,KeyEvent.KEYCODE_MEDIA_PAUSE));audio.dispatchMediaKeyEvent(new KeyEvent(KeyEvent.ACTION_UP,KeyEvent.KEYCODE_MEDIA_PAUSE));
  }
  apply();
 }
 void saveCurrent(){
  if(preset==null)return;
  android.content.SharedPreferences.Editor edit=presets().edit().putString("current",preset.encode());
  if(DeviceRoutes.headphone(type)&&!key.isEmpty()&&presets().getBoolean("autoDeviceProfiles",true))edit.putString("device."+key,preset.encode());
  edit.apply();activePreset=preset.copy();revision++;
 }
 void apply(){
  if(closing)return;
  try{
   if(!DeviceRoutes.headphone(type)){
    if(backend!=null)backend.enabled(false);phase="waiting_headphones";display=type<0?"系统路由尚未确认":"等待耳机 · 本应用效果已暂停";publish();return;
   }
   if(backend==null)backend=new SystemEqBackend();
   String raw=presets().getString("native."+key,null);JSONObject nativeProfile=raw==null?null:new JSONObject(raw);
   String rate=getSystemService(AudioManager.class).getProperty(AudioManager.PROPERTY_OUTPUT_SAMPLE_RATE);int sampleRate=48000;try{sampleRate=Integer.parseInt(rate);}catch(Exception ignored){}
   backend.apply(preset,sampleRate,nativeProfile);backend.enabled(true);
   mapped=backend.mapped();nativeBands=backend.bands;backendName=mapped?"系统高级EQ · "+nativeBands+"频段幅频近似":"系统原生EQ · "+nativeBands+"段";
   phase="active";display=routeLabel+" · "+backendName;publish();
  }catch(Exception e){phase="error";display="系统音效暂不可用："+e.getMessage();if(backend!=null){backend.close();backend=null;}publish();}
 }
 final Runnable health=new Runnable(){public void run(){
  if(closing)return;
  if(DeviceRoutes.headphone(type)&&backend!=null&&!backend.controlled()){phase="no_control";display="音效控制权已被其他应用接管";publish();}
  else if((backend==null||phase.equals("no_control"))&&DeviceRoutes.headphone(type))apply();
  control.postDelayed(this,5000);
 }};
 void publish(){
  try{
   JSONObject d=backend==null?new JSONObject():backend.snapshot();d.put("phase",phase).put("route",routeLabel).put("device_key",key).put("native_mapped",mapped).put("headphones",DeviceRoutes.headphone(type)).put("capture",false).put("replay",false).put("background_service",true).put("preset",preset.name);
   diagnostics=d.toString();
  }catch(Exception e){diagnostics="{\"phase\":\"error\"}";}
  main.post(()->{if(!closing)getSystemService(NotificationManager.class).notify(31,notification());});
 }
 static void setNativeBand(int index,int level){
  HeadphoneService s=instance;if(s==null)return;
  s.control.post(()->{try{s.backend.nativeBand(index,level);JSONObject d=s.backend.snapshot();JSONArray bands=d.getJSONArray("bands_readback");s.presets().edit().putString("native."+s.key,new JSONObject().put("bands",bands).toString()).apply();s.publish();}catch(Exception e){display=e.getMessage();}});
 }
 static void setNativeProfile(String expectedKey,int[] levels){
  HeadphoneService s=instance;if(s==null)return;
  s.control.post(()->{try{if(!expectedKey.equals(s.key)||s.backend==null||s.backend.mapped())throw new IllegalStateException("设备或处理方式已改变，请重新打开编辑器");for(int i=0;i<levels.length;i++)s.backend.nativeBand(i,levels[i]);JSONObject d=s.backend.snapshot();s.presets().edit().putString("native."+s.key,new JSONObject().put("bands",d.getJSONArray("bands_readback")).toString()).apply();s.publish();}catch(Exception e){display="系统频段未应用："+e.getMessage();}});
 }
 public int onStartCommand(Intent intent,int flags,int id){
  if(intent!=null&&"stop".equals(intent.getAction())){getSharedPreferences("headphone-routing",0).edit().putBoolean("wanted",false).apply();stopSelf();return START_NOT_STICKY;}
  if(!wanted(this)){stopSelf();return START_NOT_STICKY;}return START_STICKY;
 }
 public void onDestroy(){
  closing=true;running=false;stopping=true;if(routes!=null)routes.close();if(instance==this)instance=null;
  control.removeCallbacksAndMessages(null);control.post(()->{try{saveCurrent();if(backend!=null){backend.close();backend=null;}phase="stopped";display="系统耳机处理已停止";}finally{stopping=false;thread.quitSafely();}});
  stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();
 }
 protected void dump(FileDescriptor fd,PrintWriter writer,String[] args){writer.println(diagnostics);}
 public IBinder onBind(Intent intent){return null;}
}
