package org.openeq.app;
import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.os.*;
/** Explicit, Android-limited data-sync session, separate from audio lifecycle. */
public final class SyncService extends Service {
 static volatile boolean running;final Handler timer=new Handler(Looper.getMainLooper());
 final Runnable poll=new Runnable(){public void run(){if(!running)return;if(!PresetSync.busy)PresetSync.once(SyncService.this,"auto");timer.postDelayed(this,3000);}};
 public IBinder onBind(Intent intent){return null;}
 public int onStartCommand(Intent intent,int flags,int id){
  if(intent!=null&&"stop".equals(intent.getAction())){stopSelf();return START_NOT_STICKY;}
  getSystemService(NotificationManager.class).createNotificationChannel(new NotificationChannel("preset-sync","设置同步",NotificationManager.IMPORTANCE_LOW));
  PendingIntent open=PendingIntent.getActivity(this,91,new Intent(this,MainActivity.class).putExtra("page","同步"),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
  PendingIntent stop=PendingIntent.getService(this,92,new Intent(this,SyncService.class).setAction("stop"),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
  Notification notification=new Notification.Builder(this,"preset-sync").setSmallIcon(android.R.drawable.stat_notify_sync).setContentTitle("OpenDSP · 双端设置同步").setContentText("仅同步EQ预设 · 双端冲突不自动覆盖").setContentIntent(open).setOngoing(true).addAction(new Notification.Action.Builder(null,"停止同步",stop).build()).build();
  if(Build.VERSION.SDK_INT>=29)startForeground(24,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);else startForeground(24,notification);
  running=true;timer.removeCallbacks(poll);timer.post(poll);return START_NOT_STICKY;
 }
 public void onTimeout(int startId,int type){PresetSync.status="系统同步时限已到，请手动重新开启";stopSelf();}
 public void onDestroy(){running=false;PresetSync.generation.incrementAndGet();timer.removeCallbacksAndMessages(null);stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();}
}
