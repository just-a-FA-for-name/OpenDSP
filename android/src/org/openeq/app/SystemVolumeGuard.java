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
 * While libusb owns the DAC, Android's own media output must stay silent.
 *
 * Only STREAM_MUSIC is touched. RING / NOTIFICATION / SYSTEM are never written:
 * forcing those to zero is what used to throw the phone into vibrate mode.
 *
 * The stream is NOT locked to index 0. At index 0 a volume-down press produces
 * no change at all, so the key looked "dead" on the way down while volume-up
 * still worked. Instead the stream keeps a non-zero sentinel index and is muted
 * (ADJUST_MUTE): the speaker stays silent, yet every hardware press still moves
 * the index, so both directions can be observed and folded into one USB step.
 * After each press the sentinel and the mute state are restored immediately.
 *
 * If the ROM routes the key to our media session instead, UsbMediaVolume calls
 * noteRemoteKey() and this guard only re-locks, so a press is never counted
 * twice. The user's real volume is restored (and the stream unmuted) on exit.
 */
final class SystemVolumeGuard implements AutoCloseable {
 static final String PREF="system-volume-guard";
 private static final String ACTIVE="active";
 private static final int STREAM=AudioManager.STREAM_MUSIC;
 private static final long MIN_STEP_MS=120;
 private static final long REMOTE_DEDUP_MS=400;

 private final Context context;
 private final AudioManager audio;
 private final SharedPreferences prefs;
 private final Handler handler=new Handler(Looper.getMainLooper());
 private volatile boolean active,closed,writing,locking;
 private volatile long lastStepAt,lastLockAt;
 private int saved,sentinel;
 private boolean broadcastRegistered;
 private static volatile long lastRemoteKeyAt;

 /** Called by UsbMediaVolume when Android delivered the key to our session. */
 static void noteRemoteKey(){lastRemoteKeyAt=SystemClock.elapsedRealtime();}

 private final ContentObserver observer=new ContentObserver(handler){
  public void onChange(boolean selfChange,Uri uri){enforce();}
 };
 private final Runnable poll=new Runnable(){public void run(){if(!closed){enforce();handler.postDelayed(this,200);}}};
 private final BroadcastReceiver volumeBroadcast=new BroadcastReceiver(){
  public void onReceive(Context c,Intent i){
   if(!active||closed||writing)return;
   if(SystemClock.elapsedRealtime()-lastLockAt<150)return;
   if(i.getIntExtra("android.media.EXTRA_VOLUME_STREAM_TYPE",-1)!=STREAM)return;
   enforce();
  }
 };

