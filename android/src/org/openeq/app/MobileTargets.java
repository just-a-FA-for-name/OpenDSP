package org.openeq.app;
import android.app.*;
import android.content.*;
import android.net.Uri;
import android.widget.*;
import android.graphics.*;
import android.view.View;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;

final class MobileTargets {
 static final ExecutorService worker=Executors.newSingleThreadExecutor(r->new Thread(r,"OpenDSP-target-fit"));
 static ArrayList<TargetFitter.Curve> catalog;static boolean fitting;
 static android.content.SharedPreferences store(MainActivity a){return a.getSharedPreferences("target-workflow",0);}
 static int rate(MainActivity a){return UsbAudioService.processing?UsbAudioService.actualRate:a.usb.workbench.mode.equals("usb")?a.usb.rate:48000;}
 static ArrayList<TargetFitter.Curve> curves(MainActivity a)throws Exception{
  if(catalog==null){JSONArray data;try(InputStream in=a.getAssets().open("targets/catalog-full.json")){data=new JSONArray(MainActivity.readText(in,2097152));}ArrayList<TargetFitter.Curve> built=new ArrayList<>();for(int i=0;i<data.length();i++)built.add(new TargetFitter.Curve(data.getJSONObject(i)));catalog=built;}
  ArrayList<TargetFitter.Curve> all=new ArrayList<>(catalog);String custom=store(a).getString("custom",null);if(custom!=null)all.add(new TargetFitter.Curve(new JSONObject(custom)));return all;
 }
 static void page(MainActivity a){try{
  LinearLayout c=a.card();c.addView(a.text("目标曲线 / 测量拟合",22,a.INK));c.addView(a.text("与PC共用35条目标。声学目标需同体系原始频响；听感偏好仅是相对EQ。预览不会改声音。",13,a.MUTED));
  android.content.SharedPreferences prefs=store(a);ArrayList<TargetFitter.Curve> all=curves(a);ArrayList<String> rigs=new ArrayList<>();rigs.add("none");for(TargetFitter.Curve curve:all)if(curve.measured()&&!rigs.contains(curve.rig))rigs.add(curve.rig);
  Spinner rig=a.usb.spinner(c,"测量体系",rigs.toArray(new String[0]));rig.setSelection(Math.max(0,rigs.indexOf(prefs.getString("rig","none"))));
  rig.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onNothingSelected(android.widget.AdapterView<?> p){}public void onItemSelected(android.widget.AdapterView<?> p,View v,int n,long id){prefs.edit().putString("rig",rigs.get(n)).apply();}});
  a.rowButtons(c,"导入耳机频响 CSV",()->a.open(62,"*/*"),"导入自定义目标 CSV",()->a.open(63,"*/*"));
  c.addView(a.text(prefs.contains("measurement")?"已载入频响 · "+prefs.getString("measurement.label","用户文件"):"尚无测量数据；可先预览相对偏好",12,a.MUTED));
  c.addView(a.button("清除测量",()->{prefs.edit().remove("measurement").remove("measurement.label").apply();a.render();}));
  EditText bands=a.field(c,"拟合段数 1–32",prefs.getString("bands","20"),true),max=a.field(c,"拟合上限 Hz",prefs.getString("max","8000"),true);
  EditText boost=a.field(c,"最大提升 dB",prefs.getString("boost","6"),true),cut=a.field(c,"最大衰减 dB",prefs.getString("cut","12"),true),q=a.field(c,"最大Q",prefs.getString("q","3"),true);
  Switch smooth=a.toggle(c,"声学数据平滑",prefs.getBoolean("smooth",true));
  EditText search=a.field(c,"搜索目标 / 分类 / 测量体系",prefs.getString("search",""),false);
  c.addView(a.button("筛选",()->{save(prefs,bands,max,boost,cut,q,smooth);prefs.edit().putString("search",search.getText().toString()).apply();a.render();}));
  String query=prefs.getString("search","").toLowerCase(Locale.ROOT);
  for(TargetFitter.Curve target:all){if(!(target.name+" "+target.description+" "+target.rig+" "+target.kind).toLowerCase(Locale.ROOT).contains(query))continue;
   LinearLayout card=a.card();card.addView(a.text(target.name,17,a.INK));card.addView(a.text((target.measured()?"声学目标 · "+target.rig:"听感偏好 · 相对EQ")+"\n"+target.description,12,a.MUTED));
   card.addView(a.button("预览拟合",()->{try{save(prefs,bands,max,boost,cut,q,smooth);preview(a,target);}catch(Exception e){a.error(e);}}));
   card.addView(a.button("来源与适用范围",()->new AlertDialog.Builder(a).setTitle(target.name).setMessage(target.source+"\n\n"+(target.measured()?"需要同测量体系；不能直接叠加声学目标当作耳机校准。":"只改变数字EQ形状，不是耳机声学目标校准。")).setPositiveButton("知道了",null).show()));
  }
 }catch(Exception e){a.error(e);}}
 static void save(android.content.SharedPreferences p,EditText b,EditText max,EditText boost,EditText cut,EditText q,Switch smooth){p.edit().putString("bands",b.getText().toString()).putString("max",max.getText().toString()).putString("boost",boost.getText().toString()).putString("cut",cut.getText().toString()).putString("q",q.getText().toString()).putBoolean("smooth",smooth.isChecked()).apply();}
 static String context(MainActivity a){android.content.SharedPreferences p=store(a);return rate(a)+"|"+p.getString("rig","none")+"|"+p.getString("measurement","")+"|"+p.getString("custom","")+"|"+p.getString("bands","20")+"|"+p.getString("max","8000")+"|"+p.getString("boost","6")+"|"+p.getString("cut","12")+"|"+p.getString("q","3")+"|"+p.getBoolean("smooth",true);}
 static void preview(MainActivity a,TargetFitter.Curve target)throws Exception{
  if(fitting){a.toast("正在拟合，请稍候");return;}android.content.SharedPreferences p=store(a);TargetFitter.Options o=new TargetFitter.Options();
  o.bands=Integer.parseInt(p.getString("bands","20"));o.max=Double.parseDouble(p.getString("max","8000"));o.boost=Double.parseDouble(p.getString("boost","6"));o.cut=Double.parseDouble(p.getString("cut","12"));o.q=Double.parseDouble(p.getString("q","3"));o.smooth=p.getBoolean("smooth",true);
  Preset current=a.preset.copy();int sampleRate=rate(a);String baseContext=context(a),rig=p.getString("rig","none");String data=p.getString("measurement",null);double[][] measured=target.measured()&&data!=null?TargetFitter.points(new JSONArray(data)):null;fitting=true;a.toast("正在后台拟合，当前声音保持");
  worker.execute(()->{android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_BACKGROUND);try{
   TargetFitter.Result result=TargetFitter.fit(target,measured,rig,sampleRate,o,current);a.runOnUiThread(()->{fitting=false;if(a.isDestroyed())return;
    LinearLayout box=a.col();box.setPadding(a.dp(18),a.dp(8),a.dp(18),a.dp(8));
    box.addView(a.text(String.format(Locale.US,"%d段 · %d Hz · 前级 %.1f dB\n形状误差 RMS：%.2f → %.2f dB\n最大误差 %.2f dB · 超限制频点 %d",result.preset.bands.size(),sampleRate,result.preset.preamp,result.before,result.after,result.maximum,result.clipped),14,a.INK));
    box.addView(new Plot(a,result.response),new LinearLayout.LayoutParams(-1,a.dp(190)));
    box.addView(a.text("橙：目标形状 · 蓝：预测形状\n不含输出机架、实际佩戴或重新实测。应用后请结合听感验证；空间处理若已开启会继续保留。",12,a.MUTED));
    ScrollView scroll=new ScrollView(a);scroll.addView(box);new AlertDialog.Builder(a).setTitle("预览 · "+target.name).setView(scroll).setNegativeButton("取消",null).setPositiveButton("应用，可撤销",(d,w)->{try{if(!PresetSync.hash(a.preset).equals(result.base)||!context(a).equals(baseContext))throw new Exception("参数、测量或输出格式已变，请重新预览");a.remember();a.preset=result.preset;a.changed();a.page="均衡";a.render();}catch(Exception e){a.error(e);}}).show();
   });
  }catch(Exception e){a.runOnUiThread(()->{fitting=false;if(!a.isDestroyed())a.error(e);});}});
 }
 static void imported(MainActivity a,int request,Uri uri)throws Exception{
  String text;try(InputStream in=a.getContentResolver().openInputStream(uri)){text=MainActivity.readText(in,524288);}
  double[][] points=TargetFitter.csv(text);JSONArray data=new JSONArray();for(double[] row:points)data.put(new JSONArray().put(row[0]).put(row[1]));
  if(request==62)store(a).edit().putString("measurement",data.toString()).putString("measurement.label",points.length+"点原始频响").apply();
  else{String rig=store(a).getString("rig","none");if(rig.equals("none"))throw new Exception("请先选择目标的测量体系");
   JSONObject target=new JSONObject().put("id","custom").put("name","自定义目标").put("kind","acoustic").put("rig",rig).put("source","用户导入CSV；请核对来源").put("description","本机保存的自定义声学目标").put("points",data);store(a).edit().putString("custom",target.toString()).apply();}
  a.page="目标";a.render();
 }
 static final class Plot extends View {
  final double[][] data;final Paint paint=new Paint(3);Plot(MainActivity a,double[][] data){super(a);this.data=data;}
  protected void onDraw(Canvas canvas){super.onDraw(canvas);double lo=Double.POSITIVE_INFINITY,hi=Double.NEGATIVE_INFINITY;for(double[] row:data){lo=Math.min(lo,Math.min(row[1],row[4]));hi=Math.max(hi,Math.max(row[1],row[4]));}lo-=2;hi+=2;
   for(int column:new int[]{1,4}){Path p=new Path();for(int i=0;i<data.length;i++){float x=8+(getWidth()-16)*(float)i/(data.length-1),y=8+(getHeight()-16)*(float)((hi-data[i][column])/(hi-lo));if(i==0)p.moveTo(x,y);else p.lineTo(x,y);}paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(3);paint.setColor(column==1?Color.rgb(205,136,71):Color.rgb(88,111,244));canvas.drawPath(p,paint);}
  }
 }
}
