package org.openeq.app;
import org.openeq.dsp.Biquad;
import java.util.*;
/** Explicit magnitude-only projection for Android's system spectral EQ, not PEQ/phase parity. */
final class SystemEqProjection {
 final float[] cutoff,left,right;final float leftGain,rightGain;final int clipped;
 SystemEqProjection(Preset p,int rate,int count){
  cutoff=new float[count];left=new float[count];right=new float[count];int clipping=0;
  ArrayList<Biquad> l=new ArrayList<>(),r=new ArrayList<>();
  if(p.enabled)for(Preset.Band b:p.bands)if(b.enabled&&b.hz<rate*.5){if(!b.channel.equals("right"))l.add(b.filter(rate));if(!b.channel.equals("left"))r.add(b.filter(rate));}
  double high=Math.min(20000,rate*.45),previous=20;
  for(int i=0;i<count;i++){
   cutoff[i]=(float)(20*Math.pow(high/20,i/(count-1d)));double hz=i==0?20:Math.sqrt(previous*cutoff[i]);previous=cutoff[i];
   double a=0,b=0;for(Biquad f:l)a+=20*Math.log10(Math.max(1e-12,f.magnitude(hz,rate)));for(Biquad f:r)b+=20*Math.log10(Math.max(1e-12,f.magnitude(hz,rate)));
   if(Math.abs(a)>24||Math.abs(b)>24)clipping++;left[i]=(float)Math.max(-24,Math.min(24,a));right[i]=(float)Math.max(-24,Math.min(24,b));
  }
  double base=(p.enabled?p.preamp:0)+p.output;
  leftGain=(float)Math.max(-96,base+20*Math.log10(Math.max(1e-8,Math.min(1,1-p.balance))));
  rightGain=(float)Math.max(-96,base+20*Math.log10(Math.max(1e-8,Math.min(1,1+p.balance))));clipped=clipping;
 }
 static String unsupported(Preset p){
  ArrayList<String> list=new ArrayList<>();
  if(p.leftDelay!=0||p.rightDelay!=0)list.add("延时");if(p.invertLeft||p.invertRight)list.add("反相");
  try{RackConfig c=RackConfig.parse(p.processing);if(c.comp)list.add("压缩器");if(c.spatial.enabled)list.add("高级空间");if(c.dc)list.add("DC");
   if(c.ll!=1||c.lr!=0||c.rl!=0||c.rr!=1||c.width!=1)list.add("矩阵");
  }catch(Exception ignored){}
  for(Preset.Band b:p.bands)if(b.enabled&&b.type.equals("all_pass")){list.add("全通相位");break;}
  return String.join("、",list);
 }
}
