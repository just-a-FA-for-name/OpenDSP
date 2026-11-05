package org.openeq.app;
import java.io.*;
import org.openeq.dsp.PcmSource;

/** Batches input into the native stereo converter; no per-frame JNI calls. */
final class NativeSincSource implements PcmSource {
 final PcmSource source;final int rate;final double[] input=new double[2048],converted=new double[8192];long handle;boolean eof;
 NativeSincSource(PcmSource source,int rate,boolean adaptive)throws IOException{this.source=source;this.rate=rate;handle=UsbNative.srcOpen(source.rate(),rate,adaptive);}
 public int rate(){return rate;}public int bits(){return source.bits();}
 void clockPpm(double value)throws IOException{UsbNative.srcClock(handle,value);}
 public int read(double[] output,int frames)throws IOException{
  if(handle==0||frames<0||output.length<frames*2)throw new IOException("SRC closed or invalid block");int done=0;
  while(done<frames){
   int count=UsbNative.srcRead(handle,converted,Math.min(4096,frames-done));
   if(count>0){System.arraycopy(converted,0,output,done*2,count*2);done+=count;continue;}
   if(eof)break;int n=source.read(input,1024);if(n==0){eof=true;UsbNative.srcFinish(handle);}else UsbNative.srcPush(handle,input,n);
  }return done;
 }
 public void close()throws IOException{long old=handle;handle=0;try{if(old!=0)UsbNative.srcClose(old);}finally{source.close();}}
}
