package org.openeq.app;
import android.app.*;
import android.content.*;
import android.widget.*;
import java.util.*;
import org.json.*;
import org.openeq.dsp.*;

/** Desktop-equivalent four linear paths; nonlinear stages are explicitly excluded. */
final class ResponsePanel {
 static final class Z {
  final double re,im;Z(double r,double i){re=r;im=i;}Z add(Z b){return new Z(re+b.re,im+b.im);}Z sub(Z b){return new Z(re-b.re,im-b.im);}
  Z mul(Z b){return new Z(re*b.re-im*b.im,re*b.im+im*b.re);}Z scale(double s){return new Z(re*s,im*s);}Z div(Z b){double d=b.re*b.re+b.im*b.im;return new Z((re*b.re+im*b.im)/d,(im*b.re-re*b.im)/d);}
  double abs(){return Math.hypot(re,im);}double phase(){return Math.atan2(im,re);}Z conj(){return new Z(re,-im);}
 }
 static final Z ONE=new Z(1,0),ZERO=new Z(0,0);
 static Z delay(double hz,int frames,int rate){double w=-2*Math.PI*hz*frames/rate;return new Z(Math.cos(w),Math.sin(w));}
 static Z low(double hz,double cutoff,int rate){double p=Math.exp(-2*Math.PI*cutoff/rate);return ONE.scale(1-p).div(ONE.sub(delay(hz,1,rate).scale(p)));}
 static Z bank(SpatialStage.Filter[] filters,double hz,int rate){Z value=ONE;for(SpatialStage.Filter f:filters)if(f.enabled&&f.hz<rate/2d){double[] r=new Biquad(f.type,f.hz,f.gain,f.q,rate).transfer(hz,rate);value=value.mul(new Z(r[0],r[1]));}return value;}
 static Z[] spatial(SpatialStage.Config s,double hz,int rate){
  if(!s.enabled||s.mix==0)return new Z[]{ONE,ZERO};
  Z m=bank(s.mid,hz,rate).scale(Math.pow(10,s.midGain/20)),side=bank(s.side,hz,rate).scale(Math.pow(10,s.sideGain/20));
  if(s.lowWidth!=1||s.midWidth!=1||s.highWidth!=1){Z l=low(hz,s.lowHz,rate),h=low(hz,s.highHz,rate);side=side.mul(l.scale(s.lowWidth).add(h.sub(l).scale(s.midWidth)).add(ONE.sub(h).scale(s.highWidth)));}
  if(s.mono)side=ZERO;
  if(s.crossfeed>0)side=side.mul(ONE.sub(low(hz,s.crossHz,rate).mul(delay(hz,(int)Math.round(s.delayMs*rate/1000),rate)).scale(2*s.crossfeed)));
  return new Z[]{ONE.scale(1-s.mix).add(m.add(side).scale(s.mix/2)),m.sub(side).scale(s.mix/2)};
 }
 static Z path(Preset p,RackConfig rack,double hz,int rate,int path){
  Z eq=ONE;if(p.enabled)for(Preset.Band b:p.bands)if(b.enabled&&b.hz<rate/2d&&(b.channel.equals("both")||b.channel.equals(path%2==0?"left":"right"))){double[] r=b.filter(rate).transfer(hz,rate);eq=eq.mul(new Z(r[0],r[1]));}
  if(rack.dc){Z z=delay(hz,1,rate);eq=eq.mul(ONE.sub(z).div(ONE.sub(z.scale(Math.exp(-2*Math.PI*20/rate)))));}
  double a=(1+rack.width)/2,b=(1-rack.width)/2;double[] matrix={a*rack.ll+b*rack.rl,a*rack.lr+b*rack.rr,b*rack.ll+a*rack.rl,b*rack.lr+a*rack.rr};
  Z[] s=spatial(rack.spatial,hz,rate);Z route=path%2==0?s[0].scale(matrix[path<2?0:2]).add(s[1].scale(matrix[path<2?1:3])):s[1].scale(matrix[path<2?0:2]).add(s[0].scale(matrix[path<2?1:3]));
  boolean left=path<2;double gain=Math.pow(10,((p.enabled?p.preamp:0)+p.output)/20)*Math.min(1,left?1-p.balance:1+p.balance)*((left?p.invertLeft:p.invertRight)?-1:1);
  return eq.mul(route).scale(gain).mul(delay(hz,(int)Math.round((left?p.leftDelay:p.rightDelay)*rate/1000),rate));
 }
 static JSONObject analyze(Preset p,int rate)throws Exception{
  RackConfig rack=RackConfig.parse(p.processing);JSONArray points=new JSONArray();String[] names={"left_to_left","right_to_left","left_to_right","right_to_right"};
  for(int i=0;i<=96;i++){double hz=20*Math.pow(Math.min(20000,rate*.49)/20,i/96d);JSONObject row=new JSONObject().put("frequency_hz",hz);
   for(int path=0;path<4;path++){Z v=path(p,rack,hz,rate,path);JSONObject t=new JSONObject();
    if(v.abs()<1e-12)t.put("gain_db",JSONObject.NULL).put("phase_degrees",JSONObject.NULL).put("group_delay_ms",JSONObject.NULL);
    else{double slope=path(p,rack,hz+.01,rate,path).mul(path(p,rack,hz-.01,rate,path).conj()).phase()/.02;t.put("gain_db",20*Math.log10(v.abs())).put("phase_degrees",v.phase()*180/Math.PI).put("group_delay_ms",-1000*slope/(2*Math.PI));}
    row.put(names[path],t);
   }points.put(row);
  }
  return new JSONObject().put("sample_rate",rate).put("points",points).put("scope","理论线性EQ/DC/M-S/宽度/交叉馈送/混合/矩阵/平衡/极性/延时；不含压缩、限幅、重采样、缓冲、DAC和耳机声学");
 }
 static void show(MainActivity a){
  Preset current=a.preset.copy();int rate=MobileTargets.rate(a);a.toast("计算理论响应，不播放音频");
  MobileTargets.worker.execute(()->{try{JSONObject report=analyze(current,rate);a.runOnUiThread(()->{if(a.isDestroyed())return;try{
   a.responseExport=report.toString(2);LinearLayout panel=a.col();panel.setPadding(a.dp(16),0,a.dp(16),a.dp(12));panel.addView(a.text(rate+" Hz · "+report.getString("scope"),12,a.MUTED));
   JSONArray points=report.getJSONArray("points");for(int n:new int[]{0,22,42,54,65,76,86,96}){JSONObject row=points.getJSONObject(n);StringBuilder line=new StringBuilder(String.format(Locale.US,"%.0f Hz",row.getDouble("frequency_hz")));
    for(String name:new String[]{"left_to_left","right_to_right","right_to_left","left_to_right"}){JSONObject p=row.getJSONObject(name);line.append("\n").append(name).append(p.isNull("gain_db")?"：无路径":String.format(Locale.US,"：%+.2f dB · %.1f° · %.3f ms",p.getDouble("gain_db"),p.getDouble("phase_degrees"),p.getDouble("group_delay_ms")));}
    panel.addView(a.text(line.toString(),12,a.INK));
   }
   ScrollView scroll=new ScrollView(a);scroll.addView(panel);new AlertDialog.Builder(a).setTitle("幅度 / 相位 / 群延时").setView(scroll).setNegativeButton("关闭",null).setPositiveButton("导出完整JSON",(d,w)->a.startActivityForResult(new Intent(Intent.ACTION_CREATE_DOCUMENT).setType("application/json").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_TITLE,"OpenDSP-response.json"),64)).show();
  }catch(Exception e){a.error(e);}});}catch(Exception e){a.runOnUiThread(()->a.error(e));}});
 }
}
