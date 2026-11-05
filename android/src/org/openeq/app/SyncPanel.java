package org.openeq.app;
import android.app.AlertDialog;
import android.widget.*;
final class SyncPanel {
 static void page(MainActivity a){
  LinearLayout c=a.card();c.addView(a.text("PC ↔ Android 设置同步",22,a.INK));
  c.addView(a.text("共用完整预设：主EQ、输出、压缩器、矩阵和空间参数。\n播放设备、采样率、系统授权与USB主音量不跨设备覆盖。",13,a.MUTED));
  a.syncStatus=a.text(PresetSync.status,14,a.ACCENT);c.addView(a.syncStatus);
  a.rowButtons(c,"导入配对文件",()->a.open(61,"*/*"),"粘贴配对配置",()->{
   EditText input=new EditText(a);input.setSingleLine(false);input.setHint("粘贴PC同步窗口复制的配对配置");
   new AlertDialog.Builder(a).setTitle("配对自己的PC").setView(input).setNegativeButton("取消",null).setPositiveButton("配对",(d,w)->{try{PresetSync.pair(a,input.getText().toString());a.render();}catch(Exception e){a.error(e);}}).show();
  });
  a.rowButtons(c,"接收PC当前预设",()->choose(a,"pull"),"发送手机当前预设",()->choose(a,"push"));
  c.addView(a.button("接收PC整个预设库（不切换当前调音）",()->PresetLibrary.receivePc(a)));
  a.rowButtons(c,SyncService.running?"自动同步已开启":"开启自动双向同步",()->{a.requestNotificationPermission();try{PresetSync.start(a);}catch(Exception e){a.error(e);}},"停止自动同步",()->{PresetSync.stop(a);PresetSync.status="自动同步已停止";});
  c.addView(a.text("首次同步或两端同时修改时，请明确选择来源。单端修改自动传递；网络断开不清空配置，也不自动开始播放。接收修改可撤销。",12,a.MUTED));
  LinearLayout guide=a.card();guide.addView(a.text("连接方法",18,a.INK));
  guide.addView(a.text("1. PC预设菜单 → 手机双端设置同步。\n2. 电脑用USB或无线ADB连接手机，执行同步窗口的reverse命令。\n3. 将PC配对文件导入此页，选择第一次同步来源。\n4. 按需开启自动同步，关闭PC同步窗口即撤销连接。\nAndroid对后台数据同步有运行时限，通知栏可随时停止。",13,a.MUTED));
 }
 static void choose(MainActivity a,String direction){new AlertDialog.Builder(a).setTitle(direction.equals("pull")?"用PC预设更新手机？":"用手机预设更新PC？").setMessage("目标端当前参数会被更新，并保留撤销。双端都修改时，这会采用你选择的一端。").setNegativeButton("取消",null).setPositiveButton("同步",(d,w)->PresetSync.once(a,direction)).show();}
}
