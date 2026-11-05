package org.openeq.app;
import android.content.Context;
import java.util.Locale;

final class UsbVolume {
 /** One hardware key / notification press changes the displayed percentage by this amount. */
 static final int STEP=1;
 /** At and above the knee the legacy linear mapping is preserved, so 100% stays exactly unity. */
 private static final int KNEE=20;
 /** Below the knee every 1% step is a fixed dB increment, giving sensitive IEMs real low-level range. */
 private static final double LOW_DB_PER_STEP=2.0;
 static final class State {
  final int percent;final boolean muted;
  State(int p,boolean m){percent=Math.max(0,Math.min(100,p));muted=m;}
  double gain(){
   if(muted||percent<=0)return 0;
   if(percent>=KNEE)return percent/100d;
   return (KNEE/100d)*Math.pow(10,-LOW_DB_PER_STEP*(KNEE-percent)/20);
  }
 }
 static volatile State state=new State(100,false);static boolean loaded;
 static synchronized void load(Context context){if(loaded)return;android.content.SharedPreferences prefs=context.getSharedPreferences("usb-volume",0);state=new State(prefs.getInt("percent",100),prefs.getBoolean("muted",false));loaded=true;}
 static synchronized void set(Context context,int percent,boolean muted){load(context);if(UsbAudioService.processing&&UsbAudioService.dopActive)return;state=new State(percent,muted);context.getSharedPreferences("usb-volume",0).edit().putInt("percent",state.percent).putBoolean("muted",state.muted).apply();UsbAudioService service=UsbAudioService.instance;if(service!=null)service.volumeChanged();}
 static void adjust(Context context,int delta){load(context);set(context,state.percent+delta,false);}
 static void toggle(Context context){load(context);set(context,state.percent,!state.muted);}
 static String label(){State s=state;return s.gain()==0?"静音":String.format(Locale.US,"%d%% · %.1f dB",s.percent,20*Math.log10(s.gain()));}
}
