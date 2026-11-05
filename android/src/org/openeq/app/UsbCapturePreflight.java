package org.openeq.app;
import android.content.Context;
import android.media.*;
import android.provider.Settings;
import java.io.IOException;

/** Detects the system-USB/capture/direct-USB conflict; never writes protected settings. */
final class UsbCapturePreflight {
 static void check(Context context)throws IOException{
  AudioManager audio=(AudioManager)context.getSystemService(Context.AUDIO_SERVICE);
  boolean systemUsb=false;for(AudioDeviceInfo device:audio.getDevices(AudioManager.GET_DEVICES_OUTPUTS))
   if(device.getType()==AudioDeviceInfo.TYPE_USB_DEVICE||device.getType()==AudioDeviceInfo.TYPE_USB_HEADSET||device.getType()==AudioDeviceInfo.TYPE_USB_ACCESSORY)systemUsb=true;
  if(!systemUsb)return;
  int disabled=Settings.Secure.getInt(context.getContentResolver(),"usb_audio_automatic_routing_disabled",0);
  if(disabled==0)throw new IOException("系统仍占用USB音频路由。请在开发者选项开启“停用USB音频路由”，停止处理并重新插拔DAC后再开始，避免与直连争用。");
  throw new IOException("系统USB音频路由尚未退出。请停止处理并重新插拔DAC，使“停用USB音频路由”生效后重试。");
 }
}
