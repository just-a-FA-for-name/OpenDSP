package org.openeq.app;
import android.app.PendingIntent;
import android.content.Intent;
import android.media.VolumeProvider;
import android.media.MediaMetadata;
import android.media.session.*;
import android.os.Handler;
import android.os.Looper;

/** Remote volume for app-owned USB PCM. Media-key selection still belongs to Android. */
final class UsbMediaVolume implements AutoCloseable {
 final MediaSession session;final VolumeProvider provider;
 UsbMediaVolume(UsbAudioService service){
  session=new MediaSession(service,"OpenDSP-USB-volume");
  provider=new VolumeProvider(VolumeProvider.VOLUME_CONTROL_ABSOLUTE,100,UsbVolume.state.muted?0:UsbVolume.state.percent){
   public void onAdjustVolume(int direction){if(direction!=0){SystemVolumeGuard.noteRemoteKey();UsbVolume.adjust(service,direction>0?UsbVolume.STEP:-UsbVolume.STEP);}}
   public void onSetVolumeTo(int value){SystemVolumeGuard.noteRemoteKey();UsbVolume.set(service,value,false);}
  };
  session.setCallback(new MediaSession.Callback(){public boolean onMediaButtonEvent(Intent intent){return false;}},new Handler(Looper.getMainLooper()));
  /* Keep this session a first-class media session. Some OEM ROMs ignore a
     session with no actions / no media-button receiver and fall back to
     STREAM_MUSIC for the volume keys, which is exactly the double-output bug. */
  session.setFlags(MediaSession.FLAG_HANDLES_MEDIA_BUTTONS|MediaSession.FLAG_HANDLES_TRANSPORT_CONTROLS);
  session.setSessionActivity(PendingIntent.getActivity(service,50,new Intent(service,MainActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT));
  session.setPlaybackToRemote(provider);session.setMetadata(new MediaMetadata.Builder().putString(MediaMetadata.METADATA_KEY_TITLE,"OpenDSP USB主音量").build());
  session.setPlaybackState(new PlaybackState.Builder().setActions(PlaybackState.ACTION_PLAY_PAUSE|PlaybackState.ACTION_STOP).setState(PlaybackState.STATE_PLAYING,PlaybackState.PLAYBACK_POSITION_UNKNOWN,1).build());session.setActive(true);
 }
 void sync(){provider.setCurrentVolume(UsbVolume.state.muted?0:UsbVolume.state.percent);}
 public void close(){session.setActive(false);session.release();}
}
