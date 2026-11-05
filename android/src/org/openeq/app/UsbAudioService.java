package org.openeq.app;
import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.hardware.usb.*;
import android.media.*;
import android.media.projection.*;
import android.net.Uri;
import android.os.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.*;
import org.openeq.dsp.*;

/** User-authorized USB output; all audio packets go through libusb, never AudioTrack. */
public final class UsbAudioService extends Service {
 static final String STOP="org.openeq.app.USB_STOP";
 static final String VOLUME_DOWN="org.openeq.app.USB_VOLUME_DOWN",VOLUME_UP="org.openeq.app.USB_VOLUME_UP",MUTE="org.openeq.app.USB_MUTE";
 static volatile UsbAudioService instance;static volatile boolean processing;
 static volatile String display="选择音源应用和USB DAC，然后开始处理",diagnostics="{}",phase="idle",activeSourcePackage="",activeSourceLabel="";
 static volatile int actualRate=384000;static volatile Preset latest;
 static volatile boolean dopActive;static volatile String stopReason="not_started";
 UsbMediaVolume mediaVolume;
 final AtomicBoolean running=new AtomicBoolean();volatile long nativeHandle;volatile Thread worker;
 final AtomicBoolean stopCleanupStarted=new AtomicBoolean();
 volatile long stopRequestedAtNs;
 static final int MAX_USB_RECOVERIES=2;volatile int usbRecoveries;volatile boolean recoveryActive;volatile boolean usbDetachSignal;
 volatile PcmSource activeSource;volatile InputStream activeFile;volatile AudioRecord recording;MediaProjection projection;PowerManager.WakeLock wake;
 String deviceName;boolean captureSession;int captureUid=-1;
 long captureFramesRead,captureZeroFrames,captureZeroRun,captureMaxZeroRun,captureInvalidSamples;int captureBufferFrames;
 final AudioTimestamp captureTimestamp=new AudioTimestamp();
 final Handler notifications=new Handler(Looper.getMainLooper());final AtomicBoolean notificationQueued=new AtomicBoolean();JSONObject capabilitySnapshot;
 volatile String lastNotificationKey="";volatile long lastNotificationAt;
 final Runnable refreshNotification=()->{notificationQueued.set(false);if(!running.get())return;Notification built=notification();String key=String.valueOf(built.extras.getCharSequence(Notification.EXTRA_TITLE))+'\n'+String.valueOf(built.extras.getCharSequence(Notification.EXTRA_TEXT));long now=SystemClock.elapsedRealtime();if(key.equals(lastNotificationKey)&&now-lastNotificationAt<5000)return;lastNotificationKey=key;lastNotificationAt=now;getSystemService(NotificationManager.class).notify(23,built);};
 void notifyState(){if(notificationQueued.compareAndSet(false,true))notifications.postDelayed(refreshNotification,1000);}
 final BroadcastReceiver detached=new BroadcastReceiver(){public void onReceive(Context c,Intent i){UsbDevice d=i.getParcelableExtra(UsbManager.EXTRA_DEVICE);if(d!=null&&d.getDeviceName().equals(deviceName)){usbDetachSignal=true;if(dopActive||worker==null||!worker.isAlive()){stopReason="usb_detached";stopPlayback();return;}notifications.postDelayed(()->{if(running.get()&&nativeHandle==0&&!recoveryActive){stopReason="usb_detached";stopPlayback();}},5000);}}};
 public static void update(Preset p){latest=p.copy();}
 static boolean busy(){UsbAudioService service=instance;return service!=null&&service.worker!=null;}
 public static void stop(Context context){UsbAudioService s=instance;if(s!=null){stopReason="user_stop";s.stopPlayback();}}
 public IBinder onBind(Intent intent){return null;}
 public void onCreate(){super.onCreate();instance=this;UsbVolume.load(this);NotificationManager manager=getSystemService(NotificationManager.class);manager.createNotificationChannel(new NotificationChannel("usb-audio","USB音频",NotificationManager.IMPORTANCE_LOW));IntentFilter f=new IntentFilter(UsbManager.ACTION_USB_DEVICE_DETACHED);if(Build.VERSION.SDK_INT>=33)registerReceiver(detached,f,Context.RECEIVER_NOT_EXPORTED);else registerReceiver(detached,f);}
 PendingIntent action(int id,String name){return PendingIntent.getService(this,id,new Intent(this,UsbAudioService.class).setAction(name),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);}
 void volumeChanged(){notifications.post(()->{if(mediaVolume!=null)mediaVolume.sync();notifyState();});}
 Notification notification(){
  PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,UsbAudioService.class).setAction(STOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  Notification.Builder builder=new Notification.Builder(this,"usb-audio").setSmallIcon(android.R.drawable.ic_media_play).setContentTitle("OpenDSP · "+(dopActive?"DSD直送":"USB音量 "+UsbVolume.label())).setContentText(display).setContentIntent(open).setOngoing(true);
  if(!dopActive)builder.addAction(new Notification.Action.Builder(null,"音量−",action(52,VOLUME_DOWN)).build()).addAction(new Notification.Action.Builder(null,UsbVolume.state.muted?"取消静音":"静音",action(53,MUTE)).build()).addAction(new Notification.Action.Builder(null,"音量＋",action(54,VOLUME_UP)).build());
  builder.addAction(new Notification.Action.Builder(null,"停止处理",stop).build());
  if(mediaVolume!=null)builder.setStyle(new Notification.MediaStyle().setMediaSession(mediaVolume.session.getSessionToken()).setShowActionsInCompactView(0,1,3));
  return builder.build();
 }
 public int onStartCommand(Intent intent,int flags,int startId){
  if(intent==null)return START_NOT_STICKY;
  if(STOP.equals(intent.getAction())){stopReason="notification_stop";stopPlayback();if(worker==null)stopSelf(startId);return START_NOT_STICKY;}
  if(VOLUME_DOWN.equals(intent.getAction())||VOLUME_UP.equals(intent.getAction())||MUTE.equals(intent.getAction())){
   if(worker!=null&&!dopActive){if(MUTE.equals(intent.getAction()))UsbVolume.toggle(this);else UsbVolume.adjust(this,VOLUME_UP.equals(intent.getAction())?UsbVolume.STEP:-UsbVolume.STEP);}else if(worker==null)stopSelf(startId);
   return START_NOT_STICKY;
  }
  if(worker!=null&&worker.isAlive())return START_NOT_STICKY;
  boolean capture=intent.getBooleanExtra("capture",false);
  try{
   captureSession=capture;captureUid=-1;captureFramesRead=captureZeroFrames=captureZeroRun=captureMaxZeroRun=captureInvalidSamples=0;captureBufferFrames=0;activeSourcePackage=capture?intent.getStringExtra("source_package"):"";if(activeSourcePackage==null)activeSourcePackage="";
   activeSourceLabel=capture?CaptureSources.label(activeSourcePackage):"辅助文件播放";
   phase="preparing";display="正在连接USB输出 · "+activeSourceLabel;diagnostics="{}";capabilitySnapshot=null;stopReason="running";dopActive=intent.getIntExtra("mode",0)!=0;stopCleanupStarted.set(false);stopRequestedAtNs=0;usbRecoveries=0;recoveryActive=false;usbDetachSignal=false;
   if(Build.VERSION.SDK_INT>=29)startForeground(23,notification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK|(capture?ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION:0));
   else startForeground(23,notification());
   latest=Preset.parse(intent.getStringExtra("preset"));deviceName=intent.getStringExtra("device");
   if(!dopActive)mediaVolume=new UsbMediaVolume(this);
   wake=getSystemService(PowerManager.class).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,"OpenDSP:UsbAudio");wake.setReferenceCounted(false);wake.acquire(6*60*60*1000L);
   running.set(true);worker=new Thread(()->run(intent,startId),"OpenDSP-USB-producer");worker.start();
  }catch(Exception e){phase="error";stopReason="start_error";display="启动失败："+e.getMessage();if(mediaVolume!=null){mediaVolume.close();mediaVolume=null;}if(wake!=null&&wake.isHeld())wake.release();stopForeground(STOP_FOREGROUND_REMOVE);stopSelf(startId);}
  return START_NOT_STICKY;
 }
 void stopPlayback(){
  boolean wasRunning=running.getAndSet(false);if(wasRunning){stopRequestedAtNs=System.nanoTime();phase="stopping";display="已请求停止USB发送，正在取消传输";}long handle=nativeHandle;if(handle!=0)UsbNative.cancel(handle);
  if(stopCleanupStarted.compareAndSet(false,true)){
   AudioRecord record=recording;InputStream file=activeFile;
   new Thread(()->{if(record!=null)try{record.stop();}catch(Exception ignored){}if(file!=null)try{file.close();}catch(Exception ignored){}},"OpenDSP-stop-input").start();
  }
 }
 static ArrayList<UsbInterface> claim(UsbDevice device,UsbDeviceConnection connection)throws IOException{
  LinkedHashMap<Integer,UsbInterface> selected=new LinkedHashMap<>();
  for(int n=0;n<device.getInterfaceCount();n++){
   UsbInterface item=device.getInterface(n);if(item.getInterfaceClass()!=UsbConstants.USB_CLASS_AUDIO)continue;
   boolean choose=item.getInterfaceSubclass()==1;
   if(item.getInterfaceSubclass()==2)for(int e=0;e<item.getEndpointCount();e++){UsbEndpoint endpoint=item.getEndpoint(e);if(endpoint.getDirection()==UsbConstants.USB_DIR_OUT&&endpoint.getType()==UsbConstants.USB_ENDPOINT_XFER_ISOC)choose=true;}
   if(choose&&!selected.containsKey(item.getId()))selected.put(item.getId(),item);
  }
  ArrayList<UsbInterface> claimed=new ArrayList<>();
  try{for(UsbInterface item:selected.values()){if(!connection.claimInterface(item,true))throw new IOException("无法接管USB接口 "+item.getId()+"，可能已被其他USB播放器占用");claimed.add(item);}return claimed;}
  catch(IOException e){for(UsbInterface item:claimed){connection.releaseInterface(item);UsbNative.attach(connection.getFileDescriptor(),item.getId());}throw e;}
 }
 Object[] openUsb(UsbManager manager,int rate,int bits)throws Exception{
  UsbDevice device=manager.getDeviceList().get(deviceName);
  if(device==null||!manager.hasPermission(device))throw new IOException("USB未连接或未授权");
  UsbDeviceConnection opened=manager.openDevice(device);if(opened==null)throw new IOException("无法打开USB设备");
  ArrayList<UsbInterface> claimedNow=new ArrayList<>();long openedHandle=0;
  try{claimedNow=claim(device,opened);String capabilities=UsbNative.probe(opened.getFileDescriptor());openedHandle=UsbNative.open(opened.getFileDescriptor(),rate,bits,false);return new Object[]{opened,claimedNow,capabilities,Long.valueOf(openedHandle)};}
  catch(Exception e){if(openedHandle!=0)try{UsbNative.close(openedHandle);}catch(Exception ignored){}for(UsbInterface item:claimedNow)try{opened.releaseInterface(item);UsbNative.attach(opened.getFileDescriptor(),item.getId());}catch(Exception ignored){}try{opened.close();}catch(Exception ignored){}throw e;}
 }
 void closeUsb(UsbDeviceConnection connection,ArrayList<UsbInterface> claimed,long handle){
  if(handle!=0){try{UsbNative.cancel(handle);}catch(Exception ignored){}try{UsbNative.awaitStopped(handle,3000);}catch(Exception ignored){}try{UsbNative.close(handle);}catch(Exception ignored){}}
  if(connection!=null){for(UsbInterface item:claimed)try{connection.releaseInterface(item);UsbNative.attach(connection.getFileDescriptor(),item.getId());}catch(Exception ignored){}try{connection.close();}catch(Exception ignored){}}
 }
 void run(Intent intent,int startId){
  UsbDeviceConnection connection=null;ArrayList<UsbInterface> claimed=new ArrayList<>();long handle=0;boolean success=false,failed=false;
  try{
   android.os.Process.setThreadPriority(android.os.Process.THREAD_PRIORITY_URGENT_AUDIO);
   if(captureSession){captureUid=CaptureSources.uid(this,activeSourcePackage);UsbCapturePreflight.check(this);}
   UsbManager manager=(UsbManager)getSystemService(USB_SERVICE);
   boolean capture=intent.getBooleanExtra("capture",false);int mode=intent.getIntExtra("mode",0),rate=intent.getIntExtra("rate",384000),bits=intent.getIntExtra("bits",32);
   String capabilities;
   if(capture&&mode!=0)throw new IOException("播放捕获只支持PCM DSP输出");
   if(mode!=0){
    UsbDevice device=manager.getDeviceList().get(deviceName);
    if(device==null||!manager.hasPermission(device))throw new IOException("USB未连接或未授权");
    connection=manager.openDevice(device);if(connection==null)throw new IOException("无法打开USB设备");
    claimed=claim(device,connection);capabilities=UsbNative.probe(connection.getFileDescriptor());
    activeFile=getContentResolver().openInputStream(intent.getData());if(activeFile==null)throw new IOException("无法读取DSF");
    try(DsfSource dsf=new DsfSource(activeFile)){
     rate=mode==1?176400:352800;if(dsf.carrierRate!=rate)throw new IOException("DSF与所选DSD64/128不匹配");bits=intent.getIntExtra("bits",32);if(bits<24)throw new IOException("DoP需要24或32-bit载体");
     if(!running.get())throw new IOException("已取消");handle=UsbNative.open(connection.getFileDescriptor(),rate,bits,true);nativeHandle=handle;processing=true;actualRate=rate;
     phase="buffering";display="DSD"+(mode==1?"64":"128")+" · DoP "+rate+" Hz/"+bits+"-bit · DSP旁路";
     byte[] payload=new byte[4096];int count;long reported=0;
     while(running.get()&&(count=dsf.read(payload,payload.length/4))>0){UsbNative.writeDop(handle,payload,count*4);if(SystemClock.elapsedRealtime()-reported>500){report(handle,capabilities);reported=SystemClock.elapsedRealtime();}}
    }
   }else{
    PcmSource input=capture?capture(intent):openFile(intent.getData());activeSource=input;int inputRate=input.rate(),inputBits=input.bits();
    PcmSource source=capture||inputRate!=rate?new NativeSincSource(input,rate,capture):input;activeSource=source;
    if(!running.get())throw new IOException("已取消");Object[] usb=openUsb(manager,rate,bits);connection=(UsbDeviceConnection)usb[0];claimed=(ArrayList<UsbInterface>)usb[1];capabilities=(String)usb[2];handle=((Long)usb[3]).longValue();nativeHandle=handle;processing=true;actualRate=rate;
    phase="buffering";display=activeSourceLabel+" · USB已连接，正在等待输入";
    Preset active=latest;DspRack rack=new DspRack(rate,active);UsbVolume.State volume=UsbVolume.state;VolumeRamp master=new VolumeRamp(rate,volume.gain());double[] block=new double[2048];long reported=0;double energy=0,outputEnergy=0,peak=0,inputPeak=0,maxSourceMs=0,maxDspMs=0,maxWriteMs=0;long frames=0;boolean everSignal=false;int recoveries=0;
    while(running.get()){
     if(latest!=active){active=latest;rack.configure(active);}
     if(volume!=UsbVolume.state){volume=UsbVolume.state;master.target(volume.gain());}
     long blockStart=System.nanoTime();int count=source.read(block,block.length/2);maxSourceMs=Math.max(maxSourceMs,(System.nanoTime()-blockStart)/1000000d);if(count==0)break;
     blockStart=System.nanoTime();
     for(int i=0;i<count*2;i+=2){energy+=block[i]*block[i]+block[i+1]*block[i+1];inputPeak=Math.max(inputPeak,Math.max(Math.abs(block[i]),Math.abs(block[i+1])));rack.process(block[i],block[i+1]);double gain=master.next();block[i]=rack.left*gain;block[i+1]=rack.right*gain;outputEnergy+=block[i]*block[i]+block[i+1]*block[i+1];peak=Math.max(peak,Math.max(Math.abs(block[i]),Math.abs(block[i+1])));}frames+=count;
     maxDspMs=Math.max(maxDspMs,(System.nanoTime()-blockStart)/1000000d);
     try{
      blockStart=System.nanoTime();UsbNative.writePcm(handle,block,count*2);maxWriteMs=Math.max(maxWriteMs,(System.nanoTime()-blockStart)/1000000d);
     }catch(Throwable t){
      String message=String.valueOf(t.getMessage());
      if(!running.get()||recoveries>=MAX_USB_RECOVERIES||!message.contains("NO_DEVICE"))throw t;
      recoveries++;usbRecoveries=recoveries;recoveryActive=true;phase="reconnecting";display="USB连接中断，正在尝试重新连接DAC（"+recoveries+"/"+MAX_USB_RECOVERIES+"）";notifyState();
      closeUsb(connection,claimed,handle);connection=null;claimed=new ArrayList<>();handle=0;nativeHandle=0;processing=false;
      long deadline=SystemClock.elapsedRealtime()+30000;boolean found=false;
      while(running.get()&&SystemClock.elapsedRealtime()<deadline){
       UsbDevice device=manager.getDeviceList().get(deviceName);
       if(device!=null&&manager.hasPermission(device)){found=true;break;}
       SystemClock.sleep(500);
      }
      if(!found){stopReason="usb_detached";throw t;}
      usb=openUsb(manager,rate,bits);connection=(UsbDeviceConnection)usb[0];claimed=(ArrayList<UsbInterface>)usb[1];capabilities=(String)usb[2];handle=((Long)usb[3]).longValue();nativeHandle=handle;processing=true;recoveryActive=false;
      phase="buffering";display=activeSourceLabel+" · USB已重新连接，继续处理";notifyState();
      continue;
     }
     if(SystemClock.elapsedRealtime()-reported>500){
      JSONObject status=readStatus(handle,capabilities);double inputDb=CaptureMeter.rmsDb(energy,frames),outputDb=CaptureMeter.rmsDb(outputEnergy,frames);boolean hasSignal=CaptureMeter.hasSignal(inputDb);everSignal|=hasSignal;
      status.put("input_rate",inputRate).put("input_bits",inputBits).put("input_format",capture?"float32 · 系统播放捕获":inputBits+"bit · 文件解码").put("input_energy",energy).put("processed_frames",frames).put("source_kind",capture?"AudioPlaybackCapture":"local")
       .put("input_rms_dbfs",Double.isFinite(inputDb)?inputDb:JSONObject.NULL).put("output_rms_dbfs",Double.isFinite(outputDb)?outputDb:JSONObject.NULL).put("output_peak_dbfs",peak>0?20*Math.log10(peak):JSONObject.NULL)
       .put("source_has_signal",hasSignal).put("source_signal_seen",everSignal).put("source_package",activeSourcePackage).put("source_label",activeSourceLabel).put("source_uid",captureUid)
       .put("max_source_resample_ms",maxSourceMs).put("max_dsp_ms",maxDspMs).put("max_usb_write_ms",maxWriteMs);
      status.put("input_peak_dbfs",inputPeak>0?20*Math.log10(inputPeak):JSONObject.NULL).put("dsp_protected_frames",rack.clipped());
      status.put("usb_volume_percent",volume.percent).put("usb_muted",volume.muted).put("usb_master_gain",master.current()).put("usb_recoveries",recoveries);
      if(capture){
       status.put("capture_frames_read",captureFramesRead).put("capture_zero_frames",captureZeroFrames).put("capture_zero_run_frames",captureZeroRun).put("capture_max_zero_run_frames",captureMaxZeroRun).put("capture_invalid_samples",captureInvalidSamples).put("capture_buffer_frames",captureBufferFrames);
       AudioRecord record=recording;
       if(record!=null&&record.getTimestamp(captureTimestamp,AudioTimestamp.TIMEBASE_MONOTONIC)==AudioRecord.SUCCESS)status.put("capture_timestamp_frames",captureTimestamp.framePosition).put("capture_timestamp_ns",captureTimestamp.nanoTime);
      }
      status.put("resampler",source instanceof NativeSincSource?"native_stereo_sinc64":"same_rate_bypass");
      if(capture)((NativeSincSource)source).clockPpm((status.getDouble("queued_frames")/(rate*status.optDouble("ring_target_ms",96)/1000)-1)*1000);
      if(running.get()){phase=CaptureMeter.phase(capture,inputDb,status.optLong("completed_frames",0));display=activeSourceLabel+" · "+("processing".equals(phase)?"已捕获信号，正在处理":"buffering".equals(phase)?"正在缓冲":"等待播放；若已播放，来源可能禁止捕获");}
      status.put("phase",phase);diagnostics=status.toString();notifyState();
      reported=SystemClock.elapsedRealtime();energy=0;outputEnergy=0;peak=0;inputPeak=0;frames=0;
     }
    }
   }
   if(running.get()){UsbNative.finish(handle);report(handle,capabilities);success=true;}
  }catch(Throwable e){failed=running.get();if(failed){phase="error";if(!stopReason.equals("usb_detached"))stopReason="pipeline_error";display="处理已中断："+(e.getMessage()==null?e.getClass().getSimpleName():e.getMessage());try{JSONObject d=new JSONObject(diagnostics);d.put("last_error",display);diagnostics=d.toString();}catch(Exception ignored){}}}
  finally{
   running.set(false);processing=false;nativeHandle=0;
   if(handle!=0){
    try{
     UsbNative.cancel(handle);
     JSONObject finalStatus=new JSONObject(UsbNative.awaitStopped(handle,3000)),merged=new JSONObject(diagnostics);
     for(Iterator<String> keys=finalStatus.keys();keys.hasNext();){String key=keys.next();merged.put(key,finalStatus.get(key));}
     merged.put("usb_io_stopped",true);diagnostics=merged.toString();
     if(!failed)display="USB发送已停止，正在释放DAC";
    }catch(Exception e){try{JSONObject d=new JSONObject(diagnostics);d.put("cancel_error",e.toString()).put("usb_io_stopped",false);diagnostics=d.toString();}catch(Exception ignored){}}
    finally{try{UsbNative.close(handle);}catch(Exception e){try{JSONObject d=new JSONObject(diagnostics);d.put("release_error",e.toString());diagnostics=d.toString();}catch(Exception ignored){}}}
   }
   PcmSource source=activeSource;activeSource=null;if(source!=null)try{source.close();}catch(Exception ignored){}
   InputStream file=activeFile;activeFile=null;if(file!=null)try{file.close();}catch(Exception ignored){}
   MediaProjection oldProjection=projection;projection=null;if(oldProjection!=null)oldProjection.stop();recording=null;
   if(connection!=null){for(UsbInterface item:claimed)try{connection.releaseInterface(item);UsbNative.attach(connection.getFileDescriptor(),item.getId());}catch(Exception ignored){}connection.close();}
   if(wake!=null&&wake.isHeld())wake.release();if(success)stopReason="source_completed";if(!failed){phase="stopped";display=success?"处理结束，USB已释放":stopReason.equals("projection_revoked")?"系统已结束播放捕获授权，需要重新开始":stopReason.equals("usb_detached")?"DAC已断开，处理已停止":"已停止处理，USB已释放";}
   try{JSONObject closed=new JSONObject(diagnostics);closed.put("stop_reason",stopReason).put("session_closed",true).put("usb_claimed",false).put("running",false).put("phase",phase).put("usb_recoveries",usbRecoveries).put("source_has_signal",false).put("input_rms_dbfs",JSONObject.NULL).put("output_rms_dbfs",JSONObject.NULL);
    if(stopRequestedAtNs>0)closed.put("stop_to_release_ms",(System.nanoTime()-stopRequestedAtNs)/1000000d);
    diagnostics=closed.toString();java.io.File reportFile=new java.io.File(getFilesDir(),"usb-last-report.json");try(FileOutputStream out=new FileOutputStream(reportFile)){out.write(diagnostics.getBytes(java.nio.charset.StandardCharsets.UTF_8));}}catch(Exception ignored){}
   final Thread ended=Thread.currentThread();new Handler(getMainLooper()).post(()->{if(worker==ended){worker=null;if(mediaVolume!=null){mediaVolume.close();mediaVolume=null;}stopForeground(STOP_FOREGROUND_REMOVE);stopSelf(startId);}});
  }
 }
 JSONObject report(long handle,String capabilities)throws Exception{
  JSONObject status=readStatus(handle,capabilities);diagnostics=status.toString();
  notifyState();return status;
 }
 JSONObject readStatus(long handle,String capabilities)throws Exception{
  if(capabilitySnapshot==null)capabilitySnapshot=new JSONObject(capabilities);
  return new JSONObject(UsbNative.status(handle)).put("capabilities",capabilitySnapshot).put("phase",phase).put("source_package",activeSourcePackage).put("source_label",activeSourceLabel).put("source_kind",captureSession?"AudioPlaybackCapture":"local");
 }
 PcmSource openFile(Uri uri)throws IOException{
  if(uri==null)throw new IOException("未选择文件");activeFile=getContentResolver().openInputStream(uri);if(activeFile==null)throw new IOException("无法读取文件");
  BufferedInputStream probe=new BufferedInputStream(activeFile,65536);activeFile=probe;probe.mark(16);byte[] magic=new byte[4];int read=probe.read(magic);probe.reset();
  if(read==4&&magic[0]=='R'&&magic[1]=='I'&&magic[2]=='F'&&magic[3]=='F')return new WavSource(probe);
  probe.close();activeFile=null;return new CodecSource(this,uri);
 }
 PcmSource capture(Intent intent)throws Exception{
  if(Build.VERSION.SDK_INT<29)throw new IOException("播放捕获需要Android 10以上");
  projection=getSystemService(MediaProjectionManager.class).getMediaProjection(intent.getIntExtra("projection_result",0),intent.getParcelableExtra("projection_data"));
  if(projection==null)throw new IOException("未取得播放捕获授权");
  final MediaProjection owner=projection;projection.registerCallback(new MediaProjection.Callback(){public void onStop(){if(projection==owner){stopReason="projection_revoked";stopPlayback();}}},new Handler(getMainLooper()));
  AudioPlaybackCaptureConfiguration.Builder builder=new AudioPlaybackCaptureConfiguration.Builder(projection).addMatchingUsage(AudioAttributes.USAGE_MEDIA).addMatchingUsage(AudioAttributes.USAGE_GAME).addMatchingUsage(AudioAttributes.USAGE_UNKNOWN);
  CaptureSources.scope(builder,captureUid);AudioPlaybackCaptureConfiguration config=builder.build();
  int rate=48000,min=AudioRecord.getMinBufferSize(rate,AudioFormat.CHANNEL_IN_STEREO,AudioFormat.ENCODING_PCM_FLOAT);
  final AudioRecord record=new AudioRecord.Builder().setAudioPlaybackCaptureConfig(config).setAudioFormat(new AudioFormat.Builder().setSampleRate(rate).setChannelMask(AudioFormat.CHANNEL_IN_STEREO).setEncoding(AudioFormat.ENCODING_PCM_FLOAT).build()).setBufferSizeInBytes(Math.max(min*4,16384)).build();
  if(record.getState()!=AudioRecord.STATE_INITIALIZED){record.release();throw new IOException("无法初始化允许的应用播放捕获");}
  captureBufferFrames=record.getBufferSizeInFrames();recording=record;
  return new PcmSource(){final float[] input=new float[512];boolean started;public int rate(){return 48000;}public int bits(){return 32;}public int read(double[] output,int frames)throws IOException{
   if(!running.get())return 0;if(!started){record.startRecording();started=true;}int count=record.read(input,0,Math.min(frames*2,input.length),AudioRecord.READ_BLOCKING);if(count<=0||count%2!=0)throw new IOException("播放捕获中断："+count);
   for(int i=0;i<count;i++){if(!Float.isFinite(input[i]))captureInvalidSamples++;output[i]=Float.isFinite(input[i])?input[i]:0;}
   for(int i=0;i<count;i+=2){if(output[i]==0&&output[i+1]==0){captureZeroFrames++;captureZeroRun++;captureMaxZeroRun=Math.max(captureMaxZeroRun,captureZeroRun);}else captureZeroRun=0;}
   captureFramesRead+=count/2;return count/2;
  }public void close(){try{record.stop();}catch(Exception ignored){}record.release();}};
 }
 protected void dump(FileDescriptor fd,PrintWriter writer,String[] args){try{
  JSONObject d=new JSONObject(diagnostics).put("process_pid",android.os.Process.myPid()).put("stop_reason",stopReason).put("background_restricted",getSystemService(ActivityManager.class).isBackgroundRestricted()).put("battery_exempt",getSystemService(PowerManager.class).isIgnoringBatteryOptimizations(getPackageName()));
  try(BufferedReader reader=new BufferedReader(new FileReader("/proc/self/cgroup"))){StringBuilder groups=new StringBuilder();String line;while((line=reader.readLine())!=null)groups.append(line).append('\n');d.put("process_cgroup",groups.toString());}catch(IOException ignored){}
  writer.println(d.toString());
 }catch(Exception e){writer.println(diagnostics);}}
 public void onDestroy(){if(running.get()&&stopReason.equals("running"))stopReason="service_destroyed";stopPlayback();if(mediaVolume!=null){mediaVolume.close();mediaVolume=null;}notifications.removeCallbacksAndMessages(null);notificationQueued.set(false);unregisterReceiver(detached);if(instance==this)instance=null;super.onDestroy();}
}
