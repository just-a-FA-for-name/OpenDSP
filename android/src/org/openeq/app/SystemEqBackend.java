package org.openeq.app;
import android.media.audiofx.*;
import org.json.*;

/** Global Android effects: no capture/replay and no new audio output stream. */
final class SystemEqBackend implements AutoCloseable {
 final DynamicsProcessing dynamics;final Equalizer equalizer;final int bands;String limitation="";
 SystemEqBackend(){
  DynamicsProcessing advanced=null;Equalizer basic=null;
  for(int count:new int[]{64,32}){
   try{
    DynamicsProcessing.Config config=new DynamicsProcessing.Config.Builder(DynamicsProcessing.VARIANT_FAVOR_FREQUENCY_RESOLUTION,2,true,count,false,0,false,0,true).setPreferredFrameDuration(10).build();
    advanced=new DynamicsProcessing(0,0,config);
    if(!advanced.hasControl()||advanced.getChannelCount()!=2)throw new IllegalStateException("System effect is not controllable stereo");
    advanced.setEnabled(false);break;
   }catch(RuntimeException e){if(advanced!=null)advanced.release();advanced=null;limitation="系统高级EQ不可用，使用设备原生频段。";}
  }
  if(advanced==null){basic=new Equalizer(0,0);if(!basic.hasControl()){basic.release();throw new IllegalStateException("系统音效控制权被其他应用占用");}basic.setEnabled(false);}
  dynamics=advanced;equalizer=basic;bands=advanced!=null?advanced.getConfig().getPreEqBandCount():basic.getNumberOfBands();
 }
 boolean mapped(){return dynamics!=null;}
 boolean controlled(){return dynamics!=null?dynamics.hasControl():equalizer.hasControl();}
 void apply(Preset p,int rate,JSONObject nativeProfile)throws Exception{
  if(!controlled())throw new IllegalStateException("系统音效控制权被其他应用接管");
  if(dynamics!=null){
   SystemEqProjection curve=new SystemEqProjection(p,rate,bands);
   for(int channel=0;channel<2;channel++){
    DynamicsProcessing.Eq eq=new DynamicsProcessing.Eq(true,true,bands);float[] gains=channel==0?curve.left:curve.right;
    for(int i=0;i<bands;i++)eq.setBand(i,new DynamicsProcessing.EqBand(true,curve.cutoff[i],gains[i]));
    dynamics.setPreEqByChannelIndex(channel,eq);
    dynamics.setInputGainbyChannel(channel,channel==0?curve.leftGain:curve.rightGain);
    dynamics.setLimiterByChannelIndex(channel,new DynamicsProcessing.Limiter(true,p.protection,0,1,100,20,(float)p.ceiling,0));
   }
   String unsupported=SystemEqProjection.unsupported(p);
   limitation="幅频近似，不是原生PEQ/相位等价；限幅为系统算法。"+(unsupported.isEmpty()?"":"此路径未启用："+unsupported+"。")+(curve.clipped>0?"部分系统频段超范围，已限幅到±24dB。":"");
  }else{
   short[] range=equalizer.getBandLevelRange();JSONArray values=nativeProfile==null?null:nativeProfile.optJSONArray("bands");
   boolean compatible=values!=null&&values.length()==bands;
   if(compatible)for(short i=0;i<bands;i++)if(values.getJSONObject(i).getInt("frequency_mhz")!=equalizer.getCenterFreq(i))compatible=false;
   for(short i=0;i<bands;i++)equalizer.setBandLevel(i,(short)(compatible?Math.max(range[0],Math.min(range[1],values.getJSONObject(i).getInt("level_mb"))):0));
  }
 }
 void enabled(boolean enabled){int result=dynamics!=null?dynamics.setEnabled(enabled):equalizer.setEnabled(enabled);if(result!=AudioEffect.SUCCESS)throw new IllegalStateException("系统未接受音效开关");}
 JSONObject snapshot()throws Exception{
  JSONObject state=new JSONObject().put("backend",mapped()?"system_spectral_eq":"system_native_eq").put("bands",bands).put("has_control",controlled()).put("enabled",dynamics!=null?dynamics.getEnabled():equalizer.getEnabled()).put("limitation",limitation);
  JSONArray rows=new JSONArray();
  if(dynamics!=null){state.put("input_gain_left_db",dynamics.getInputGainByChannelIndex(0)).put("input_gain_right_db",dynamics.getInputGainByChannelIndex(1));for(int i=0;i<bands;i++){DynamicsProcessing.EqBand left=dynamics.getPreEqBandByChannelIndex(0,i),right=dynamics.getPreEqBandByChannelIndex(1,i);rows.put(new JSONObject().put("cutoff_hz",left.getCutoffFrequency()).put("left_db",left.getGain()).put("right_db",right.getGain()));}}
  else for(short i=0;i<bands;i++)rows.put(new JSONObject().put("frequency_mhz",equalizer.getCenterFreq(i)).put("level_mb",equalizer.getBandLevel(i)));
  if(equalizer!=null){short[] range=equalizer.getBandLevelRange();state.put("minimum_mb",range[0]).put("maximum_mb",range[1]);}
  return state.put("bands_readback",rows);
 }
 void nativeBand(int index,int level){if(equalizer==null)throw new IllegalStateException("当前为高级系统EQ，请编辑主EQ");short[] range=equalizer.getBandLevelRange();equalizer.setBandLevel((short)index,(short)Math.max(range[0],Math.min(range[1],level)));}
 public void close(){try{if(controlled())enabled(false);}catch(Exception ignored){}if(dynamics!=null)dynamics.release();if(equalizer!=null)equalizer.release();}
}
