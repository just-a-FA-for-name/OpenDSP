import android.content.Context;
import android.media.*;
import android.os.*;
import java.lang.reflect.*;

/** Temporary media mix probe: aggregate levels only, no recording is saved. */
public final class RootPolicyProbe {
 static Object call(Object object,String method,Class<?>[] types,Object... args)throws Exception{return object.getClass().getMethod(method,types).invoke(object,args);}
 public static void main(String[] args){AudioRecord recorder=null;AudioManager manager=null;Object policy=null;Class<?> policyClass=null;
  Thread watchdog=new Thread(()->{try{Thread.sleep(15000);}catch(Exception e){}System.err.println("POLICY_PROBE_TIMEOUT");System.exit(3);});watchdog.setDaemon(true);watchdog.start();
  try{
   if(Looper.myLooper()==null)Looper.prepareMainLooper();Class<?> activity=Class.forName("android.app.ActivityThread");Object thread=activity.getMethod("systemMain").invoke(null);Context context=(Context)activity.getMethod("getSystemContext").invoke(thread);manager=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);
   Class<?> ruleClass=Class.forName("android.media.audiopolicy.AudioMixingRule"),ruleBuilder=Class.forName("android.media.audiopolicy.AudioMixingRule$Builder");Object rules=ruleBuilder.getConstructor().newInstance();
   call(rules,"setTargetMixRole",new Class[]{int.class},0);
   // Media and game only. Do not capture communications or bypass protected audio.
   for(int usage:new int[]{AudioAttributes.USAGE_MEDIA,AudioAttributes.USAGE_GAME})call(rules,"addRule",new Class[]{AudioAttributes.class,int.class},new AudioAttributes.Builder().setUsage(usage).build(),1);
   call(rules,"excludeMixRule",new Class[]{int.class,Object.class},4,Integer.valueOf(android.os.Process.myUid()));Object rule=call(rules,"build",new Class[]{});
   Class<?> mixClass=Class.forName("android.media.audiopolicy.AudioMix"),mixBuilder=Class.forName("android.media.audiopolicy.AudioMix$Builder");Object mixB=mixBuilder.getConstructor(ruleClass).newInstance(rule);
   call(mixB,"setFormat",new Class[]{AudioFormat.class},new AudioFormat.Builder().setSampleRate(48000).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build());
   call(mixB,"setRouteFlags",new Class[]{int.class},3);Object mix=call(mixB,"build",new Class[]{});
   policyClass=Class.forName("android.media.audiopolicy.AudioPolicy");Object builder=Class.forName("android.media.audiopolicy.AudioPolicy$Builder").getConstructor(Context.class).newInstance(context);call(builder,"addMix",new Class[]{mixClass},mix);policy=call(builder,"build",new Class[]{});
   int status=(Integer)AudioManager.class.getMethod("registerAudioPolicy",policyClass).invoke(manager,policy);System.out.println("register_status="+status);if(status!=0)throw new IllegalStateException("Audio policy registration failed");
   recorder=(AudioRecord)call(policy,"createAudioRecordSink",new Class[]{mixClass},mix);if(recorder==null||recorder.getState()!=AudioRecord.STATE_INITIALIZED)throw new IllegalStateException("Capture sink unavailable");recorder.startRecording();System.out.println("POLICY_CAPTURE_READY uid="+android.os.Process.myUid());
   short[] samples=new short[4800];long count=0;double squares=0;int peak=0;for(int n=0;n<100;n++){int got=recorder.read(samples,0,samples.length,AudioRecord.READ_BLOCKING);if(got<0)throw new IllegalStateException("read="+got);for(int i=0;i<got;i++){int x=samples[i];squares+=(double)x*x;peak=Math.max(peak,Math.abs(x));}count+=got;}
   System.out.println("samples="+count+" peak_pcm16="+peak+" rms_pcm16="+Math.sqrt(squares/Math.max(1,count)));System.out.println("scope=temporary media/game capture policy; no USB exclusivity or complete app coverage proven");
  }catch(Throwable error){if(error instanceof InvocationTargetException&&error.getCause()!=null)error=error.getCause();error.printStackTrace();}
  finally{if(recorder!=null){try{recorder.stop();}catch(Exception e){}recorder.release();}if(manager!=null&&policy!=null)try{AudioManager.class.getMethod("unregisterAudioPolicy",policyClass).invoke(manager,policy);System.out.println("policy_unregistered=true");}catch(Exception e){e.printStackTrace();}}
  System.exit(0);
 }
}
