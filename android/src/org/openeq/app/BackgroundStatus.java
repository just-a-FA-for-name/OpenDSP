package org.openeq.app;
import android.app.ActivityManager;
import android.content.*;
import android.net.Uri;
import android.os.PowerManager;
import android.provider.Settings;

final class BackgroundStatus {
 static String describe(Context c){boolean restricted=c.getSystemService(ActivityManager.class).isBackgroundRestricted(),exempt=c.getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(c.getPackageName());
  return (restricted?"后台运行：系统已限制":"后台运行：系统未报告限制")+" · "+(exempt?"已豁免电池优化":"尚未豁免电池优化")+"\n“无限制”仍不能保证厂商不限制调度；中断原因会保留在诊断中。";}
 static void requestBatteryExemption(Context c){
  PowerManager power=c.getSystemService(PowerManager.class);
  Intent intent=power.isIgnoringBatteryOptimizations(c.getPackageName())?new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+c.getPackageName())):new Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,Uri.parse("package:"+c.getPackageName()));
  c.startActivity(intent);
 }
}
