package org.openeq.app;
import android.app.*;
import android.content.*;
import android.media.audiofx.Equalizer;
import android.os.*;
import org.json.*;

/** Foreground owner of the explicitly selected platform-native compatibility EQ. */
public final class NativeEqService extends Service {
    static Equalizer effect;static String currentKey="";static volatile boolean running;DeviceRoutes routes;
    static synchronized Equalizer open(Context context){
        if(HeadphoneService.running||HeadphoneService.stopping)throw new IllegalStateException("请在工作台停止系统耳机模式后再打开旧兼容工具");
        if(UsbAudioService.busy())throw new IllegalStateException("请先停止USB直连并等待释放");
        if(effect==null)effect=new Equalizer(0,0);
        try{Intent intent=new Intent(context,NativeEqService.class);if(Build.VERSION.SDK_INT>=26)context.startForegroundService(intent);else context.startService(intent);return effect;}
        catch(RuntimeException e){effect.release();effect=null;throw e;}
    }
    static synchronized void save(Context context){if(effect==null||currentKey.isEmpty())return;try{JSONArray bands=new JSONArray();for(short i=0;i<effect.getNumberOfBands();i++)bands.put(new JSONObject().put("frequency_mhz",effect.getCenterFreq(i)).put("level_mb",effect.getBandLevel(i)));JSONObject state=new JSONObject().put("enabled",effect.getEnabled()).put("bands",bands);context.getSharedPreferences("native-eq",0).edit().putString("device."+currentKey,state.toString()).apply();}catch(Exception ignored){}}
    static synchronized void load(Context context,String key){if(effect==null)return;String saved=context.getSharedPreferences("native-eq",0).getString("device."+key,null);try{
        if(saved==null){for(short i=0;i<effect.getNumberOfBands();i++)effect.setBandLevel(i,(short)0);return;}
        JSONObject state=new JSONObject(saved);JSONArray bands=state.getJSONArray("bands");if(bands.length()!=effect.getNumberOfBands())throw new Exception("native EQ layout changed");for(short i=0;i<effect.getNumberOfBands();i++)if(bands.getJSONObject(i).getInt("frequency_mhz")!=effect.getCenterFreq(i))throw new Exception("native EQ frequency changed");
        short[] range=effect.getBandLevelRange();for(short i=0;i<effect.getNumberOfBands();i++){int level=bands.getJSONObject(i).getInt("level_mb");effect.setBandLevel(i,(short)Math.max(range[0],Math.min(range[1],level)));}effect.setEnabled(state.getBoolean("enabled"));
    }catch(Exception e){for(short i=0;i<effect.getNumberOfBands();i++)effect.setBandLevel(i,(short)0);}}
    @Override public void onCreate(){super.onCreate();NotificationManager manager=(NotificationManager)getSystemService(NOTIFICATION_SERVICE);manager.createNotificationChannel(new NotificationChannel("eq", "OpenEQ 音频处理",NotificationManager.IMPORTANCE_LOW));
        Intent open=new Intent(this,MainActivity.class);PendingIntent activity=PendingIntent.getActivity(this,0,open,PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);Intent stop=new Intent(this,NativeEqService.class).setAction("stop");PendingIntent stopIntent=PendingIntent.getService(this,1,stop,PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        Notification notification=new Notification.Builder(this,"eq").setSmallIcon(android.R.drawable.ic_media_play).setContentTitle("OpenEQ · 系统兼容 EQ").setContentText("后台保持 EQ，按当前设备加载设置").setContentIntent(activity).setOngoing(true).addAction(new Notification.Action.Builder(null,"停止",stopIntent).build()).build();startForeground(10,notification);
        running=true;routes=new DeviceRoutes(this,(key,label,type)->{synchronized(NativeEqService.class){if(key.equals(currentKey))return;save(this);currentKey=key;load(this,key);}});
    }
    @Override public int onStartCommand(Intent intent,int flags,int id){if(intent!=null&&"stop".equals(intent.getAction())){stopSelf();return START_NOT_STICKY;}return START_NOT_STICKY;}
    @Override public void onDestroy(){running=false;save(this);if(routes!=null)routes.close();synchronized(NativeEqService.class){if(effect!=null)effect.release();effect=null;currentKey="";}stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();}
    @Override public IBinder onBind(Intent intent){return null;}
}
