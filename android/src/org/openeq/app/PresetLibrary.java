package org.openeq.app;
import android.content.*;
import android.widget.*;
import android.app.AlertDialog;
import java.io.*;
import java.util.*;
import org.json.*;

/** Visible combined library; importing never applies or silently overwrites a preset. */
final class PresetLibrary {
 static int invalid;
 static SharedPreferences prefs(Context c){return c.getSharedPreferences("MainActivity",0);}
 static TreeMap<String,Preset> all(Context c)throws Exception{
  TreeMap<String,Preset> result=new TreeMap<>();
  invalid=0;
  for(String file:c.getAssets().list("presets"))try(InputStream in=c.getAssets().open("presets/"+file)){Preset p=Preset.parse(MainActivity.readText(in,262144));result.put(p.name,p);}catch(Exception e){invalid++;}
  for(Map.Entry<String,?> entry:prefs(c).getAll().entrySet())if(entry.getKey().startsWith("saved.")&&entry.getValue() instanceof String)try{Preset p=Preset.parse((String)entry.getValue());result.put(p.name,p);}catch(Exception e){invalid++;}
  return result;
 }
 static JSONObject merge(Context context,JSONObject pack)throws Exception{
  if(!pack.getString("format").equals("org.opendsp.preset-library")||pack.getInt("version")!=1)throw new IOException("不支持的预设库格式");
  JSONArray entries=pack.getJSONArray("presets");if(entries.length()>200)throw new IOException("单次最多200份预设");
  ArrayList<Preset> incoming=new ArrayList<>();HashSet<String> unique=new HashSet<>();
  for(int i=0;i<entries.length();i++){Preset p=Preset.parse(entries.getJSONObject(i).toString());if(!unique.add(p.name))throw new IOException("库内重名："+p.name);incoming.add(p);}
  SharedPreferences prefs=prefs(context);Map<String,?> before=prefs.getAll();HashMap<String,String> stored=new HashMap<>();
  for(Map.Entry<String,?> entry:before.entrySet())if(entry.getKey().startsWith("saved.")&&entry.getValue() instanceof String)stored.put(entry.getKey(),(String)entry.getValue());
  JSONObject backup=new JSONObject(stored);File backupFile=new File(context.getFilesDir(),"preset-library-before-"+System.currentTimeMillis()+".json");
  try(FileOutputStream out=new FileOutputStream(backupFile)){out.write(backup.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
  SharedPreferences.Editor edit=prefs.edit();JSONArray names=new JSONArray();int added=0,same=0,renamed=0;
  for(Preset original:incoming){
   Preset next=original.copy();int index=0;
   while(true){
    String key="saved."+next.name,existing=stored.get(key);
    if(existing==null){edit.putString(key,next.encode()).putString("library.source."+next.name,"PC");stored.put(key,next.encode());added++;if(index>0)renamed++;break;}
    if(PresetSync.hash(Preset.parse(existing)).equals(PresetSync.hash(next))){same++;break;}
    index++;String suffix=index==1?" · PC导入":" · PC导入 "+index;next.name=original.name.substring(0,Math.min(original.name.length(),100-suffix.length()))+suffix;
   }
   names.put(new JSONObject().put("pc_name",original.name).put("phone_name",next.name));
  }
  if(!edit.commit())throw new IOException("预设库保存失败");
  return new JSONObject().put("added",added).put("already_present",same).put("renamed_conflicts",renamed).put("entries",names).put("current_unchanged",Objects.equals(before.get("current"),prefs.getString("current",null))).put("backup",backupFile.getName());
 }
 static void page(MainActivity a){
  try{
   TreeMap<String,Preset> presets=all(a);LinearLayout section=a.card();section.addView(a.text("全部预设 · "+presets.size()+" 份",19,a.INK));
   if(invalid>0)section.addView(a.text(invalid+"份无效预设未加载，原文件未删除",12,a.MUTED));
   section.addView(a.text("PC导入、本机保存和内置调音直接列在这里。点“应用”才会改变当前声音。",12,a.MUTED));
   for(Preset preset:presets.values()){
    LinearLayout item=a.card();String source=prefs(a).getString("library.source."+preset.name,prefs(a).contains("saved."+preset.name)?"本机":"内置");
    item.addView(a.text(preset.name,16,a.INK));item.addView(a.text(source+" · "+preset.bands.size()+"段 · 前级 "+preset.preamp+" dB",12,a.MUTED));
    a.rowButtons(item,"应用",()->{a.remember();a.preset=preset.copy();a.changed();a.render();a.toast("已应用 "+preset.name);},source.equals("内置")?"另存本机":"删除本机副本",()->{
     if(source.equals("内置")){prefs(a).edit().putString("saved."+preset.name,preset.encode()).apply();a.render();}
     else new AlertDialog.Builder(a).setMessage("删除本机副本："+preset.name+"？内置同名预设将重新显示，当前调音保持。").setNegativeButton("取消",null).setPositiveButton("删除",(d,w)->{prefs(a).edit().remove("saved."+preset.name).remove("library.source."+preset.name).apply();a.render();}).show();
    });
   }
  }catch(Exception e){a.error(e);}
 }
 static void receivePc(MainActivity a){
  synchronized(PresetSync.class){if(PresetSync.busy){a.toast("同步正在进行，请稍候");return;}PresetSync.busy=true;}long session=PresetSync.generation.get();
  PresetSync.jobs.execute(()->{try{JSONObject response=PresetSync.request(a,new JSONObject().put("cmd","get_preset_library"));PresetSync.check(session);JSONObject pack=response.getJSONObject("library");
   PresetSync.main(()->{PresetSync.check(session);JSONObject report=merge(a,pack);a.page="预设";a.render();a.toast("预设库已导入："+report.getInt("added")+"份新增，"+report.getInt("already_present")+"份已存在；当前声音未切换");return null;});
  }catch(Exception e){a.runOnUiThread(()->a.error(e));}finally{PresetSync.busy=false;}});
 }
}
