package org.openeq.app;
import android.content.*;
import android.os.*;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;

/** Bidirectional preset sync only; hardware, permissions and master volume remain local. */
final class PresetSync {
 static volatile String status="尚未配对";static volatile boolean busy;
 static final java.util.concurrent.atomic.AtomicLong generation=new java.util.concurrent.atomic.AtomicLong();
 static void check(long session)throws IOException{if(generation.get()!=session)throw new IOException("同步会话已停止或重新配对");}
 static final ExecutorService jobs=Executors.newSingleThreadExecutor(r->new Thread(r,"OpenDSP-preset-sync"));
 static SharedPreferences prefs(Context c){return c.getSharedPreferences("preset-sync",0);}
 static String canonical(Object value)throws Exception{
  if(value instanceof JSONObject){JSONObject j=(JSONObject)value;ArrayList<String> keys=new ArrayList<>();Iterator<String> iterator=j.keys();while(iterator.hasNext())keys.add(iterator.next());Collections.sort(keys);StringBuilder b=new StringBuilder("{");for(String k:keys){if(b.length()>1)b.append(',');b.append(JSONObject.quote(k)).append(':').append(canonical(j.get(k)));}return b.append('}').toString();}
  if(value instanceof JSONArray){JSONArray a=(JSONArray)value;StringBuilder b=new StringBuilder("[");for(int n=0;n<a.length();n++){if(n>0)b.append(',');b.append(canonical(a.get(n)));}return b.append(']').toString();}
  if(value instanceof Number)return new java.math.BigDecimal(value.toString()).stripTrailingZeros().toPlainString();
  if(value==null||value==JSONObject.NULL)return "null";return value instanceof String?JSONObject.quote((String)value):value.toString();
 }
 static String hash(Preset p)throws Exception{
  byte[] bytes=MessageDigest.getInstance("SHA-256").digest(canonical(p.json()).getBytes(StandardCharsets.UTF_8));StringBuilder out=new StringBuilder();for(byte b:bytes)out.append(String.format(Locale.ROOT,"%02x",b&255));return out.toString();
 }
 static <T>T main(Callable<T> task)throws Exception{
  if(Looper.myLooper()==Looper.getMainLooper())return task.call();FutureTask<T> pending=new FutureTask<>(task);new Handler(Looper.getMainLooper()).post(pending);try{return pending.get(5,TimeUnit.SECONDS);}catch(Exception e){pending.cancel(false);throw e;}
 }
 static Preset local(Context c)throws Exception{return main(()->{MainActivity a=MainActivity.live.get();return a!=null?a.preset.copy():Preset.parse(c.getSharedPreferences("MainActivity",0).getString("current",""));});}
 static String device(Context c)throws Exception{return main(()->{MainActivity a=MainActivity.live.get();if(HeadphoneService.running)return "system:"+HeadphoneService.activeKey;UsbAudioService usb=UsbAudioService.instance;if(UsbAudioService.processing&&usb!=null)return "usb:"+usb.deviceName;return a==null?"idle":"idle:"+a.deviceKey;});}
 static void receive(Context c,Preset next,String expected,String expectedDevice,long session)throws Exception{
  main(()->{check(session);Preset current=local(c);if(!hash(current).equals(expected)||!device(c).equals(expectedDevice))throw new IOException("手机参数或设备已改变，未覆盖；请重新同步");
   MainActivity a=MainActivity.live.get();if(a!=null){a.remember();a.preset=next;a.changed();if(a.visible)a.render();else a.needsRender=true;}
   else{
    c.getSharedPreferences("MainActivity",0).edit().putString("sync.rollback",current.encode()).putString("current",next.encode()).apply();
    UsbAudioService.update(next);RootEqService.update(next);if(HeadphoneService.running)HeadphoneService.update(next,HeadphoneService.activeKey);
   }return null;
  });
 }
 static synchronized void pair(Context c,String text)throws Exception{
  JSONObject j=new JSONObject(text);if(!j.optString("format").equals("org.opendsp.sync-pair")||j.optInt("version")!=1)throw new IOException("无效的配对文件");
  URL url=new URL(j.getString("url"));if(!url.getProtocol().equals("http")||!url.getHost().equals("127.0.0.1")||url.getPort()<1024||!url.getPath().isEmpty()||url.getUserInfo()!=null||url.getQuery()!=null||url.getRef()!=null)throw new IOException("只支持通过ADB通道连接本机同步服务");
  String token=j.getString("token");if(!token.matches("[0-9A-Fa-f]{64}"))throw new IOException("无效配对令牌");
  stop(c);prefs(c).edit().clear().putString("url",url.toString()).putString("token",token).apply();status="已配对配置，首次同步请选择来源";
 }
 static JSONObject request(Context c,JSONObject command)throws Exception{
  SharedPreferences p=prefs(c);return request(p.getString("url",""),p.getString("token",""),command);
 }
 static JSONObject request(String base,String token,JSONObject command)throws Exception{
  if(base.isEmpty())throw new IOException("请先导入PC配对文件");
  HttpURLConnection connection=(HttpURLConnection)new URL(base+(command==null?"/v1/state":"/v1/command")).openConnection();
  connection.setConnectTimeout(3000);connection.setReadTimeout(7000);connection.setInstanceFollowRedirects(false);connection.setRequestProperty("Authorization","Bearer "+token);
  try{
   if(command!=null){connection.setRequestMethod("POST");connection.setDoOutput(true);connection.setRequestProperty("Content-Type","application/json");byte[] body=command.toString().getBytes(StandardCharsets.UTF_8);connection.setFixedLengthStreamingMode(body.length);try(OutputStream out=connection.getOutputStream()){out.write(body);}}
   int code=connection.getResponseCode();if(code!=200)throw new IOException(code==401?"配对已失效，请在PC重新导出":"同步服务返回 "+code);
   JSONObject reply;try(InputStream in=connection.getInputStream()){reply=new JSONObject(MainActivity.readText(in,1048576));}
   if(!reply.optBoolean("ok",false))throw new IOException(reply.optString("error","PC拒绝了修改"));return reply;
  }finally{connection.disconnect();}
 }
 static String direction(String local,String remote,String baseLocal,String baseRemote){
  if(local.equals(remote))return "equal";
  if(baseLocal.isEmpty()||baseRemote.isEmpty())return "first";
  boolean l=!local.equals(baseLocal),r=!remote.equals(baseRemote);
  return l&&r?"conflict":l?"push":r?"pull":"equal";
 }
 static void sync(Context c,String mode)throws Exception{
  final long session;final String peer,base; synchronized(PresetSync.class){session=generation.get();peer=prefs(c).getString("token","");base=prefs(c).getString("url","");}
  String device=device(c);Preset local=local(c);String lh=hash(local);JSONObject remote=request(base,peer,null);check(session);Preset pc=Preset.parse(remote.getJSONObject("preset").toString());String rh=hash(pc);
  SharedPreferences p=prefs(c);String action=mode.equals("auto")?direction(lh,rh,p.getString("base.local",""),p.getString("base.remote","")):mode;
  if(mode.equals("auto")&&!p.getString("base.device",device).equals(device))throw new IOException("输出设备已切换，请重新选择同步来源");
  if(action.equals("first"))throw new IOException("首次同步请选择“接收PC”或“发送手机”");
  if(action.equals("conflict"))throw new IOException("两端均已修改，自动同步暂停覆盖；请选择保留哪端");
  if(action.equals("pull")){receive(c,pc,lh,device,session);lh=hash(pc);}
  else if(action.equals("push")){
   check(session);if(!peer.equals(prefs(c).getString("token","")))throw new IOException("配对已改变");
   JSONObject response=request(base,peer,new JSONObject().put("cmd","apply_preset").put("expected_revision",remote.getString("revision")).put("auto_headroom",false).put("preset",local.json()));
   rh=hash(Preset.parse(response.getJSONObject("preset").toString()));if(!rh.equals(lh))throw new IOException("PC未原样接受此预设，请查看PC状态");
  }
  check(session);p.edit().putString("base.local",lh).putString("base.remote",rh).putString("base.device",device).apply();status=action.equals("pull")?"已接收PC当前预设":action.equals("push")?"已发送手机当前预设":"两端参数一致";
 }
 static synchronized void once(Context c,String mode){if(busy)return;busy=true;long session=generation.get();Context app=c.getApplicationContext();jobs.execute(()->{try{check(session);sync(app,mode);}catch(Exception e){if(session==generation.get())status="未同步："+e.getMessage();}finally{busy=false;}});}
 static void start(Context c){if(prefs(c).getString("url","").isEmpty()){status="请先配对";return;}c.startForegroundService(new Intent(c,SyncService.class));}
 static void stop(Context c){generation.incrementAndGet();c.stopService(new Intent(c,SyncService.class));}
}
