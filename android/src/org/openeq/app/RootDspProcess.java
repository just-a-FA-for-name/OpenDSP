package org.openeq.app;
import android.media.*;
import android.os.Process;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.atomic.AtomicReference;
import org.json.*;

/** Separate Root process. Native USB output is a distinct backend, never implied here. */
public final class RootDspProcess {
 static volatile boolean running=true;
 static String read(File file)throws IOException{try(InputStream in=new FileInputStream(file)){ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] b=new byte[4096];int n;while((n=in.read(b))!=-1){out.write(b,0,n);if(out.size()>262144)throw new IOException("配置文件过大");}return new String(out.toByteArray(),StandardCharsets.UTF_8);}}
 static void write(File file,JSONObject value)throws Exception{File next=new File(file.getPath()+".tmp");try(FileOutputStream out=new FileOutputStream(next)){out.write(value.toString().getBytes(StandardCharsets.UTF_8));}if(!next.renameTo(file))throw new IOException("无法更新状态文件");}
 public static void main(String[] args){RootAudioRouter router=null;AudioTrack output=null;File directory=new File(args[0]),config=new File(directory,"preset.json"),status=new File(directory,"status.json"),stop=new File(directory,"stop");int parent=Integer.parseInt(args[1]);long frames=0;double energyL=0,energyR=0,outL=0,outR=0;long started=System.nanoTime();int exit=1;String error=null;final AtomicReference<JSONObject> reports=new AtomicReference<>();Thread control=null;
  try{final AtomicReference<Preset> pending=new AtomicReference<>(Preset.parse(read(config)));control=new Thread(()->{long changed=config.lastModified();while(running){try{if(stop.exists()||!new File("/proc/"+parent).exists()){running=false;break;}JSONObject report=reports.getAndSet(null);if(report!=null)write(status,report);if(config.lastModified()!=changed){Preset p=Preset.parse(read(config));pending.set(p);changed=config.lastModified();}Thread.sleep(150);}catch(Exception e){System.err.println("CONFIG_ERROR "+e);}}},"OpenEQ-root-control");control.setDaemon(true);control.start();
   router=new RootAudioRouter(false);int rate=router.record.getSampleRate();Preset active=pending.get();DspRack rack=new DspRack(rate,active);
   int min=AudioTrack.getMinBufferSize(rate,AudioFormat.CHANNEL_OUT_STEREO,AudioFormat.ENCODING_PCM_FLOAT);output=new AudioTrack.Builder().setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).setAllowedCapturePolicy(AudioAttributes.ALLOW_CAPTURE_BY_NONE).build()).setAudioFormat(new AudioFormat.Builder().setSampleRate(rate).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).setEncoding(AudioFormat.ENCODING_PCM_FLOAT).build()).setTransferMode(AudioTrack.MODE_STREAM).setBufferSizeInBytes(Math.max(min*2,8192)).build();if(output.getState()!=AudioTrack.STATE_INITIALIZED)throw new IllegalStateException("Root 回放输出不可用");
   Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO);float[] block=new float[1024];router.record.startRecording();output.play();System.out.println("ROOT_EQ_READY rate="+rate+" format=float32 uid="+Process.myUid());long lastReport=0;
   while(running){Preset next=pending.get();if(next!=active){rack.configure(next);active=next;}int count=router.record.read(block,0,block.length,AudioRecord.READ_BLOCKING);if(count<=0||count%2!=0)throw new IOException("Root 音频输入错误："+count);
    for(int n=0;n<count;n+=2){double l=block[n],r=block[n+1];rack.process(l,r);energyL+=l*l;energyR+=r*r;outL+=rack.left*rack.left;outR+=rack.right*rack.right;block[n]=(float)rack.left;block[n+1]=(float)rack.right;frames++;}
    for(int n=0;n<count&&running;){int done=output.write(block,n,count-n,AudioTrack.WRITE_BLOCKING);if(done<=0)throw new IOException("Root 音频输出错误："+done);n+=done;}
    long now=System.nanoTime();if(now-lastReport>500000000){lastReport=now;reports.set(new JSONObject().put("state","running").put("input_rate",rate).put("input_format","float32").put("output_mode","Android AudioTrack shared").put("usb_exclusive",false).put("frames",frames).put("input_energy_left",energyL).put("input_energy_right",energyR).put("output_energy_left",outL).put("output_energy_right",outR).put("rendered_frames",Integer.toUnsignedLong(output.getPlaybackHeadPosition())).put("underruns",output.getUnderrunCount()).put("clipped_frames",rack.clipped()).put("preset",active.name).put("seconds",(now-started)/1e9));}
   }exit=0;
  }catch(Throwable failure){if(failure instanceof java.lang.reflect.InvocationTargetException&&failure.getCause()!=null)failure=failure.getCause();error=failure.toString();failure.printStackTrace();}
  finally{running=false;if(control!=null)try{control.join(600);}catch(Exception ignored){}if(output!=null){try{output.stop();}catch(Exception ignored){}output.release();}if(router!=null)router.close();try{JSONObject pendingReport=reports.getAndSet(null);if(pendingReport!=null)write(status,pendingReport);JSONObject ended=status.exists()?new JSONObject(read(status)):new JSONObject();ended.put("state",exit==0?"stopped":"error").put("policy_registered",router!=null).put("policy_unregistered",router!=null&&router.policyUnregistered);if(error!=null)ended.put("error",error);write(status,ended);}catch(Exception ignored){}}
  System.exit(exit);
 }
}
