package org.openeq.app;
import org.json.*;
import org.openeq.dsp.SpatialStage;
/** Typed validated snapshot of desktop-compatible output extensions. */
final class RackConfig {
 boolean dc,comp;double ll=1,lr,rl,rr=1,width=1,threshold=-18,ratio=4,attack=10,release=120,knee=6,makeup;
 SpatialStage.Config spatial=new SpatialStage.Config();
 static double number(JSONObject j,String key,double value,double low,double high)throws Exception{return Preset.bounded(j.optDouble(key,value),low,high,key);}
 static RackConfig parse(JSONObject root)throws Exception{
  RackConfig r=new RackConfig();r.dc=root.optBoolean("dc_block",false);
  JSONObject m=root.optJSONObject("matrix");if(m!=null){r.ll=number(m,"ll",1,-2,2);r.lr=number(m,"lr",0,-2,2);r.rl=number(m,"rl",0,-2,2);r.rr=number(m,"rr",1,-2,2);r.width=number(m,"width",1,0,2);}
  JSONObject c=root.optJSONObject("compressor");if(c!=null){r.comp=c.optBoolean("enabled",false);r.threshold=number(c,"threshold_db",-18,-60,0);r.ratio=number(c,"ratio",4,1,20);r.attack=number(c,"attack_ms",10,.1,200);r.release=number(c,"release_ms",120,5,2000);r.knee=number(c,"knee_db",6,0,24);r.makeup=number(c,"makeup_db",0,-12,24);}
  JSONObject s=root.optJSONObject("spatial");if(s!=null){
   SpatialStage.Config x=r.spatial;x.enabled=s.optBoolean("enabled",false);x.mono=s.optBoolean("mono",false);x.mix=number(s,"mix",1,0,1);
   x.midGain=number(s,"mid_gain_db",0,-12,6);x.sideGain=number(s,"side_gain_db",0,-18,6);
   x.lowWidth=number(s,"low_width",1,0,2);x.midWidth=number(s,"mid_width",1,0,2);x.highWidth=number(s,"high_width",1,0,2);
   x.lowHz=number(s,"low_crossover_hz",180,50,1000);x.highHz=number(s,"high_crossover_hz",3500,500,10000);
   x.crossfeed=number(s,"crossfeed",0,0,.45);x.crossHz=number(s,"crossfeed_hz",700,100,1500);x.delayMs=number(s,"crossfeed_delay_ms",.25,0,.8);
   x.mid=filters(s.optJSONArray("mid_filters"));x.side=filters(s.optJSONArray("side_filters"));x.validate();
  }return r;
 }
 static SpatialStage.Filter[] filters(JSONArray values)throws Exception{
  if(values==null)return new SpatialStage.Filter[0];if(values.length()>16)throw new Exception("M/S最多16段");
  SpatialStage.Filter[] bank=new SpatialStage.Filter[values.length()];
  for(int i=0;i<bank.length;i++){JSONObject j=values.getJSONObject(i);if(!j.optString("channel","both").equals("both"))throw new Exception("M/S通道必须为both");SpatialStage.Filter b=new SpatialStage.Filter();b.type=j.getString("type");b.hz=j.getDouble("frequency_hz");b.gain=j.getDouble("gain_db");b.q=j.getDouble("q");b.enabled=j.optBoolean("enabled",true);bank[i]=b;}return bank;
 }
}
