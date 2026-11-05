import android.media.*;
import android.os.*;

/** Short-lived REMOTE_SUBMIX probe. Reports aggregate levels only, never saves captured audio. */
public final class RootAudioProbe {
    public static void main(String[] args){
        new Thread(()->{try{Thread.sleep(8000);}catch(Exception ignored){}System.err.println("PROBE_TIMEOUT");System.exit(3);},"probe-watchdog").start();
        AudioRecord record=null;AudioTrack track=null;
        try{
            if(Looper.myLooper()==null)Looper.prepareMainLooper();
            Class<?> at=Class.forName("android.app.ActivityThread");at.getDeclaredMethod("systemMain").invoke(null);
            int rate=48000,mask=AudioFormat.CHANNEL_IN_STEREO;
            int min=AudioRecord.getMinBufferSize(rate,mask,AudioFormat.ENCODING_PCM_16BIT);
            record=new AudioRecord.Builder().setAudioSource(8).setAudioFormat(new AudioFormat.Builder().setSampleRate(rate).setChannelMask(mask).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(Math.max(min*4,192000)).build();
            System.out.println("record_state="+record.getState()+" session="+record.getAudioSessionId());
            record.startRecording();System.out.println("recording_state="+record.getRecordingState());
            track=new AudioTrack.Builder().setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).build()).setAudioFormat(new AudioFormat.Builder().setSampleRate(rate).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(192000).build();
            short[] tone=new short[48000];for(int n=0;n<tone.length/2;n++){short s=(short)(16*Math.sin(2*Math.PI*1000*n/rate));tone[n*2]=s;tone[n*2+1]=s;}
            track.play();track.write(tone,0,tone.length,AudioTrack.WRITE_NON_BLOCKING);
            short[] data=new short[4800];long total=0;double squares=0;int peak=0;
            for(int n=0;n<12;n++){int count=record.read(data,0,data.length,AudioRecord.READ_BLOCKING);if(count<0)throw new IllegalStateException("read="+count);for(int i=0;i<count;i++){peak=Math.max(peak,Math.abs((int)data[i]));squares+=(double)data[i]*data[i];}total+=count;}
            System.out.println("samples="+total+" peak_pcm16="+peak+" rms_pcm16="+Math.sqrt(squares/Math.max(1,total)));
            System.out.println("NOTE: Capturable mixed media only; this does not prove voice-call capture or USB exclusive output.");
        }catch(Throwable t){t.printStackTrace();System.exit(2);}
        finally{if(record!=null){try{record.stop();}catch(Exception ignored){}record.release();}if(track!=null){try{track.stop();}catch(Exception ignored){}track.release();}}
        System.exit(0);
    }
}