 SystemVolumeGuard(Context context){
  this.context=context;
  audio=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);
  prefs=context.getSharedPreferences(PREF,0);
 }

 boolean active(){return active&&!closed;}

 void start(){
  /* Escape hatch: on ROMs where muting the media stream also mutes the
     playback-capture feed, the lock can be turned off from the workbench. */
  if(!prefs.getBoolean("enabled",true)){active=false;return;}
  /* Read while un-muted: several ROMs report 0 from getStreamVolume() when
     the stream is muted, and the settings value is not written back reliably. */
  boolean wasActive=prefs.getBoolean(ACTIVE,false);
  int stored=prefs.getInt("music",-1);
  try{if(audio.isStreamMute(STREAM))audio.adjustStreamVolume(STREAM,AudioManager.ADJUST_UNMUTE,0);}catch(SecurityException ignored){}
  /* If a previous session was killed before restoring, the saved value in
     prefs is the user's real volume; otherwise take the live index. */
  saved=wasActive&&stored>=0?stored:audio.getStreamVolume(STREAM);
  int max=audio.getStreamMaxVolume(STREAM);
  /* One step is all the headroom needed: the guard restores it after every
     press, so both raise and lower stay observable and the unmute blip stays
     at the quietest possible index. */
  sentinel=max>=3?2:Math.max(1,max);
  prefs.edit().putBoolean(ACTIVE,true).putInt("music",saved).putInt("sentinel",sentinel).apply();
  active=true;
  Uri uri=Settings.System.getUriFor("volume_music");
  if(uri!=null)try{context.getContentResolver().registerContentObserver(uri,false,observer);}catch(Exception ignored){}
  try{
   IntentFilter filter=new IntentFilter("android.media.VOLUME_CHANGED_ACTION");
   if(Build.VERSION.SDK_INT>=33)context.registerReceiver(volumeBroadcast,filter,Context.RECEIVER_EXPORTED);
   else context.registerReceiver(volumeBroadcast,filter);
   broadcastRegistered=true;
  }catch(Exception ignored){}
  locking=true;
  lock();
  handler.postDelayed(poll,200);
 }

 /** Re-apply the silent state: non-zero sentinel index plus stream mute. */
 private void lock(){
  writing=true;
  lastLockAt=SystemClock.elapsedRealtime();
  try{
   /* Some ROMs ignore setStreamVolume while the stream is muted, so drop the
      mute first, move the index, then mute again. The window is a few ms and
      the index is the quietest one, so nothing audible escapes. */
   if(audio.isStreamMute(STREAM))audio.adjustStreamVolume(STREAM,AudioManager.ADJUST_UNMUTE,0);
   audio.setStreamVolume(STREAM,sentinel,0);
   /* Some ROMs ignore the first write when the index sits at 0; one retry. */
   if(audio.getStreamVolume(STREAM)!=sentinel)audio.setStreamVolume(STREAM,sentinel,0);
   audio.adjustStreamVolume(STREAM,AudioManager.ADJUST_MUTE,0);
  }catch(SecurityException ignored){}
  finally{writing=false;}
 }

 private void enforce(){
  if(!active||closed||writing)return;
  if(SystemClock.elapsedRealtime()-lastLockAt<150)return;
  boolean muted;
  try{muted=audio.isStreamMute(STREAM);}catch(Exception e){return;}
  /* Still muted means no key arrived: nothing to observe. */
  if(muted)return;
  int current;
  try{current=audio.getStreamVolume(STREAM);}catch(Exception e){return;}
  /* A key press un-mutes the stream before it moves the index. When the index
     did not move either, the press was volume-up (volume-down keeps it muted). */
  int direction=current>sentinel?1:(current<sentinel?-1:1);
  lock();
  step(direction);
 }

 /** Fold one hardware volume press into the USB master volume. */
 private void step(int direction){
  if(!active||closed||!UsbAudioService.processing||UsbAudioService.dopActive)return;
  long now=SystemClock.elapsedRealtime();
  if(now-lastRemoteKeyAt<REMOTE_DEDUP_MS)return;   // media session already handled it
  if(now-lastStepAt<MIN_STEP_MS)return;
  lastStepAt=now;
  UsbVolume.adjust(context,direction>0?UsbVolume.STEP:-UsbVolume.STEP);
 }

 /** Restore the user's real volume and clear the mute. Idempotent. */
 @Override public void close(){
  if(closed)return;
  closed=true;active=false;
  if(!locking){prefs.edit().putBoolean(ACTIVE,false).apply();return;}
  handler.removeCallbacksAndMessages(null);
  try{context.getContentResolver().unregisterContentObserver(observer);}catch(Exception ignored){}
  if(broadcastRegistered){try{context.unregisterReceiver(volumeBroadcast);}catch(Exception ignored){}broadcastRegistered=false;}
  writing=true;
  try{
   audio.adjustStreamVolume(STREAM,AudioManager.ADJUST_UNMUTE,0);
   audio.setStreamVolume(STREAM,saved,0);
  }catch(SecurityException ignored){}
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
  int value=p.getInt("music",-1);
  try{
   a.adjustStreamVolume(STREAM,AudioManager.ADJUST_UNMUTE,0);
   if(value>=0)a.setStreamVolume(STREAM,value,0);
  }catch(SecurityException ignored){}
  p.edit().putBoolean(ACTIVE,false).apply();
 }
}
