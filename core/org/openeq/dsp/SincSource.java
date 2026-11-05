package org.openeq.dsp;
import java.io.*;
/** Streaming 64-tap windowed sinc with interpolated phase coefficients; no per-frame allocation. */
public final class SincSource implements PcmSource {
 final PcmSource source;final int rate;final boolean adaptive,bypass;final double ratio;double position,ppm,step;
 final double[][] kernel=new double[257][64],delta=new double[256][64];final double[] ring=new double[16384],input=new double[2048];long loaded,total=-1;
 public SincSource(PcmSource source,int outputRate,boolean adaptive){
  if(outputRate<8000||outputRate>768000)throw new IllegalArgumentException("Invalid rate");this.source=source;rate=outputRate;this.adaptive=adaptive;ratio=source.rate()/(double)rate;step=ratio;bypass=!adaptive&&source.rate()==rate;
  double cutoff=Math.min(1,rate/(double)source.rate())*.94;
  if(!bypass){
   for(int phase=0;phase<=256;phase++){double fraction=phase/256.0,sum=0;for(int t=0;t<64;t++){double x=t-31-fraction,window=.42+.5*Math.cos(Math.PI*x/32)+.08*Math.cos(2*Math.PI*x/32);double v=Math.abs(x)<1e-12?cutoff:Math.sin(Math.PI*cutoff*x)/(Math.PI*x);kernel[phase][t]=v*window;sum+=kernel[phase][t];}for(int t=0;t<64;t++)kernel[phase][t]/=sum;}
   for(int phase=0;phase<256;phase++)for(int t=0;t<64;t++)delta[phase][t]=kernel[phase+1][t]-kernel[phase][t];
  }
 }
 public int rate(){return rate;}public int bits(){return source.bits();}
 public void clockPpm(double value){if(!Double.isFinite(value))throw new IllegalArgumentException("Non-finite clock correction");if(adaptive){ppm+=((Math.max(-2000,Math.min(2000,value)))-ppm)/16;step=ratio*(1+ppm/1000000);}}
 boolean ensure(long frame)throws IOException{
  while(loaded<=frame&&total<0){int n=source.read(input,1024);if(n==0){total=loaded;break;}for(int i=0;i<n;i++){int at=(int)((loaded++%8192)*2);ring[at]=input[i*2];ring[at+1]=input[i*2+1];}}
  return total<0||position<total;
 }
 public int read(double[] out,int frames)throws IOException{
  if(frames<0||out.length<frames*2)throw new IOException("Invalid output block");if(bypass)return source.read(out,frames);
  int done=0;
  while(done<frames){
   long base=(long)position;if(!ensure(base+32))break;double fractional=(position-base)*256;int phase=Math.min(255,(int)fractional);double blend=fractional-phase,l=0,r=0;double[] weights=kernel[phase],differences=delta[phase];
   if(base>=31&&base+32<loaded&&base-31>=loaded-8192){
    int at=(int)((base-31)&8191)*2;
    for(int t=0;t<64;t++){double weight=weights[t]+blend*differences[t];l+=ring[at]*weight;r+=ring[at+1]*weight;at=(at+2)&16383;}
   }else for(int t=0;t<64;t++){long index=base+t-31;if(index<0||index>=loaded)continue;if(index<loaded-8192)throw new IOException("Resampler history lost");int at=(int)(index&8191)*2;double weight=weights[t]+blend*differences[t];l+=ring[at]*weight;r+=ring[at+1]*weight;}
   out[done*2]=l;out[done*2+1]=r;done++;position+=step;
  }return done;
 }
 public void close()throws IOException{source.close();}
}
