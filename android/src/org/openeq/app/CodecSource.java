package org.openeq.app;
import android.content.Context;
import android.net.Uri;
import android.media.*;
import java.io.*;
import java.nio.*;
import org.openeq.dsp.PcmSource;

/** Android's decoder for user-selected FLAC/AAC/MP3 etc.; reports actual PCM precision. */
final class CodecSource implements PcmSource {
 final MediaExtractor extractor=new MediaExtractor();MediaCodec codec;final MediaCodec.BufferInfo info=new MediaCodec.BufferInfo();
 int rate,channels,encoding=AudioFormat.ENCODING_PCM_16BIT,index=-1;ByteBuffer buffer;boolean inputEnd,outputEnd;volatile boolean closed;
 CodecSource(Context context,Uri uri)throws IOException{
  try{
   extractor.setDataSource(context,uri,null);MediaFormat format=null;
   for(int i=0;i<extractor.getTrackCount();i++){MediaFormat f=extractor.getTrackFormat(i);String mime=f.getString(MediaFormat.KEY_MIME);if(mime!=null&&mime.startsWith("audio/")){format=f;extractor.selectTrack(i);break;}}
   if(format==null)throw new IOException("没有可解码音轨；加密文件不支持");
   rate=format.getInteger(MediaFormat.KEY_SAMPLE_RATE);channels=format.getInteger(MediaFormat.KEY_CHANNEL_COUNT);checkFormat();
   String mime=format.getString(MediaFormat.KEY_MIME);format.setInteger(MediaFormat.KEY_PCM_ENCODING,AudioFormat.ENCODING_PCM_FLOAT);
   codec=MediaCodec.createDecoderByType(mime);codec.configure(format,null,null,0);codec.start();pump();
  }catch(Exception e){close();throw new IOException("音频解码失败："+e.getMessage(),e);}
 }
 void checkFormat()throws IOException{if(rate<8000||rate>768000||(channels!=1&&channels!=2))throw new IOException("仅支持单/双声道音源");}
 public int rate(){return rate;}
 public int bits(){return encoding==AudioFormat.ENCODING_PCM_16BIT?16:encoding==AudioFormat.ENCODING_PCM_24BIT_PACKED?24:32;}
 void pump()throws IOException{
  long deadline=System.nanoTime()+10_000_000_000L;
  try{
   while(!closed&&!outputEnd){
    if(!inputEnd){int slot=codec.dequeueInputBuffer(0);if(slot>=0){ByteBuffer input=codec.getInputBuffer(slot);if(input==null)throw new IOException("解码输入不可用");int size=extractor.readSampleData(input,0);if(size<0){codec.queueInputBuffer(slot,0,0,0,MediaCodec.BUFFER_FLAG_END_OF_STREAM);inputEnd=true;}else{codec.queueInputBuffer(slot,0,size,extractor.getSampleTime(),0);extractor.advance();}}}
    int slot=codec.dequeueOutputBuffer(info,10000);
    if(slot==MediaCodec.INFO_OUTPUT_FORMAT_CHANGED){MediaFormat f=codec.getOutputFormat();int newRate=f.getInteger(MediaFormat.KEY_SAMPLE_RATE),newChannels=f.getInteger(MediaFormat.KEY_CHANNEL_COUNT);if(buffer!=null&&(newRate!=rate||newChannels!=channels))throw new IOException("音源中途改变格式");rate=newRate;channels=newChannels;encoding=f.containsKey(MediaFormat.KEY_PCM_ENCODING)?f.getInteger(MediaFormat.KEY_PCM_ENCODING):AudioFormat.ENCODING_PCM_16BIT;checkFormat();
     if(encoding!=AudioFormat.ENCODING_PCM_FLOAT&&encoding!=AudioFormat.ENCODING_PCM_16BIT&&encoding!=AudioFormat.ENCODING_PCM_24BIT_PACKED&&encoding!=AudioFormat.ENCODING_PCM_32BIT)throw new IOException("解码器输出格式不支持");}
    if(slot>=0){if(info.size==0){codec.releaseOutputBuffer(slot,false);if((info.flags&MediaCodec.BUFFER_FLAG_END_OF_STREAM)!=0)outputEnd=true;continue;}index=slot;buffer=codec.getOutputBuffer(slot);if(buffer==null)throw new IOException("解码输出不可用");buffer.position(info.offset);buffer.limit(info.offset+info.size);buffer.order(ByteOrder.LITTLE_ENDIAN);return;}
    if(System.nanoTime()>deadline)throw new IOException("解码器无进展");
   }
  }catch(RuntimeException e){throw new IOException("解码器停止："+e.getMessage(),e);}
 }
 double sample(){
  if(encoding==AudioFormat.ENCODING_PCM_FLOAT){float v=buffer.getFloat();return Float.isFinite(v)?v:0;}
  if(encoding==AudioFormat.ENCODING_PCM_16BIT)return buffer.getShort()/32768.0;
  if(encoding==AudioFormat.ENCODING_PCM_32BIT)return buffer.getInt()/2147483648.0;
  int v=(buffer.get()&255)|((buffer.get()&255)<<8)|(buffer.get()<<16);return v/8388608.0;
 }
 public int read(double[] output,int frames)throws IOException{
  if(frames<0||output.length<frames*2)throw new IOException("Invalid decoded block");int count=0;
  while(count<frames&&!closed){
   if(index<0){if(outputEnd)break;pump();if(index<0)break;}
   int bytes=(encoding==AudioFormat.ENCODING_PCM_16BIT?2:encoding==AudioFormat.ENCODING_PCM_24BIT_PACKED?3:4)*channels;
   if(buffer.remaining()<bytes){if(buffer.hasRemaining())throw new IOException("解码PCM未按帧对齐");boolean end=(info.flags&MediaCodec.BUFFER_FLAG_END_OF_STREAM)!=0;codec.releaseOutputBuffer(index,false);index=-1;if(end)outputEnd=true;continue;}
   double left=sample();output[count*2]=left;output[count*2+1]=channels==2?sample():left;count++;
  }return count;
 }
 public void close(){if(closed)return;closed=true;if(codec!=null){try{codec.stop();}catch(Exception ignored){}try{codec.release();}catch(Exception ignored){}}extractor.release();}
}
