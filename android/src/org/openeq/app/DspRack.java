package org.openeq.app;
import java.util.ArrayList;
import org.openeq.dsp.*;

/** Shared by the WAV player and privileged real-time bridge. */
final class DspRack {
 static void validate(Preset p)throws Exception{RackConfig.parse(p.processing);}
 final int rate,fadeFrames;Chain current,old,pending;int transition;double left,right;
 DspRack(int rate,Preset p){this.rate=rate;fadeFrames=rate/50;current=new Chain(p);}
 void configure(Preset p){pending=new Chain(p);}
 void process(double l,double r){if(old==null&&pending!=null){old=current;current=pending;pending=null;transition=0;}if(!Double.isFinite(l))l=0;if(!Double.isFinite(r))r=0;current.process(l,r);left=current.stage.left;right=current.stage.right;if(old!=null){old.process(l,r);double mix=(double)transition/fadeFrames;left=old.stage.left*(1-mix)+left*mix;right=old.stage.right*(1-mix)+right*mix;if(++transition>=fadeFrames)old=null;}}
 long clipped(){return current.stage.clippedFrames;}
 final class Chain {final Biquad[] left,right;final StereoStage stage;final RackConfig config;final SpatialStage spatial;final double dcA;double xl,yl,xr,yr;
  Chain(Preset p){
   try{config=RackConfig.parse(p.processing);}catch(Exception e){throw new IllegalArgumentException(e);}
   stage=new StereoStage(rate,Math.max(p.leftDelay,p.rightDelay));dcA=Math.exp(-2*Math.PI*20/rate);
   spatial=config.spatial.enabled?new SpatialStage(config.spatial,rate):null;
   ArrayList<Biquad> l=new ArrayList<>(),r=new ArrayList<>();
   if(p.enabled)for(Preset.Band b:p.bands)if(b.enabled&&b.hz<rate/2.0){if(!b.channel.equals("right"))l.add(b.filter(rate));if(!b.channel.equals("left"))r.add(b.filter(rate));}
   left=l.toArray(new Biquad[0]);right=r.toArray(new Biquad[0]);
   stage.configure((p.enabled?p.preamp:0)+p.output,p.balance,p.leftDelay,p.rightDelay,p.invertLeft,p.invertRight,p.protection,p.ceiling);
   stage.compressor(config.comp,config.threshold,config.ratio,config.knee,config.makeup,config.attack,config.release);
  }
  void process(double l,double r){
   if(config.dc){yl=l-xl+dcA*yl;xl=l;l=yl;yr=r-xr+dcA*yr;xr=r;r=yr;}
   for(Biquad b:left)l=b.process(l);for(Biquad b:right)r=b.process(r);
   if(spatial!=null){spatial.process(l,r);l=spatial.left;r=spatial.right;}
   if(config.ll!=1||config.lr!=0||config.rl!=0||config.rr!=1||config.width!=1){double a=l*config.ll+r*config.lr,b=l*config.rl+r*config.rr;if(config.width!=1){double m=(a+b)*.5,s=(a-b)*.5*config.width;a=m+s;b=m-s;}l=a;r=b;}
   stage.process(l,r);
  }
 }
}
