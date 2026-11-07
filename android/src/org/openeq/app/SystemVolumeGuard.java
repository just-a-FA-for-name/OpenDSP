package org.openeq.app;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.database.ContentObserver;
import android.media.AudioManager;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.Settings;

/**
 * While libusb owns the DAC, Android's own output must stay silent.
 *
 * The app never writes to AudioTrack, so a hardware volume press in any other
 * app is not delivered to MainActivity and (depending on the ROM and on which
 * media session is active) is applied to STREAM_MUSIC instead. Raising that
 * stream makes notifications, media and system sounds come out of the speaker
 * while the USB DAC is still playing the exclusive stream: two outputs at once.
 *
 * This guard remembers the user's real volumes, locks the audible system
 * streams to zero for the whole USB session, and restores them when the DAC is
 * released. A content observer plus a short poll catches every external
 * attempt to raise them, folds each raise into one USB volume step (so the key
 * still feels like it controls OpenDSP) and immediately re-locks the system
 * side. No hidden mute flag and no DND policy are used: even if the process is
 * killed, the only leftover state is a zero volume that the user can raise
 * with the hardware key, and the next app launch restores the saved values.
 *
 * STREAM_ALARM is deliberately left alone so alarms are never suppressed.
 */
final class SystemVolumeGuard implements AutoCloseable {
 static final String PREF="system-volume-guard";
 private static final String ACTIVE="active";
 /* Order matters: music first (volume keys default to it while media is active),
    ring last (default when nothing is playing). Alarm is intentionally absent. */
 private static final int[] STREAMS={AudioManager.STREAM_MUSIC,AudioManager.STREAM_NOTIFICATION,AudioManager.STREAM_SYSTEM,AudioManager.STREAM_RING};
 private static final String[] KEYS={"music","notification","system","ring"};
 private static final String[] SETTINGS={"volume_music","volume_notification","volume_system","volume_ring"};
 private static final long MIN_STEP_MS=120;

 private final Context context;
 private final AudioManager audio;
 private final SharedPreferences prefs;
 private final Handler handler=new Handler(Looper.getMainLooper());
 private final int[] saved=new int[STREAMS.length];
 private volatile boolean active,closed,writing,locking;
 private volatile long lastStepAt,lastLockAt;
 private boolean broadcastRegistered;
 private final ContentObserver observer=new ContentObserver(handler){
  public void onChange(boolean selfChange,Uri uri){enforce();}
 };
 private final Runnable poll=new Runnable(){public void run(){if(!closed){enforce();handler.postDelayed(this,400);}}};
 /** Best-effort symmetry: at index 0 a volume-down press changes no setting,
     so only AOSP's VOLUME_CHANGED_ACTION tells us the user pressed "minus". */
 private final BroadcastReceiver volumeBroadcast=new BroadcastReceiver(){
  public void onReceive(Context c,Intent i){
   if(!active||closed||writing)return;
   if(SystemClock.elapsedRealtime()-lastLockAt<250)return;
   int type=i.getIntExtra("android.media.EXTRA_VOLUME_STREAM_TYPE",-1);
   if(type!=AudioManager.STREAM_MUSIC&&type!=AudioManager.STREAM_RING&&type!=AudioManager.STREAM_NOTIFICATION)return;
   int value=i.getIntExtra("android.media.EXTRA_VOLUME_STREAM_VALUE",-1);
   int prev=i.getIntExtra("android.media.EXTRA_PREV_VOLUME_STREAM_VALUE",-1);
   if(value==0&&prev==0)step(-1);
  }
 };

