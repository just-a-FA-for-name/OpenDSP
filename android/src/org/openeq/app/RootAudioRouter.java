package org.openeq.app;
import android.content.Context;
import android.media.*;
import android.os.*;
import java.lang.reflect.*;

/** Runtime-only privileged routing; no vendor audio policy file is modified. */
final class RootAudioRouter implements AutoCloseable {
 final AudioManager manager;final Object policy;final Class<?> policyClass;final AudioRecord record;boolean closed,policyUnregistered;
 static Object call(Object o,String name,Class<?>[] types,Object... args)throws Exception{return o.getClass().getMethod(name,types).invoke(o,args);}
 RootAudioRouter(boolean duplicate)throws Exception {
  if(android.os.Process.myUid()!=0)throw new SecurityException("此后端必须由已授权的 Root 进程启动");
  if(Looper.myLooper()==null)Looper.prepareMainLooper();Class<?> at=Class.forName("android.app.ActivityThread");Object thread=at.getMethod("systemMain").invoke(null);Context context=(Context)at.getMethod("getSystemContext").invoke(thread);manager=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);
  Class<?> ruleClass=Class.forName("android.media.audiopolicy.AudioMixingRule");Object rules=Class.forName("android.media.audiopolicy.AudioMixingRule$Builder").getConstructor().newInstance();call(rules,"setTargetMixRole",new Class[]{int.class},0);
  // Media/game capture only. Other usage groups retain their original volume policy.
  for(int usage:new int[]{AudioAttributes.USAGE_UNKNOWN,AudioAttributes.USAGE_MEDIA,AudioAttributes.USAGE_GAME})call(rules,"addRule",new Class[]{AudioAttributes.class,int.class},new AudioAttributes.Builder().setUsage(usage).build(),1);
  call(rules,"excludeMixRule",new Class[]{int.class,Object.class},4,Integer.valueOf(android.os.Process.myUid()));Object rule=call(rules,"build",new Class[]{});
  Class<?> mixClass=Class.forName("android.media.audiopolicy.AudioMix");Object mixBuilder=Class.forName("android.media.audiopolicy.AudioMix$Builder").getConstructor(ruleClass).newInstance(rule);
  call(mixBuilder,"setFormat",new Class[]{AudioFormat.class},new AudioFormat.Builder().setSampleRate(48000).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).setEncoding(AudioFormat.ENCODING_PCM_FLOAT).build());call(mixBuilder,"setRouteFlags",new Class[]{int.class},duplicate?3:2);Object mix=call(mixBuilder,"build",new Class[]{});
  policyClass=Class.forName("android.media.audiopolicy.AudioPolicy");Object builder=Class.forName("android.media.audiopolicy.AudioPolicy$Builder").getConstructor(Context.class).newInstance(context);call(builder,"addMix",new Class[]{mixClass},mix);policy=call(builder,"build",new Class[]{});
  int status=(Integer)AudioManager.class.getMethod("registerAudioPolicy",policyClass).invoke(manager,policy);if(status!=0)throw new IllegalStateException("系统拒绝注册音频路由："+status);
  AudioRecord created=null;try{created=(AudioRecord)call(policy,"createAudioRecordSink",new Class[]{mixClass},mix);if(created==null||created.getState()!=AudioRecord.STATE_INITIALIZED)throw new IllegalStateException("Root 混流录音入口不可用");record=created;}catch(Exception e){if(created!=null)created.release();AudioManager.class.getMethod("unregisterAudioPolicy",policyClass).invoke(manager,policy);throw e;}
 }
 public void close(){if(closed)return;closed=true;try{record.stop();}catch(Exception ignored){}record.release();try{AudioManager.class.getMethod("unregisterAudioPolicy",policyClass).invoke(manager,policy);policyUnregistered=true;}catch(Exception e){e.printStackTrace();}}
}
