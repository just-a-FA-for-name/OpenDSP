package org.openeq.app;
import android.media.*;
import android.os.Process;
import java.io.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import org.openeq.dsp.Biquad;
import org.openeq.dsp.StereoStage;

/** Foreground preview player. Uses Android shared AudioTrack; never claims USB exclusivity. */
final class Player {
    interface Listener {void status(String message);}
    private final AtomicBoolean running=new AtomicBoolean();
    private volatile Preset pending;
    private Thread thread;
    volatile AudioTrack track;
    volatile int underruns;
    void update(Preset p){pending=p.copy();}
    boolean playing(){return running.get();}
    void stop(){running.set(false);AudioTrack t=track;if(t!=null)try{t.pause();}catch(Exception ignored){} }
    void play(InputStream source, Preset preset,Listener listener)throws Exception {
        if(UsbAudioService.busy())throw new Exception("请先停止USB直连并等待释放");
        if(thread!=null&&thread.isAlive())throw new Exception("请先停止当前试听，稍后重试");
        pending=preset.copy();running.set(true);
        thread=new Thread(()->{
            AudioTrack output=null;
            try(InputStream raw=source){
                Process.setThreadPriority(Process.THREAD_PRIORITY_AUDIO);
                Wav wav=new Wav(raw);int rate=wav.rate;
                int min=AudioTrack.getMinBufferSize(rate,AudioFormat.CHANNEL_OUT_STEREO,AudioFormat.ENCODING_PCM_FLOAT);
                if(min<=0)throw new IOException("设备不支持此采样率");
                output=new AudioTrack.Builder().setAudioAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
                    .setAudioFormat(new AudioFormat.Builder().setSampleRate(rate).setChannelMask(AudioFormat.CHANNEL_OUT_STEREO).setEncoding(AudioFormat.ENCODING_PCM_FLOAT).build())
                    .setTransferMode(AudioTrack.MODE_STREAM).setBufferSizeInBytes(Math.max(min*2,32768)).build();
                if(output.getState()!=AudioTrack.STATE_INITIALIZED)throw new IOException("音频输出初始化失败");
                track=output;output.play();listener.status("试听中 · "+rate+" Hz · Android 共享输出");
                float[] block=new float[1024];Preset active=null;DspRack rack=null;boolean routedThroughRoot=false;long submitted=0;
                while(running.get()){
                    boolean root=RootEqService.processing;
                    if(pending!=active||root!=routedThroughRoot){active=pending;routedThroughRoot=root;Preset processing=active;
                        if(root){processing=active.copy();processing.enabled=false;processing.output=0;processing.balance=0;processing.leftDelay=processing.rightDelay=0;processing.invertLeft=processing.invertRight=false;processing.protection=false;processing.processing=new org.json.JSONObject();}
                        if(rack==null)rack=new DspRack(rate,processing);else rack.configure(processing);
                    }
                    int count=0;
                    while(count<block.length&&wav.remaining>=wav.frameBytes){
                        double l=wav.sample(),r=wav.channels==2?wav.sample():l;
                        rack.process(l,r);block[count++]=(float)rack.left;block[count++]=(float)rack.right;
                    }
                    if(count==0)break;
                    int written=0;while(written<count&&running.get()){int n=output.write(block,written,count-written,AudioTrack.WRITE_BLOCKING);if(n<0)throw new IOException("AudioTrack write: "+n);if(n==0)throw new IOException("音频输出无进展");written+=n;}submitted+=written/2;
                }
                // AudioTrack.stop() drops queued streaming frames; wait for playback before releasing.
                long deadline=System.currentTimeMillis()+3000;while(running.get()&&Integer.toUnsignedLong(output.getPlaybackHeadPosition())<submitted&&System.currentTimeMillis()<deadline)Thread.sleep(5);
                underruns=output.getUnderrunCount();listener.status("试听结束 · 欠载 "+underruns+" · 削波帧 "+rack.clipped());
            }catch(Exception e){listener.status("试听失败："+e.getMessage());}
            finally {running.set(false);track=null;if(output!=null){try{output.stop();}catch(Exception ignored){}output.release();}}
        },"OpenEQ-preview");thread.start();
    }
    static final class Wav {
        final InputStream in;int channels,rate,bits,frameBytes;long remaining;
        Wav(InputStream input)throws IOException {
            in=new BufferedInputStream(input,65536);
            if(!tag().equals("RIFF"))throw new IOException("仅支持 RIFF WAV");u32();if(!tag().equals("WAVE"))throw new IOException("不是 WAV 文件");
            boolean fmt=false;long scanned=0;
            while(true){String id=tag();long size=u32();scanned+=size+8;if(scanned>16*1024*1024&&!id.equals("data"))throw new IOException("WAV 头部过大");
                if(id.equals("fmt ")){if(size<16||size>65536)throw new IOException("无效 fmt");int encoding=u16();channels=u16();rate=(int)u32();u32();frameBytes=u16();bits=u16();
                    if(encoding!=1||(channels!=1&&channels!=2)||(bits!=16&&bits!=24)||rate<8000||rate>192000||frameBytes!=channels*(bits/8))throw new IOException("支持 8–192 kHz、单/双声道 PCM 16/24-bit WAV");skip(size-16+(size&1));fmt=true;
                }else if(id.equals("data")){if(!fmt)throw new IOException("缺少 fmt");remaining=size;if(size%frameBytes!=0)throw new IOException("WAV 数据未按帧对齐");break;}
                else skip(size+(size&1));
            }
        }
        int read()throws IOException{int x=in.read();if(x<0)throw new EOFException("WAV 文件截断");return x;}
        int u16()throws IOException{return read()|read()<<8;}
        long u32()throws IOException{return (long)read()|((long)read()<<8)|((long)read()<<16)|((long)read()<<24);}
        String tag()throws IOException{return ""+(char)read()+(char)read()+(char)read()+(char)read();}
        void skip(long n)throws IOException{while(n>0){long s=in.skip(n);if(s<=0){read();s=1;}n-=s;}}
        double sample()throws IOException{int v=read()|read()<<8;if(bits==24){v|=read()<<16;v=(v<<8)>>8;remaining-=3;return v/8388608.0;}remaining-=2;return (short)v/32768.0;}
    }
}
