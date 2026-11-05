package org.openeq.app;
import android.content.*;
import android.media.*;
import android.os.*;
import java.util.*;

/** Uses media routing attributes on API 33+, rather than guessing from a list of connected outputs. */
final class DeviceRoutes implements AutoCloseable {
 interface Listener{void route(String key,String label,int type);}
 final AudioManager audio;final Handler handler=new Handler(Looper.getMainLooper());final Listener listener;volatile boolean closed;
 final HandlerThread scanner=new HandlerThread("OpenDSP-route-scan");final Handler scan;
 final Runnable scanTask=()->readRoute();
 final AudioDeviceCallback callback=new AudioDeviceCallback(){public void onAudioDevicesAdded(AudioDeviceInfo[] d){check();}public void onAudioDevicesRemoved(AudioDeviceInfo[] d){check();}};
 final Runnable poll=new Runnable(){public void run(){if(closed)return;check();handler.postDelayed(this,8000);}};
 DeviceRoutes(Context context,Listener listener){audio=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);this.listener=listener;scanner.start();scan=new Handler(scanner.getLooper());audio.registerAudioDeviceCallback(callback,handler);handler.post(poll);}
 static String key(int type,String name,String address){String identity=type+"|"+name+"|"+address;try{byte[] data=java.security.MessageDigest.getInstance("SHA-256").digest(identity.getBytes(java.nio.charset.StandardCharsets.UTF_8));StringBuilder s=new StringBuilder();for(byte b:data)s.append(String.format(Locale.US,"%02x",b&255));return s.toString();}catch(Exception e){return identity;}}
 void check(){if(closed)return;scan.removeCallbacks(scanTask);scan.postDelayed(scanTask,180);}
 void readRoute(){if(closed)return;try{List<AudioDeviceInfo> devices=new ArrayList<>();boolean confirmed=false;if(Build.VERSION.SDK_INT>=33)try{devices.addAll(audio.getAudioDevicesForAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build()));confirmed=!devices.isEmpty();}catch(SecurityException ignored){}
   devices.removeIf(d->d.getType()==25); // framework TYPE_REMOTE_SUBMIX is an internal routing sink, not the headphone
   if(devices.isEmpty()){devices.addAll(Arrays.asList(audio.getDevices(AudioManager.GET_DEVICES_OUTPUTS)));devices.removeIf(d->d.getType()==25);
    ArrayList<AudioDeviceInfo> personal=new ArrayList<>();for(AudioDeviceInfo item:devices)if(headphone(item.getType()))personal.add(item);
    if(personal.size()>1){publish("","多个耳机已连接，暂不能确认系统活动路由",-1);return;}
    if(personal.size()==1){devices.clear();devices.add(personal.get(0));}else devices.sort((a,b)->Integer.compare(priority(b.getType()),priority(a.getType())));
   }
   if(devices.isEmpty()){publish("","未检测到输出设备",-1);return;}
   AudioDeviceInfo d=devices.get(0);String name=d.getProductName().toString(),address=d.getAddress();
   boolean bluetooth=d.getType()==AudioDeviceInfo.TYPE_BLUETOOTH_A2DP||d.getType()==AudioDeviceInfo.TYPE_BLE_HEADSET||d.getType()==AudioDeviceInfo.TYPE_BLE_SPEAKER||d.getType()==AudioDeviceInfo.TYPE_HEARING_AID;
   String transport=bluetooth?"蓝牙":d.getType()==AudioDeviceInfo.TYPE_USB_DEVICE||d.getType()==AudioDeviceInfo.TYPE_USB_HEADSET?"USB":d.getType()==AudioDeviceInfo.TYPE_WIRED_HEADPHONES||d.getType()==AudioDeviceInfo.TYPE_WIRED_HEADSET?"有线":"系统";
   boolean temporary=bluetooth&&(address==null||address.isEmpty());if(temporary)address="session:"+d.getId();
   publish(key(d.getType(),name,address),transport+" · "+name+(confirmed?"":"（按连接状态识别）")+(temporary?" · 临时设备标识":""),d.getType());
  }catch(Exception ignored){}
 }
 void publish(String key,String label,int type){handler.post(()->{if(!closed)listener.route(key,label,type);});}
 static boolean headphone(int type){return priority(type)>1;}
 static int priority(int type){switch(type){case AudioDeviceInfo.TYPE_USB_DEVICE:case AudioDeviceInfo.TYPE_USB_HEADSET:case AudioDeviceInfo.TYPE_USB_ACCESSORY:return 4;case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP:case AudioDeviceInfo.TYPE_BLE_HEADSET:case AudioDeviceInfo.TYPE_BLE_SPEAKER:case AudioDeviceInfo.TYPE_HEARING_AID:return 3;case AudioDeviceInfo.TYPE_WIRED_HEADPHONES:case AudioDeviceInfo.TYPE_WIRED_HEADSET:return 2;case AudioDeviceInfo.TYPE_BUILTIN_SPEAKER:return 1;default:return 0;}}
 public void close(){closed=true;handler.removeCallbacks(poll);scan.removeCallbacksAndMessages(null);scanner.quitSafely();audio.unregisterAudioDeviceCallback(callback);}
}
