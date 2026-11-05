import org.openeq.dsp.Biquad;
import org.openeq.dsp.StereoStage;
public class DspTest {
  static void near(double actual,double expected,double tolerance) {if(Math.abs(actual-expected)>tolerance)throw new AssertionError(actual+" != "+expected);}
  public static void main(String[] args) {
    for(double rate:new double[]{44100,48000,96000,192000,384000}) {
      Biquad flat=new Biquad("peak",1000,0,0.707,rate);
      for(int n=0;n<10000;n++){double x=Math.sin(n*0.13)*0.2;near(flat.process(x),x,1e-10);}
      Biquad peak=new Biquad("peak",1000,6,2,rate);
      near(20*Math.log10(peak.magnitude(1000,rate)),6,1e-8);
      // Independent time-domain steady-state gain check.
      double in=0,out=0;
      for(int n=0;n<(int)rate;n++){double x=0.1*Math.sin(2*Math.PI*1000*n/rate);double y=peak.process(x);if(n>rate/2){in+=x*x;out+=y*y;}}
      near(10*Math.log10(out/in),6,0.002);
      near(new Biquad("notch",1000,0,1,rate).magnitude(1000,rate),0,1e-8);
      near(20*Math.log10(new Biquad("low_shelf",1000,6,0.707,rate).magnitude(1,rate)),6,0.001);
      near(20*Math.log10(new Biquad("high_shelf",1000,6,0.707,rate).magnitude(rate/2-1,rate)),6,0.001);
      for(String type:new String[]{"peak","low_shelf","high_shelf","low_pass","high_pass","notch","all_pass"})
        for(double q:new double[]{0.1,0.707,30}){
          Biquad b=new Biquad(type,19000,24,q,rate);
          for(int n=0;n<20000;n++){double y=b.process(n==0?1:0);if(!Double.isFinite(y)||Math.abs(y)>100)throw new AssertionError("Unstable "+type);}
          if(type.equals("all_pass"))near(b.magnitude(1300,rate),1,1e-8);
        }
    }
    boolean rejected=false;try{new Biquad("unknown",1000,0,1,48000);}catch(IllegalArgumentException e){rejected=true;}
    if(!rejected)throw new AssertionError("Unknown filter accepted");
    StereoStage stereo=new StereoStage(48000);stereo.configure(-6,0,0,0,false,true,false,-1);
    for(int n=0;n<10000;n++)stereo.process(0.5,0.5);near(stereo.left,0.5*Math.pow(10,-6.0/20),1e-10);near(stereo.right,-stereo.left,1e-10);
    stereo.configure(0,1,0,0,false,false,false,-1);for(int n=0;n<10000;n++)stereo.process(0.5,0.5);near(stereo.left,0,1e-10);near(stereo.right,0.5,1e-10);
    stereo=new StereoStage(48000);stereo.configure(0,0,1,0,false,false,false,0);for(int n=0;n<10000;n++)stereo.process(0,0);
    stereo.process(0.2,0.2);near(stereo.left,0,1e-10);near(stereo.right,0.2,1e-10);for(int n=0;n<47;n++){stereo.process(0,0);near(stereo.left,0,1e-10);}stereo.process(0,0);near(stereo.left,0.2,1e-10);
    stereo=new StereoStage(48000);stereo.configure(0,0,0,0,false,false,true,-1);
    for(int n=0;n<100000;n++){stereo.process(10*Math.sin(n*0.13),5*Math.sin(n*0.13));if(Math.abs(stereo.left)>Math.pow(10,-1.0/20)+1e-10)throw new AssertionError("Peak ceiling exceeded");near(stereo.right,stereo.left/2,1e-10);}near(stereo.clippedFrames,0,0);
    System.out.println("PASS: biquad identity/response/time-domain/stability at 5 rates through384k; output gain, polarity, balance, 1 ms delay, linked peak ceiling");
  }
}
