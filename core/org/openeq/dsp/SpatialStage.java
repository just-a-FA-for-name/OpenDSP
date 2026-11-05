package org.openeq.dsp;
/** Same M/S and crossfeed topology as Windows rc23. Prepared outside the sample loop. */
public final class SpatialStage {
 public static final class Filter {
  public String type="peak";public double hz=1000,gain,q=.707;public boolean enabled=true;
 }
 public static final class Config {
  public boolean enabled,mono;public double mix=1,midGain,sideGain,lowWidth=1,midWidth=1,highWidth=1,lowHz=180,highHz=3500,crossfeed,crossHz=700,delayMs=.25;
  public Filter[] mid=new Filter[0],side=new Filter[0];
  public void validate(){
   range(mix,0,1);range(midGain,-12,6);range(sideGain,-18,6);range(lowWidth,0,2);range(midWidth,0,2);range(highWidth,0,2);
   range(lowHz,50,1000);range(highHz,500,10000);if(highHz<lowHz*2)throw new IllegalArgumentException("High crossover must be at least twice low");
   range(crossfeed,0,.45);range(crossHz,100,1500);range(delayMs,0,.8);
   for(Filter[] bank:new Filter[][]{mid,side}){if(bank==null||bank.length>16)throw new IllegalArgumentException("Maximum 16 M/S filters");
    for(Filter f:bank){range(f.hz,20,20000);new Biquad(f.type,f.hz,f.gain,f.q,48000);}
   }
  }
 }
 static void range(double n,double lo,double hi){if(!Double.isFinite(n)||n<lo||n>hi)throw new IllegalArgumentException("Invalid spatial parameter");}
 final boolean active,mono,width;final double mix,mg,sg,lw,mw,hw,lp,hp,cp,amount;final double[] delay;final int frames;
 final Biquad[] mid,side;int index;double low,high,cross;public double left,right;
 static Biquad[] prepare(Filter[] bank,int rate){java.util.ArrayList<Biquad> out=new java.util.ArrayList<>();for(Filter f:bank)if(f.enabled&&f.hz<rate/2.0)out.add(new Biquad(f.type,f.hz,f.gain,f.q,rate));return out.toArray(new Biquad[0]);}
 public SpatialStage(Config c,int rate){
  c.validate();active=c.enabled&&c.mix>0;mono=c.mono;mix=c.mix;mg=Math.pow(10,c.midGain/20);sg=Math.pow(10,c.sideGain/20);
  lw=c.lowWidth;mw=c.midWidth;hw=c.highWidth;width=lw!=1||mw!=1||hw!=1;lp=Math.exp(-2*Math.PI*c.lowHz/rate);hp=Math.exp(-2*Math.PI*c.highHz/rate);cp=Math.exp(-2*Math.PI*c.crossHz/rate);amount=c.crossfeed;
  frames=active&&amount>0?(int)Math.round(c.delayMs*rate/1000):0;delay=new double[frames+1];mid=active?prepare(c.mid,rate):new Biquad[0];side=active?prepare(c.side,rate):new Biquad[0];
 }
 public void process(double l,double r){
  if(!active){left=l;right=r;return;}double m=(l+r)*.5,s=(l-r)*.5;
  for(Biquad b:mid)m=b.process(m);for(Biquad b:side)s=b.process(s);m*=mg;s*=sg;
  if(width){low=(1-lp)*s+lp*low;high=(1-hp)*s+hp*high;s=lw*low+mw*(high-low)+hw*(s-high);}
  if(mono)s=0;
  if(amount>0){cross=(1-cp)*s+cp*cross;delay[index]=cross;s-=2*amount*delay[(index-frames+delay.length)%delay.length];index=(index+1)%delay.length;}
  left=l*(1-mix)+(m+s)*mix;right=r*(1-mix)+(m-s)*mix;
 }
}
