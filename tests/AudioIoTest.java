import org.openeq.dsp.*;
import java.io.*;
import java.nio.*;
public class AudioIoTest {
 static void check(boolean yes,String message){if(!yes)throw new AssertionError(message);}
 static byte[] wav(int encoding,int bits,byte[] data){ByteBuffer b=ByteBuffer.allocate(44+data.length).order(ByteOrder.LITTLE_ENDIAN);b.put("RIFF".getBytes()).putInt(36+data.length).put("WAVEfmt ".getBytes()).putInt(16).putShort((short)encoding).putShort((short)2).putInt(384000).putInt(384000*2*bits/8).putShort((short)(2*bits/8)).putShort((short)bits).put("data".getBytes()).putInt(data.length).put(data);return b.array();}
 static byte[] dsf(int rate){ByteBuffer b=ByteBuffer.allocate(124).order(ByteOrder.LITTLE_ENDIAN);b.put("DSD ".getBytes()).putLong(28).putLong(124).putLong(0).put("fmt ".getBytes()).putLong(52).putInt(1).putInt(0).putInt(2).putInt(2).putInt(rate).putInt(1).putLong(128).putInt(16).putInt(0).put("data".getBytes()).putLong(44);b.put((byte)1).put((byte)2);for(int i=2;i<16;i++)b.put((byte)0x69);b.put((byte)4).put((byte)8);for(int i=2;i<16;i++)b.put((byte)0x69);return b.array();}
 static final class Tone implements PcmSource {final int rate,total;final double hz;int frame;Tone(int rate,int total,double hz){this.rate=rate;this.total=total;this.hz=hz;}public int rate(){return rate;}public int bits(){return 32;}public int read(double[] out,int count){int n=Math.min(count,total-frame);for(int i=0;i<n;i++){double s=.2*Math.sin(2*Math.PI*hz*frame++/rate);out[i*2]=s;out[i*2+1]=-s;}return n;}public void close(){}}
 static double residual(double hz,double ppm)throws Exception{
  SincSource source=new SincSource(new Tone(48000,100000,hz),384000,true);for(int i=0;i<1000;i++)source.clockPpm(ppm);
  double[] block=new double[2048];double xx=0,ss=0,cc=0,sc=0,xs=0,xc=0;long frame=0;int n;
  while((n=source.read(block,1024))>0)for(int i=0;i<n;i++,frame++)if(frame>4096&&frame<760000){
   double angle=2*Math.PI*hz*(1+ppm/1e6)*frame/384000,s=Math.sin(angle),c=Math.cos(angle),x=block[2*i];xx+=x*x;ss+=s*s;cc+=c*c;sc+=s*c;xs+=x*s;xc+=x*c;
  }
  double det=ss*cc-sc*sc,a=(xs*cc-xc*sc)/det,b=(xc*ss-xs*sc)/det;return 10*Math.log10(Math.max(1e-16,(xx-a*xs-b*xc)/xx));
 }
 static void longClockRun()throws Exception{
  PcmSource constant=new PcmSource(){int read;public int rate(){return 48000;}public int bits(){return 32;}public int read(double[] out,int frames){int n=Math.min(frames,48000*60-read);for(int i=0;i<n;i++){out[2*i]=.125;out[2*i+1]=-.125;}read+=n;return n;}public void close(){}};
  SincSource source=new SincSource(constant,384000,true);double[] out=new double[2048];long total=0;int n,blocks=0;
  while((n=source.read(out,1024))>0){if(blocks++%187==0)source.clockPpm(blocks%1122<561?700:-700);
   if(total>4096&&total<384000*59L)for(int i=0;i<n;i++){check(Math.abs(out[2*i]-.125)<1e-11&&Math.abs(out[2*i+1]+.125)<1e-11,"DC/channel continuity under changing clocks");}total+=n;
  }
  check(Math.abs(total-384000*60L)<384000*.15,"Long clock run frame bounds");
  try{source.clockPpm(Double.NaN);throw new AssertionError("NaN clock accepted");}catch(IllegalArgumentException expected){}
 }
 public static void main(String[] args)throws Exception{
  double[] out=new double[4096];ByteBuffer values=ByteBuffer.allocate(16).order(ByteOrder.LITTLE_ENDIAN).putInt(1).putInt(-1).putInt(Integer.MAX_VALUE).putInt(Integer.MIN_VALUE);
  WavSource w=new WavSource(new ByteArrayInputStream(wav(1,32,values.array())));check(w.rate()==384000&&w.read(out,2)==2,"384k PCM32 header");check(out[0]==1/2147483648.0&&out[1]==-1/2147483648.0,"PCM32 low bits preserved");check(out[2]==Integer.MAX_VALUE/2147483648.0&&out[3]==-1,"PCM32 signed endpoints");
  values=ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putFloat(Float.NaN).putFloat(.125f);w=new WavSource(new ByteArrayInputStream(wav(3,32,values.array())));w.read(out,1);check(out[0]==0&&out[1]==.125,"float WAV sanitization");
  for(int rate:new int[]{2822400,5644800}){DsfSource source=new DsfSource(new ByteArrayInputStream(dsf(rate)));byte[] payload=new byte[32];check(source.carrierRate==rate/16&&source.read(payload,8)==8,"DSD carrier rate");check((payload[0]&255)==0x40&&(payload[1]&255)==0x80&&(payload[2]&255)==0x10&&(payload[3]&255)==0x20,"DSF LSB order to DoP words");check(source.read(payload,8)==0,"DSF EOF");}
  SincSource up=new SincSource(new Tone(48000,4800,1000),384000,false);int frames=0,n;double power=0;long measured=0;
  while((n=up.read(out,2048))>0){for(int i=0;i<n;i++){if(frames+i>1000&&frames+i<37000){power+=out[i*2]*out[i*2];measured++;}check(Math.abs(out[i*2]+out[i*2+1])<1e-12,"resampler channel isolation");}frames+=n;}
  check(Math.abs(frames-38400)<=1&&Math.abs(10*Math.log10(power/measured/.02))<.03,"48k->384k rate and passband");
  SincSource down=new SincSource(new Tone(96000,19200,35000),48000,false);frames=0;power=0;measured=0;
  while((n=down.read(out,2048))>0){for(int i=0;i<n;i++)if(frames+i>300&&frames+i<9200){power+=out[i*2]*out[i*2];measured++;}frames+=n;}
  check(Math.sqrt(power/measured)<.001,"downsampling rejects above-Nyquist tone");
  SpatialStage.Config config=new SpatialStage.Config();config.enabled=true;config.lowWidth=0;config.highWidth=1.65;config.crossfeed=.25;
  SpatialStage spatial=new SpatialStage(config,384000);
  for(int i=0;i<20000;i++){double x=.1*Math.sin(i*.03);spatial.process(x,x);check(Math.abs(spatial.left-x)<1e-12&&Math.abs(spatial.right-x)<1e-12,"mono preservation");}
  StereoStage stage=new StereoStage(384000,0);stage.configure(0,0,0,0,false,false,true,-1);stage.process(Double.NaN,Double.POSITIVE_INFINITY);check(Double.isFinite(stage.left)&&Double.isFinite(stage.right),"output finite handling");
  check(!CaptureMeter.hasSignal(CaptureMeter.rmsDb(0,4800)),"silence is not a captured signal");
  double level=CaptureMeter.rmsDb(4800*2*.01,4800);check(Math.abs(level+20)<1e-10,"two-channel RMS level");
  check(CaptureMeter.phase(true,level,0).equals("buffering"),"USB claim alone is not processing");
  check(CaptureMeter.phase(true,Double.NEGATIVE_INFINITY,4800).equals("waiting")&&CaptureMeter.phase(true,level,4800).equals("processing")&&CaptureMeter.phase(true,Double.NEGATIVE_INFINITY,9600).equals("waiting"),"signal transitions recover in both directions");
  VolumeRamp volume=new VolumeRamp(384000,1);volume.target(0);double previous=1;
  for(int i=0;i<7680;i++){double gain=volume.next();check(gain<=previous&&gain>=0,"Mute ramp monotonic");previous=gain;}check(volume.current()==0,"Mute reaches exact zero");
  volume.target(.37);previous=0;for(int i=0;i<7680;i++){double gain=volume.next();check(gain>=previous&&gain<=.37,"Volume ramp monotonic");previous=gain;}check(volume.current()==.37,"Volume target exact");
  volume.target(1);for(int i=0;i<100;i++)volume.next();volume.target(.1);for(int i=0;i<7680;i++)volume.next();check(volume.current()==.1,"Mid-ramp retarget");
  try{volume.target(Double.NaN);throw new AssertionError("Invalid gain accepted");}catch(IllegalArgumentException expected){}
  Biquad tail=new Biquad("peak",1000,3,1,48000);tail.process(1);double last=1;for(int i=0;i<100000;i++)last=tail.process(0);check(last==0,"Silent filter tail terminates");
  for(double hz:new double[]{997,10003,19001}){double error=residual(hz,137);check(error< -95,"Adaptive SRC residual at "+hz+"Hz: "+error);System.out.println("Adaptive SRC "+hz+"Hz: "+error+" dBc (synthetic, not acoustic)");}
  check(residual(10003,-613)< -95,"Negative clock correction");longClockRun();
  System.out.println("PASS: PCM32/float WAV, DSF64/128 endian/EOF, sinc384k/passband/antialias, spatial mono, finite input");
 }
}
