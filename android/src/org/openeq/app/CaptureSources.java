package org.openeq.app;
import android.content.Context;
import android.content.pm.PackageManager;
import android.media.AudioPlaybackCaptureConfiguration;
import android.os.Process;

/** Explicit app choices; never reads media libraries, accounts or playback history. */
final class CaptureSources {
 static final String[] PACKAGES={"com.netease.cloudmusic","tv.danmaku.bili",""};
 static final String[] LABELS={"网易云音乐","哔哩哔哩","所有允许捕获的媒体应用"};
 static int index(String packageName){for(int i=0;i<PACKAGES.length;i++)if(PACKAGES[i].equals(packageName))return i;throw new IllegalArgumentException("不支持的音源应用");}
 static String label(String packageName){return LABELS[index(packageName)];}
 static int uid(Context context,String packageName)throws PackageManager.NameNotFoundException{
  index(packageName);if(packageName.isEmpty())return -1;
  android.content.pm.ApplicationInfo info=context.getPackageManager().getApplicationInfo(packageName,0);
  if(!info.enabled||info.uid==Process.myUid())throw new IllegalArgumentException("音源应用不可用");
  return info.uid;
 }
 static void scope(AudioPlaybackCaptureConfiguration.Builder builder,int uid){
  // Android forbids combining matching and excluded UID rules.
  if(uid>=0)builder.addMatchingUid(uid);else builder.excludeUid(Process.myUid());
 }
}