 SystemVolumeGuard(Context context){
  this.context=context;
  audio=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);
  prefs=context.getSharedPreferences(PREF,0);
 }

 boolean active(){return active&&!closed;}

 void start(){
  /* Escape hatch: on ROMs where index 0 also mutes the playback-capture feed,
     the user can turn the lock off from the workbench without reinstalling. */
  if(!prefs.getBoolean("enabled",true)){active=false;return;}
  for(int i=0;i<STREAMS.length;i++)saved[i]=audio.getStreamVolume(STREAMS[i]);
  SharedPreferences.Editor e=prefs.edit().putBoolean(ACTIVE,true);
  for(int i=0;i<STREAMS.length;i++)e.putInt(KEYS[i],saved[i]);
  e.apply();
  active=true;
  for(String key:SETTINGS){
   Uri uri=Settings.System.getUriFor(key);
   if(uri!=null)try{context.getContentResolver().registerContentObserver(uri,false,observer);}catch(Exception ignored){}
  }
  try{
   IntentFilter filter=new IntentFilter("android.media.VOLUME_CHANGED_ACTION");
   if(Build.VERSION.SDK_INT>=33)context.registerReceiver(volumeBroadcast,filter,Context.RECEIVER_EXPORTED);
   else context.registerReceiver(volumeBroadcast,filter);
   broadcastRegistered=true;
  }catch(Exception ignored){}
  locking=true;
  lock();
  handler.postDelayed(poll,400);
 }

 /** Put every audible system stream back to zero. Called on start and after any external raise. */
 private void lock(){
  writing=true;
  lastLockAt=SystemClock.elapsedRealtime();
  try{for(int stream:STREAMS)try{audio.setStreamVolume(stream,0,0);}catch(SecurityException ignored){}}
  finally{writing=false;}
 }

 /** True when at least one stream was raised by something outside this guard. */
 private boolean raised(){
  for(int stream:STREAMS){
   try{if(audio.getStreamVolume(stream)>0)return true;}catch(Exception ignored){}
  }
  return false;
 }

 private void enforce(){
  if(!active||closed||writing)return;
  if(!raised())return;
  lock();
  step(1);
 }

 /** Fold one hardware volume press into the USB master volume. */
 private void step(int direction){
  if(!active||closed||!UsbAudioService.processing||UsbAudioService.dopActive)return;
  long now=SystemClock.elapsedRealtime();
  if(now-lastStepAt<MIN_STEP_MS)return;
  lastStepAt=now;
  UsbVolume.adjust(context,direction>0?UsbVolume.STEP:-UsbVolume.STEP);
 }

 /** Restore the exact volumes the user had before the session. Idempotent. */
 @Override public void close(){
  if(closed)return;
  closed=true;active=false;
  if(!locking){prefs.edit().putBoolean(ACTIVE,false).apply();return;}
  handler.removeCallbacksAndMessages(null);
  try{context.getContentResolver().unregisterContentObserver(observer);}catch(Exception ignored){}
  if(broadcastRegistered){try{context.unregisterReceiver(volumeBroadcast);}catch(Exception ignored){}broadcastRegistered=false;}
  writing=true;
  try{for(int i=STREAMS.length-1;i>=0;i--)try{audio.setStreamVolume(STREAMS[i],saved[i],0);}catch(SecurityException ignored){}}
  finally{writing=false;}
  prefs.edit().putBoolean(ACTIVE,false).apply();
 }

 /** Recovery path for a killed process: restore once, only when no session is live. */
 static void restoreIfOrphan(Context context){
  if(UsbAudioService.processing||UsbAudioService.busy())return;
  UsbAudioService live=UsbAudioService.instance;
  if(live!=null&&live.systemGuard!=null&&live.systemGuard.active())return;
  SharedPreferences p=context.getSharedPreferences(PREF,0);
  if(!p.getBoolean(ACTIVE,false))return;
  AudioManager a=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);
  for(int i=0;i<STREAMS.length;i++){
   int value=p.getInt(KEYS[i],-1);
   if(value>=0)try{a.setStreamVolume(STREAMS[i],value,0);}catch(SecurityException ignored){}
  }
  p.edit().putBoolean(ACTIVE,false).apply();
 }
}
