package org.openeq.app;
import android.app.*;
import android.content.*;
import android.os.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import org.json.*;

/** Foreground owner of a separate, explicitly authorized Root DSP process. */
public final class RootEqService extends Service {
 static volatile RootEqService instance;static volatile String display="Root PEQ 未运行";static volatile boolean processing,stopping;
 final Handler handler=new Handler(Looper.getMainLooper());volatile java.lang.Process process;File directory;DeviceRoutes routes;String key="";volatile boolean closing;final java.util.concurrent.CountDownLatch ownerDone=new java.util.concurrent.CountDownLatch(1);boolean ownerStarted;
static void start(Context context){if(HeadphoneService.running||HeadphoneService.stopping||UsbAudioService.busy())throw new IllegalStateException("请先停止当前音频链路并等待释放");if(stopping||(instance!=null&&instance.closing))throw new IllegalStateException("正在恢复系统音频路由，请稍后再启用");if(Build.VERSION.SDK_INT<29)throw new IllegalStateException("Root 全局后端需要 Android 10 或以上");Intent intent=new Intent(context,RootEqService.class);context.startForegroundService(intent);}
 static void stop(Context context){if(instance!=null){stopping=true;display="正在停止 Root PEQ 并恢复系统路由…";}context.stopService(new Intent(context,RootEqService.class));}
 static void update(Preset p){RootEqService service=instance;if(service!=null&&service.directory!=null)try{service.writePreset(p);}catch(Exception e){display="Root 配置更新失败："+e.getMessage();}}
 void writePreset(Preset p)throws Exception{File next=new File(directory,"preset.next");try(FileOutputStream out=new FileOutputStream(next)){out.write(p.encode().getBytes(StandardCharsets.UTF_8));}if(!next.renameTo(new File(directory,"preset.json")))throw new IOException("无法更新 Root EQ 配置");}
 static String quote(String s){return "'"+s.replace("'","'\\''")+"'";}
 @Override public void onCreate(){super.onCreate();instance=this;display="正在启动 Root PEQ，首次使用请在 Magisk 授权";processing=false;
  NotificationManager nm=(NotificationManager)getSystemService(NOTIFICATION_SERVICE);nm.createNotificationChannel(new NotificationChannel("root-eq","OpenEQ Root 音频处理",NotificationManager.IMPORTANCE_LOW));PendingIntent open=PendingIntent.getActivity(this,20,new Intent(this,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);PendingIntent stop=PendingIntent.getService(this,21,new Intent(this,RootEqService.class).setAction("stop"),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
  startForeground(20,new Notification.Builder(this,"root-eq").setSmallIcon(android.R.drawable.ic_media_play).setContentTitle("OpenEQ · Root 全局 PEQ").setContentText("普通媒体与游戏经过自研 EQ；USB 独占为独立后端").setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null,"停止",stop).build()).build());
  try{directory=new File(getFilesDir(),"root-sessions/"+Long.toHexString(System.nanoTime()));if(!directory.mkdirs())throw new IOException("无法创建 Root 音频会话目录");Preset initial;try{initial=Preset.parse(getSharedPreferences("MainActivity",0).getString("current",""));}catch(Exception e){initial=Preset.defaults();}writePreset(initial);
   routes=new DeviceRoutes(this,(next,label,type)->{if(next.equals(key))return;key=next;if(!getSharedPreferences("MainActivity",0).getBoolean("autoDeviceProfiles",true))return;try{String saved=getSharedPreferences("MainActivity",0).getString("device."+key,null);Preset p=saved!=null?Preset.parse(saved):Preset.defaults();p.upgradeLegacyStarter();writePreset(p);}catch(Exception e){display="设备配置加载失败："+e.getMessage();}});
   ownerStarted=true;new Thread(()->{try{String command="CLASSPATH="+quote(getApplicationInfo().sourceDir)+" app_process /system/bin org.openeq.app.RootDspProcess "+quote(directory.getAbsolutePath())+" "+android.os.Process.myPid();java.lang.Process child=new ProcessBuilder("su","-c",command).redirectErrorStream(true).start();process=child;
    if(closing){if(!child.waitFor(3,java.util.concurrent.TimeUnit.SECONDS))child.destroy();return;}String last="";try(BufferedReader in=new BufferedReader(new InputStreamReader(child.getInputStream(),StandardCharsets.UTF_8))){String line;while((line=in.readLine())!=null){last=line.length()>500?line.substring(0,500):line;if(line.startsWith("ROOT_EQ_READY")){processing=true;display="Root PEQ · 48 kHz 浮点入口 → Android 共享输出";}}}int code=child.waitFor();if(!closing&&code!=0)display="Root 音频后端退出（"+code+"）："+last;
   }catch(Exception e){if(!closing)display="Root PEQ 启动失败："+e.getMessage();}finally{processing=false;ownerDone.countDown();if(!closing)handler.post(()->stopSelf());}},"OpenEQ-root-owner").start();handler.post(statusPoll);
  }catch(Exception e){display="Root PEQ 启动失败："+e.getMessage();stopSelf();}
 }
 final Runnable statusPoll=new Runnable(){public void run(){if(closing)return;try{File status=new File(directory,"status.json");if(status.exists()){JSONObject j=new JSONObject(RootDspProcess.read(status));String state=j.optString("state");if(state.equals("running")){processing=true;display="Root PEQ · "+j.optInt("input_rate")+" Hz · "+j.optLong("frames")+" 帧 · 欠载 "+j.optInt("underruns");}else if(state.equals("error")){display="Root PEQ："+j.optString("error");stopSelf();return;}}}catch(Exception ignored){}handler.postDelayed(this,1000);}};
 @Override public int onStartCommand(Intent intent,int flags,int id){if(intent!=null&&"stop".equals(intent.getAction())){display="Root PEQ 已停止";stopSelf();}return START_NOT_STICKY;}
 @Override public void onDestroy(){closing=true;stopping=true;handler.removeCallbacksAndMessages(null);if(routes!=null)routes.close();if(directory!=null)try{new File(directory,"stop").createNewFile();}catch(Exception ignored){}if(!ownerStarted)ownerDone.countDown();new Thread(()->{try{ownerDone.await();java.lang.Process child=process;if(child!=null){if(!child.waitFor(3,java.util.concurrent.TimeUnit.SECONDS)){child.destroy();child.waitFor();}}}catch(InterruptedException e){Thread.currentThread().interrupt();}finally{stopping=false;if(display.startsWith("Root PEQ ·")||display.startsWith("正在启动")||display.startsWith("正在停止"))display="Root PEQ 已停止，系统路由已恢复";}},"OpenEQ-root-stop").start();processing=false;if(instance==this)instance=null;stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();}
 @Override public IBinder onBind(Intent intent){return null;}
}
