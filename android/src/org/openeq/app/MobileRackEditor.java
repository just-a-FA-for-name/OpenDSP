package org.openeq.app;
import android.app.*;
import android.widget.*;
import org.json.*;
import java.util.*;
/** Mobile editors preserve the same complete output objects as desktop presets. */
final class MobileRackEditor {
 final MainActivity a;MobileRackEditor(MainActivity a){this.a=a;}
 void addControls(LinearLayout panel){
  a.rowButtons(panel,"压缩器",()->edit("compressor"),"声道矩阵",()->edit("matrix"));
  a.rowButtons(panel,"高级声场",()->edit("spatial"),"M/S 均衡器",this::chooseBank);
  a.rowButtons(panel,"空间试听场景",this::scenes,"恢复输出默认",this::reset);
  panel.addView(a.text("PCM路径支持完整主EQ、压缩、矩阵、DC滤波及M/S空间处理。DSF→DoP直送绕过全部DSP。",11,a.MUTED));
 }
 JSONObject group(String name)throws Exception{JSONObject v=a.preset.processing.optJSONObject(name);return v==null?new JSONObject():new JSONObject(v.toString());}
 void commit(String name,JSONObject value)throws Exception{Preset next=a.preset.copy();next.processing.put(name,value);DspRack.validate(next);a.remember();a.preset=next;a.changed();a.render();}
 void edit(String name){
  try{
   JSONObject draft=group(name);LinearLayout panel=a.col();panel.setPadding(a.dp(18),0,a.dp(18),a.dp(12));
   final Switch enabled=name.equals("matrix")?null:a.toggle(panel,name.equals("spatial")?"启用空间处理":"启用压缩器",draft.optBoolean("enabled",false));
   String[][] fields=name.equals("compressor")?new String[][]{
    {"threshold_db","阈值 dB","-18"},{"ratio","压缩比","4"},{"attack_ms","启动 ms","10"},{"release_ms","释放 ms","120"},{"knee_db","拐点 dB","6"},{"makeup_db","补偿 dB","0"}
   }:name.equals("matrix")?new String[][]{
    {"ll","LL","1"},{"lr","LR","0"},{"rl","RL","0"},{"rr","RR","1"},{"width","宽度 0–2","1"}
   }:new String[][]{
    {"mix","效果混合 0–1","1"},{"mid_gain_db","中央增益 dB","0"},{"side_gain_db","侧向增益 dB","0"},{"low_width","低频宽度 0–2","1"},{"mid_width","中频宽度 0–2","1"},{"high_width","高频宽度 0–2","1"},
    {"low_crossover_hz","低分频 Hz","180"},{"high_crossover_hz","高分频 Hz","3500"},{"crossfeed","交叉馈送 0–0.45","0"},{"crossfeed_hz","馈送低通 Hz","700"},{"crossfeed_delay_ms","馈送延时 ms","0.25"}
   };
   EditText[] editors=new EditText[fields.length];for(int i=0;i<fields.length;i++)editors[i]=a.field(panel,fields[i][1],Double.toString(draft.optDouble(fields[i][0],Double.parseDouble(fields[i][2]))),true);
   Switch mono=name.equals("spatial")?a.toggle(panel,"单声道混合（100%效果时完全单声道）",draft.optBoolean("mono",false)):null;
   Switch dc=name.equals("matrix")?a.toggle(panel,"直流滤波",a.preset.processing.optBoolean("dc_block",false)):null;
   ScrollView scroll=new ScrollView(a);scroll.addView(panel);
   AlertDialog dialog=new AlertDialog.Builder(a).setTitle(name.equals("spatial")?"高级声场":name.equals("matrix")?"矩阵 / DC":"压缩器").setView(scroll).setNegativeButton("取消",null).setNeutralButton("恢复本组默认",(d,w)->{try{commit(name,new JSONObject());}catch(Exception e){a.error(e);}}).setPositiveButton("保存",null).create();
   dialog.setOnShowListener(v->dialog.getButton(-1).setOnClickListener(w->{try{
    for(int i=0;i<fields.length;i++)draft.put(fields[i][0],Double.parseDouble(editors[i].getText().toString()));if(enabled!=null)draft.put("enabled",enabled.isChecked());if(mono!=null)draft.put("mono",mono.isChecked());
    Preset next=a.preset.copy();next.processing.put(name,draft);if(dc!=null)next.processing.put("dc_block",dc.isChecked());DspRack.validate(next);
    a.remember();a.preset=next;a.changed();dialog.dismiss();a.render();
   }catch(Exception e){a.error(e);}}));dialog.show();
  }catch(Exception e){a.error(e);}
 }
 void reset(){new AlertDialog.Builder(a).setTitle("恢复输出处理").setItems(new String[]{"只恢复输出增益/平衡/延时/保护","压缩器","矩阵","高级空间","全部输出处理"},(d,index)->{try{
  Preset next=a.preset.copy();if(index==0||index==4){next.output=next.balance=next.leftDelay=next.rightDelay=0;next.invertLeft=next.invertRight=false;next.protection=true;next.ceiling=-1;}
  if(index==4)next.processing=new JSONObject();else if(index>0)next.processing.put(new String[]{"","compressor","matrix","spatial"}[index],new JSONObject());
  a.remember();a.preset=next;a.changed();a.render();
 }catch(Exception e){a.error(e);}}).setNegativeButton("取消",null).show();}
 void chooseBank(){new AlertDialog.Builder(a).setTitle("M/S · 每组最多16段").setItems(new String[]{"中央 Mid","侧向 Side"},(d,n)->bank(n==0?"mid_filters":"side_filters")).setNegativeButton("关闭",null).show();}
 void bank(String key){try{
  JSONObject spatial=group("spatial");JSONArray data=spatial.optJSONArray(key);final JSONArray bands=data==null?new JSONArray():data;String[] labels=new String[bands.length()+1];labels[0]="＋ 添加频段";
  for(int i=0;i<bands.length();i++){JSONObject b=bands.getJSONObject(i);labels[i+1]=b.getString("type")+" · "+b.getDouble("frequency_hz")+"Hz · "+b.getDouble("gain_db")+"dB";}
  new AlertDialog.Builder(a).setTitle(key.startsWith("mid")?"中央 EQ":"侧向 EQ").setItems(labels,(d,n)->editBand(key,spatial,bands,n-1)).setNegativeButton("关闭",null).show();
 }catch(Exception e){a.error(e);}}
 void editBand(String key,JSONObject spatial,JSONArray bands,int index){try{
  if(index<0&&bands.length()>=16){a.toast("最多16段");return;}JSONObject b=index<0?new JSONObject():bands.getJSONObject(index);LinearLayout p=a.col();p.setPadding(a.dp(18),0,a.dp(18),a.dp(12));
  Spinner type=new Spinner(a);type.setAdapter(new ArrayAdapter<>(a,android.R.layout.simple_spinner_dropdown_item,Preset.LABELS));type.setSelection(Math.max(0,Arrays.asList(Preset.TYPES).indexOf(b.optString("type","peak"))));p.addView(type);
  EditText hz=a.field(p,"频率 Hz",Double.toString(b.optDouble("frequency_hz",1000)),true),gain=a.field(p,"增益 dB",Double.toString(b.optDouble("gain_db",0)),true),q=a.field(p,"Q",Double.toString(b.optDouble("q",.707)),true);Switch on=a.toggle(p,"启用本段",b.optBoolean("enabled",true));
  AlertDialog dialog=new AlertDialog.Builder(a).setTitle("M/S滤波器").setView(p).setNegativeButton("取消",null).setNeutralButton("删除",(d,w)->{if(index>=0)try{bands.remove(index);spatial.put(key,bands);commit("spatial",spatial);}catch(Exception e){a.error(e);}}).setPositiveButton("保存",null).create();
  dialog.setOnShowListener(v->dialog.getButton(-1).setOnClickListener(w->{try{JSONObject next=new JSONObject().put("type",Preset.TYPES[type.getSelectedItemPosition()]).put("frequency_hz",Double.parseDouble(hz.getText().toString())).put("gain_db",Double.parseDouble(gain.getText().toString())).put("q",Double.parseDouble(q.getText().toString())).put("channel","both").put("enabled",on.isChecked());
   JSONArray replacement=new JSONArray(bands.toString());if(index<0)replacement.put(next);else replacement.put(index,next);spatial.put(key,replacement);commit("spatial",spatial);dialog.dismiss();
  }catch(Exception e){a.error(e);}}));dialog.show();
 }catch(Exception e){a.error(e);}}
 void scenes(){try{
  final JSONArray scenes;try(java.io.InputStream input=a.getAssets().open("targets/spatial-scenes.json")){scenes=new JSONArray(MainActivity.readText(input,262144));}
  String[] labels=new String[scenes.length()+1];for(int i=0;i<scenes.length();i++)labels[i]=scenes.getJSONObject(i).getString("name");labels[scenes.length()]="旁路（保留参数）";
  new AlertDialog.Builder(a).setTitle("PC同款空间场景 · 可撤销").setItems(labels,(d,n)->{try{JSONObject s=n==scenes.length()?group("spatial").put("enabled",false):new JSONObject(scenes.getJSONObject(n).getJSONObject("spatial").toString());commit("spatial",s);}catch(Exception e){a.error(e);}}).setNegativeButton("关闭",null).show();
 }catch(Exception e){a.error(e);}}
}
