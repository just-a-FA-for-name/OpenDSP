package org.openeq.app;
import android.app.*;
import android.os.Bundle;
import android.content.Intent;
import org.json.*;
import java.io.*;
import java.util.concurrent.*;

public class OpenEqInstrumentation extends Instrumentation {
    Bundle arguments;
    @Override public void onCreate(Bundle arguments){super.onCreate(arguments);this.arguments=arguments;start();}
    void check(boolean yes,String label){if(!yes)throw new AssertionError(label);}
    void mobileFeatures(Bundle result)throws Exception{
        android.content.Context context=getTargetContext();check(!((KeyguardManager)context.getSystemService(android.content.Context.KEYGUARD_SERVICE)).isKeyguardLocked(),"Unlock phone for workbench checks");
        android.content.SharedPreferences prefs=context.getSharedPreferences("MainActivity",0),sync=PresetSync.prefs(context);
        java.util.Map<String,?> saved=new java.util.HashMap<>(prefs.getAll()),savedSync=new java.util.HashMap<>(sync.getAll());
        MainActivity main=(MainActivity)startActivitySync(new Intent(context,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));waitForIdleSync();
        JSONObject report=new JSONObject().put("audio_played",false).put("real_pc_preset_modified",false);
        try{
            runOnMainSync(()->{if(main.routes!=null)main.routes.close();main.preset=Preset.defaults();main.preset.name="UI-test-temporary";main.changed();});
            for(String page:new String[]{"工作台","均衡","机架","预设","目标","同步","设备"}){runOnMainSync(()->{main.page=page;main.render();});waitForIdleSync();check(main.body.getChildCount()>0,"Page rendered "+page);}
            runOnMainSync(()->{main.page="均衡";main.render();});waitForIdleSync();
            runOnMainSync(()->{
                double hz=main.preset.bands.get(3).hz;float x=main.chart.x(hz),y=main.chart.y(main.preset.preamp);long now=android.os.SystemClock.uptimeMillis();
                main.chart.dispatchTouchEvent(android.view.MotionEvent.obtain(now,now,android.view.MotionEvent.ACTION_DOWN,x,y,0));
                main.chart.dispatchTouchEvent(android.view.MotionEvent.obtain(now,now+50,android.view.MotionEvent.ACTION_MOVE,x+main.dp(28),y-main.dp(14),0));
                main.chart.dispatchTouchEvent(android.view.MotionEvent.obtain(now,now+70,android.view.MotionEvent.ACTION_UP,x+main.dp(28),y-main.dp(14),0));
                check(main.preset.bands.get(3).hz!=hz,"Graph drag changes selected band");main.history(false);check(main.preset.bands.get(3).hz==hz,"Graph undo");main.history(true);check(main.preset.bands.get(3).hz!=hz,"Graph redo");
            });
            String pair;try(InputStream in=new FileInputStream(new File(context.getExternalFilesDir("interop"),"sync-test-pair.json"))){pair=MainActivity.readText(in,8192);}final String pairing=pair;
            runOnMainSync(()->{try{PresetSync.pair(main,pairing);}catch(Exception e){throw new RuntimeException(e);}});
            PresetSync.sync(context,"pull");check(PresetSync.local(context).preamp==-3,"Receive PC fixture");
            runOnMainSync(()->{main.remember();main.preset.preamp=-7;main.changed();});PresetSync.sync(context,"auto");
            JSONObject remote=PresetSync.request(context,null);check(remote.getJSONObject("preset").getDouble("preamp_db")==-7,"Automatic phone to PC");
            Preset next=Preset.parse(remote.getJSONObject("preset").toString());next.preamp=-5;
            PresetSync.request(context,new JSONObject().put("cmd","apply_preset").put("expected_revision",remote.getString("revision")).put("auto_headroom",false).put("preset",next.json()));
            PresetSync.sync(context,"auto");check(PresetSync.local(context).preamp==-5,"Automatic PC to phone");
            runOnMainSync(()->{main.preset.preamp=-6;main.changed();});remote=PresetSync.request(context,null);next.preamp=-4;
            PresetSync.request(context,new JSONObject().put("cmd","apply_preset").put("expected_revision",remote.getString("revision")).put("auto_headroom",false).put("preset",next.json()));
            boolean conflict=false;try{PresetSync.sync(context,"auto");}catch(IOException expected){conflict=true;}check(conflict&&PresetSync.local(context).preamp==-6,"Conflict preserves phone edits");check(PresetSync.request(context,null).getJSONObject("preset").getDouble("preamp_db")==-4,"Conflict preserves PC edits");
            PresetSync.sync(context,"pull");check(PresetSync.local(context).preamp==-4,"Explicit conflict resolution");
            runOnMainSync(()->{PresetSync.start(main);main.moveTaskToBack(true);});
            for(int i=0;i<30&&!SyncService.running;i++)Thread.sleep(100);check(SyncService.running,"Foreground sync service starts");
            remote=PresetSync.request(context,null);next.preamp=-8;
            PresetSync.request(context,new JSONObject().put("cmd","apply_preset").put("expected_revision",remote.getString("revision")).put("auto_headroom",false).put("preset",next.json()));
            for(int i=0;i<80&&PresetSync.local(context).preamp!=-8;i++)Thread.sleep(100);
            check(PresetSync.local(context).preamp==-8,"Actual service syncs while Activity backgrounded");
            runOnMainSync(()->PresetSync.stop(main));for(int i=0;i<30&&SyncService.running;i++)Thread.sleep(100);check(!SyncService.running,"Sync service stops");
            runOnMainSync(()->main.deviceKey="fixture-device-changed");boolean deviceConflict=false;try{PresetSync.sync(context,"auto");}catch(IOException expected){deviceConflict=true;}check(deviceConflict,"Device change pauses automatic overwrite");
            report.put("result","PASS_MOBILE_FEATURES").put("pages",7).put("graph_drag_undo_redo",true).put("actual_network_bidirectional_sync",true).put("background_sync_service",true).put("conflict_preserves_both",true).put("device_switch_guard",true);
            result.putString("result",report.toString());
        }finally{
            runOnMainSync(()->{PresetSync.stop(main);restorePreferences(prefs,saved);restorePreferences(sync,savedSync);try{main.preset=Preset.parse(prefs.getString("current",""));UsbAudioService.update(main.preset);RootEqService.update(main.preset);}catch(Exception ignored){}main.finish();});
            try(FileOutputStream output=new FileOutputStream(new File(context.getExternalFilesDir("interop"),"mobile-feature-checks.json"))){output.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        }
    }
    void reject(JSONObject j)throws Exception{try{Preset.parse(j.toString());throw new AssertionError("Invalid config accepted");}catch(AssertionError e){throw e;}catch(Exception expected){}}
    org.openeq.dsp.PcmSource memoryTone(final int frames){
        return new org.openeq.dsp.PcmSource(){int at;public int rate(){return 48000;}public int bits(){return 32;}public int read(double[] out,int requested){int n=Math.min(Math.min(256,requested),frames-at);for(int i=0;i<n;i++){double x=.2*Math.sin(2*Math.PI*10003*at++/48000);out[i*2]=x;out[i*2+1]=-x;}return n;}public void close(){}};
    }
    void nativeSrc(Bundle result)throws Exception{
        org.openeq.dsp.SincSource reference=new org.openeq.dsp.SincSource(memoryTone(48000),384000,true);
        double error=0;long frames=0;double[] expected=new double[2048],actual=new double[2048];
        try(NativeSincSource nativeSource=new NativeSincSource(memoryTone(48000),384000,true)){
            for(int i=0;i<1000;i++){reference.clockPpm(137);nativeSource.clockPpm(137);}
            int n;while((n=reference.read(expected,1024))>0){int count=nativeSource.read(actual,1024);check(count==n,"Native SRC duration");for(int i=0;i<n*2;i++)error=Math.max(error,Math.abs(expected[i]-actual[i]));frames+=n;}
            check(nativeSource.read(actual,1024)==0&&error<1e-9,"Native/Java SRC sample parity "+error);
        }
        double[] out=new double[2048];double javaMs,nativeMs;int count;
        long begin=android.os.Debug.threadCpuTimeNanos();
        try(org.openeq.dsp.SincSource javaSource=new org.openeq.dsp.SincSource(memoryTone(48000*3),384000,true)){while((count=javaSource.read(out,1024))>0)check(Double.isFinite(out[count*2-1]),"Java finite");}
        javaMs=(android.os.Debug.threadCpuTimeNanos()-begin)/1e6;begin=android.os.Debug.threadCpuTimeNanos();
        try(NativeSincSource source=new NativeSincSource(memoryTone(48000*3),384000,true)){while((count=source.read(out,1024))>0)check(Double.isFinite(out[count*2-1]),"Native finite");}
        nativeMs=(android.os.Debug.threadCpuTimeNanos()-begin)/1e6;
        Preset active;try{active=Preset.parse(getTargetContext().getSharedPreferences("MainActivity",0).getString("current",""));}catch(Exception e){active=Preset.defaults();}
        DspRack rack=new DspRack(384000,active);begin=android.os.Debug.threadCpuTimeNanos();for(int i=0;i<384000*3;i++)rack.process(.1*Math.sin(i*.03),.07*Math.cos(i*.017));double rackMs=(android.os.Debug.threadCpuTimeNanos()-begin)/1e6;
        JSONObject report=new JSONObject().put("result","PASS_NATIVE_SRC_PARITY").put("frames",frames).put("max_sample_error",error).put("java_cpu_ms_for_3sec",javaMs).put("native_cpu_ms_for_3sec",nativeMs).put("active_dsp_cpu_ms_for_3sec",rackMs).put("active_main_bands",active.bands.size()).put("audio_played",false).put("input","synthetic memory only");
        try(FileOutputStream output=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"native-src-checks.json"))){output.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        result.putString("result",report.toString());
    }
    void workbenchLayout(Bundle result)throws Exception{
        // Draw only OpenDSP's own view hierarchy; never captures the lock screen or other apps.
        if(((KeyguardManager)getTargetContext().getSystemService(android.content.Context.KEYGUARD_SERVICE)).isKeyguardLocked()){result.putString("result","SKIP_LAYOUT_LOCKED");return;}
        android.content.SharedPreferences preferences=getTargetContext().getSharedPreferences("MainActivity",0);
        java.util.Map<String,?> saved=new java.util.HashMap<>(preferences.getAll());
        MainActivity main=(MainActivity)startActivitySync(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));waitForIdleSync();
        try{runOnMainSync(()->{try{
            if(main.routes!=null)main.routes.close();main.page="工作台";main.render();
            check(main.usb.workbench.sourcePick.getCount()==3,"Three source choices");
            check(!containsText(main.root,"打开 WAV")&&!containsText(main.root,"选文件 → USB播放"),"Capture-first layout");
            check(CaptureSources.uid(main,"com.netease.cloudmusic")>=10000&&CaptureSources.uid(main,"tv.danmaku.bili")>=10000,"Named-app visibility");
            int width=main.getResources().getDisplayMetrics().widthPixels,height=main.getResources().getDisplayMetrics().heightPixels;
            main.root.measure(android.view.View.MeasureSpec.makeMeasureSpec(width,android.view.View.MeasureSpec.EXACTLY),android.view.View.MeasureSpec.makeMeasureSpec(height,android.view.View.MeasureSpec.EXACTLY));main.root.layout(0,0,width,height);
            android.graphics.Bitmap bitmap=android.graphics.Bitmap.createBitmap(width,height,android.graphics.Bitmap.Config.ARGB_8888);main.root.draw(new android.graphics.Canvas(bitmap));
            try(FileOutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"workbench-layout.png"))){bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG,100,out);}bitmap.recycle();
        }catch(Exception e){throw new RuntimeException(e);}});result.putString("result","PASS_OWN_VIEW_LAYOUT: capture-first hierarchy and named-app visibility; no capture session or playback");}
        finally{runOnMainSync(main::finish);waitForIdleSync();android.content.SharedPreferences.Editor restore=preferences.edit().clear();for(java.util.Map.Entry<String,?> entry:saved.entrySet()){Object v=entry.getValue();String k=entry.getKey();if(v instanceof String)restore.putString(k,(String)v);else if(v instanceof Boolean)restore.putBoolean(k,(Boolean)v);else if(v instanceof Integer)restore.putInt(k,(Integer)v);else if(v instanceof Long)restore.putLong(k,(Long)v);else if(v instanceof Float)restore.putFloat(k,(Float)v);}restore.commit();}
    }
    void noAudio(Bundle result)throws Exception{
        java.util.ArrayList<String> cases=new java.util.ArrayList<>();
        result.putStringArrayList("cases",cases);
        check(UsbNative.version().equals("1.0.30"),"Native library load");cases.add("arm64 JNI/libusb loaded");
        try{UsbNative.open(-1,384000,32,false);throw new AssertionError("Invalid descriptor accepted");}catch(IOException expected){cases.add("invalid USB descriptor rejected");}
        String[] files=getTargetContext().getAssets().list("presets");int loaded=0;
        for(String file:files)if(file.endsWith(".json"))try(InputStream in=getTargetContext().getAssets().open("presets/"+file)){Preset p=Preset.parse(MainActivity.readText(in,262144));Preset.parse(p.encode());loaded++;}
        check(loaded>=20,"Bundled presets");cases.add(loaded+" complete preset roundtrips");
        Preset synthetic=Preset.defaults();synthetic.processing=new JSONObject("{\"dc_block\":true,\"compressor\":{\"enabled\":true,\"threshold_db\":-24},\"matrix\":{\"width\":1.1},\"spatial\":{\"enabled\":true,\"crossfeed\":0.2,\"high_width\":1.5}}");
        DspRack rack=new DspRack(384000,synthetic);
        for(int n=0;n<38400;n++){rack.process(n==0?Double.NaN:.01*Math.sin(n*.03),n==0?Double.POSITIVE_INFINITY:.02*Math.cos(n*.02));check(Double.isFinite(rack.left)&&Double.isFinite(rack.right),"Finite device DSP");}
        cases.add("384k full DSP processed only in memory");
        File fixture=new File(getTargetContext().getExternalFilesDir("interop"),"synthetic384.flac");
        if(fixture.exists())try(CodecSource source=new CodecSource(getTargetContext(),android.net.Uri.fromFile(fixture))){
            check(source.rate()==384000,"MediaCodec changed source rate");double[] samples=new double[2048];long frames=0;int n;
            while((n=source.read(samples,1024))>0){frames+=n;for(int i=0;i<n*2;i++)check(Double.isFinite(samples[i]),"Decoded sample not finite");}
            check(frames>1000,"FLAC decoded frames");cases.add("MediaCodec FLAC384 decoded without playback; output bits="+source.bits());result.putLong("decoded_frames",frames);
        }
        boolean locked=((KeyguardManager)getTargetContext().getSystemService(android.content.Context.KEYGUARD_SERVICE)).isKeyguardLocked();
        if(!locked){
            android.content.SharedPreferences preferences=getTargetContext().getSharedPreferences("MainActivity",0);
            java.util.Map<String,?> saved=new java.util.HashMap<>(preferences.getAll());
            final MainActivity main=(MainActivity)startActivitySync(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            waitForIdleSync();final Preset[] original={null};
            try{
                runOnMainSync(()->{main.getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);if(main.routes!=null)main.routes.close();original[0]=main.preset.copy();});
                check(main.page.equals("工作台"),"Application opens to processing workbench");
                runOnMainSync(()->{
                    android.widget.Spinner sources=main.usb.workbench.sourcePick;check(sources!=null&&sources.getCount()==3,"Source app selector");
                    android.widget.Button begin=main.usb.workbench.mode.equals("system")?main.usb.workbench.system.start:main.usb.workbench.start;
                    check(begin!=null&&begin.getText().toString().equals("开始处理"),"Desktop-style process control");
                    check(!containsText(main.root,"打开 WAV")&&!containsText(main.root,"选文件 → USB播放"),"No file picker in primary workflow");
                    try{check(CaptureSources.uid(main,"com.netease.cloudmusic")>=10000&&CaptureSources.uid(main,"tv.danmaku.bili")>=10000,"Named apps resolve through package visibility");check(CaptureSources.uid(main,"")==-1,"All-app scope");}catch(Exception e){throw new RuntimeException(e);}
                    String savedSource=main.usb.sourcePackage;main.usb.sourcePackage="tv.danmaku.bili";main.usb.requestSource="tv.danmaku.bili";main.usb.requestRate=384000;main.usb.requestBits=32;main.usb.requestMode=0;main.usb.action="capture";main.usb.pending=true;main.usb.flow=19;
                    Bundle savedState=new Bundle();main.usb.saveState(savedState);UsbPanel restored=new UsbPanel(main,savedState);
                    try{check(restored.pending&&restored.requestSource.equals("tv.danmaku.bili")&&restored.requestRate==384000&&restored.flow==19,"Pending authorization restores exact source/format");}finally{restored.close();}
                    main.usb.pending=false;main.usb.action=null;main.usb.sourcePackage=savedSource;main.usb.message="";
                });cases.add("capture-first workbench, named-source scope and rotation-safe request");
                for(String page:new String[]{"工作台","均衡","预设","设备","工具"}){
                    runOnMainSync(()->{main.page=page;main.render();check(main.root.getWidth()>=0,"Layout");});
                    waitForIdleSync();assertOwnWindow();cases.add("render "+page);
                }
                String[] groups={"compressor","matrix","spatial"};
                for(String group:groups){runOnMainSync(()->new MobileRackEditor(main).edit(group));waitForIdleSync();assertOwnWindow();clickOwnText("取消");waitForIdleSync();cases.add("open/cancel "+group);}
                runOnMainSync(()->new MobileRackEditor(main).chooseBank());waitForIdleSync();assertOwnWindow();clickOwnText("关闭");waitForIdleSync();cases.add("M/S bank picker");
                runOnMainSync(()->{
                    try{String before=main.preset.encode();new MobileRackEditor(main).commit("spatial",new JSONObject("{\"enabled\":true,\"high_width\":1.65}"));check(main.preset.processing.getJSONObject("spatial").getDouble("high_width")==1.65,"Spatial edit");check(!main.undo.isEmpty(),"Undo available");main.preset=Preset.parse(main.undo.pop());main.changed();check(main.preset.encode().equals(before),"Undo restore");}catch(Exception e){throw new RuntimeException(e);}
                });cases.add("spatial commit and undo");
                runOnMainSync(()->{main.outputSettings();});waitForIdleSync();assertOwnWindow();clickOwnText("取消");waitForIdleSync();cases.add("output editor");
                if(((android.hardware.usb.UsbManager)getTargetContext().getSystemService(android.content.Context.USB_SERVICE)).getDeviceList().isEmpty()){
                    runOnMainSync(()->main.usb.choose("probe"));waitForIdleSync();check(!UsbAudioService.processing,"No-device path started processing");cases.add("no DAC handled without service/permission start");
                }
            }finally{
                runOnMainSync(()->{if(original[0]!=null){main.preset=original[0];main.changed();}main.finish();});
                waitForIdleSync();android.content.SharedPreferences.Editor restore=preferences.edit().clear();
                for(java.util.Map.Entry<String,?> item:saved.entrySet()){Object value=item.getValue();String key=item.getKey();if(value instanceof String)restore.putString(key,(String)value);else if(value instanceof Boolean)restore.putBoolean(key,(Boolean)value);else if(value instanceof Integer)restore.putInt(key,(Integer)value);else if(value instanceof Long)restore.putLong(key,(Long)value);else if(value instanceof Float)restore.putFloat(key,(Float)value);}
                restore.commit();
            }
        }
        result.putString("result",locked?"PASS_RUNTIME_UI_LOCKED":"PASS_NO_AUDIO");
        result.putStringArrayList("cases",cases);result.putBoolean("ui_locked",locked);result.putInt("target_uid",android.os.Process.myUid());
        JSONObject report=new JSONObject().put("result",result.getString("result")).put("cases",new JSONArray(cases)).put("ui_locked",locked).put("audio_played",false).put("usb_dac_transfer_tested",false).put("uid",android.os.Process.myUid());
        try(FileOutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"no-audio-checks.json"))){out.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
    }
    void assertOwnWindow(){
        String observed="none";
        for(int attempt=0;attempt<20;attempt++){
            android.view.accessibility.AccessibilityNodeInfo node=getUiAutomation().getRootInActiveWindow();
            if(node!=null){CharSequence name=node.getPackageName();observed=name==null?"null":name.toString();node.recycle();if("org.openeq.app".equals(observed))return;}
            android.os.SystemClock.sleep(100);
        }
        throw new AssertionError("OpenDSP window not visible (package="+observed+"); no other app content inspected");
    }
    boolean containsText(android.view.View view,String text){
        if(view instanceof android.widget.TextView&&text.contentEquals(((android.widget.TextView)view).getText()))return true;
        if(view instanceof android.view.ViewGroup){android.view.ViewGroup group=(android.view.ViewGroup)view;for(int i=0;i<group.getChildCount();i++)if(containsText(group.getChildAt(i),text))return true;}
        return false;
    }
    void clickOwnText(String text){
        for(int attempt=0;attempt<25;attempt++){
            android.view.accessibility.AccessibilityNodeInfo root=getUiAutomation().getRootInActiveWindow();
            if(root!=null){
                if("org.openeq.app".equals(String.valueOf(root.getPackageName()))){
                    for(android.view.accessibility.AccessibilityNodeInfo node:root.findAccessibilityNodeInfosByText(text))
                        if(text.contentEquals(node.getText())&&node.isClickable()&&node.performAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_CLICK)){root.recycle();android.os.SystemClock.sleep(250);return;}
                }
                root.recycle();
            }
            android.os.SystemClock.sleep(100);
        }
        throw new AssertionError("Own-app button not available: "+text);
    }
    void usbPrepare(Bundle result)throws Exception{
        android.hardware.usb.UsbManager manager=(android.hardware.usb.UsbManager)getTargetContext().getSystemService(android.content.Context.USB_SERVICE);
        android.hardware.usb.UsbDevice device=null;for(android.hardware.usb.UsbDevice candidate:manager.getDeviceList().values())if(candidate.getVendorId()==9770&&candidate.getProductId()==6687){device=candidate;break;}
        check(device!=null,"Expected ACMEE 4X");final android.hardware.usb.UsbDevice dac=device;
        MainActivity main=(MainActivity)startActivitySync(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));waitForIdleSync();
        JSONObject report=new JSONObject().put("uid",android.os.Process.myUid()).put("vendor",dac.getVendorId()).put("product",dac.getProductId()).put("audio_submitted",false);
        android.hardware.usb.UsbDeviceConnection connection=null;java.util.ArrayList<android.hardware.usb.UsbInterface> claimed=new java.util.ArrayList<>();long handle=0;
        try{
            runOnMainSync(()->{if(main.routes!=null)main.routes.close();UsbPanel.probing=true;});
            if(!manager.hasPermission(dac)){
                runOnMainSync(()->manager.requestPermission(dac,PendingIntent.getBroadcast(getTargetContext(),907,
                    new Intent("org.openeq.app.TEST_USB_PERMISSION").setPackage("org.openeq.app"),
                    PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_MUTABLE)));
                for(int attempt=0;attempt<120&&!manager.hasPermission(dac);attempt++){
                    android.view.accessibility.AccessibilityNodeInfo root=getUiAutomation().getRootInActiveWindow();
                    if(root!=null){
                        String owner=String.valueOf(root.getPackageName());
                        if(owner.equals("com.android.systemui")&&!root.findAccessibilityNodeInfosByText("OpenDSP").isEmpty()){
                            for(android.view.accessibility.AccessibilityNodeInfo button:root.findAccessibilityNodeInfosByViewId("android:id/button1"))
                                if(button.isClickable())button.performAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_CLICK);
                        }
                        root.recycle();
                    }
                    Thread.sleep(250);
                }
            }
            check(manager.hasPermission(dac),"USB permission not granted");
            connection=manager.openDevice(dac);check(connection!=null,"USB open");
            claimed=UsbAudioService.claim(dac,connection);
            report.put("capabilities",new JSONObject(UsbNative.probe(connection.getFileDescriptor())));
            handle=UsbNative.open(connection.getFileDescriptor(),384000,32,false);
            JSONObject state=new JSONObject(UsbNative.status(handle));report.put("prepared",state);
            check(state.getInt("rate")==384000&&state.getInt("valid_bits")==32&&state.getInt("container_bits")==32,"Requested full format");
            check(state.getLong("accepted_frames")==0&&state.getLong("packets")==0,"Prepare-only must not submit audio");
            if(arguments.getString("usb_silence")!=null){
                double[] zero=new double[4096];long frames=0;
                while(frames<384000*3L){int count=(int)Math.min(zero.length,(384000*3L-frames)*2);UsbNative.writePcm(handle,zero,count);frames+=count/2;}
                UsbNative.finish(handle);JSONObject transmitted=new JSONObject(UsbNative.status(handle));report.put("digital_silence_only",true).put("audio_submitted",true).put("transmitted",transmitted);
                check(transmitted.getLong("completed_frames")==frames&&transmitted.getLong("packets")>0,"USB frame completion");
                check(transmitted.getLong("feedback_packets")>0,"Feedback did not arrive");
                check(transmitted.getLong("errors")==0&&transmitted.getLong("underrun_frames")==0,"USB errors/underruns");
                report.put("result","PASS_PCM_SILENCE");result.putString("result","PASS_PCM_SILENCE: three seconds384k32 digital zero, completed frames/feedback, no errors or underruns");
            }else{report.put("result","PASS_PREPARE_ONLY");result.putString("result","PASS_PREPARE_ONLY: USB permission/claim and384k32 readback; zero audio packets");}
        }catch(Throwable error){report.put("result","FAIL").put("error",error.toString());throw error;}
        finally{
            if(handle!=0){UsbNative.cancel(handle);UsbNative.close(handle);}
            if(connection!=null){for(android.hardware.usb.UsbInterface item:claimed){connection.releaseInterface(item);UsbNative.attach(connection.getFileDescriptor(),item.getId());}connection.close();}
            report.put("interfaces_released",true);UsbPanel.probing=false;
            try(FileOutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),arguments.getString("usb_silence")!=null?"usb-silence-checks.json":"usb-prepare-checks.json"))){out.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
            runOnMainSync(main::finish);
        }
    }
    void usbServiceSilence(Bundle result)throws Exception{
        boolean locked=((KeyguardManager)getTargetContext().getSystemService(android.content.Context.KEYGUARD_SERVICE)).isKeyguardLocked();
        MainActivity main=locked?null:(MainActivity)startActivitySync(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));if(main!=null)waitForIdleSync();
        android.content.Context owner=getTargetContext();
        String savedPreset=owner.getSharedPreferences("MainActivity",0).getString("current","");
        android.hardware.usb.UsbManager manager=(android.hardware.usb.UsbManager)getTargetContext().getSystemService(android.content.Context.USB_SERVICE);
        android.hardware.usb.UsbDevice dac=null;for(android.hardware.usb.UsbDevice item:manager.getDeviceList().values())if(item.getVendorId()==9770&&item.getProductId()==6687)dac=item;
        check(dac!=null&&manager.hasPermission(dac),"DAC must already be authorized");final String name=dac.getDeviceName();
        android.content.ContentResolver resolver=getTargetContext().getContentResolver();android.net.Uri uri=null;
        JSONObject report=new JSONObject().put("digital_silence_only",true).put("uid",android.os.Process.myUid());
        android.content.SharedPreferences volumePrefs=getTargetContext().getSharedPreferences("usb-volume",0);java.util.Map<String,?> savedVolume=new java.util.HashMap<>(volumePrefs.getAll());UsbVolume.load(getTargetContext());UsbVolume.State originalVolume=UsbVolume.state;
        try{
            android.content.ContentValues values=new android.content.ContentValues();values.put(android.provider.MediaStore.MediaColumns.DISPLAY_NAME,"OpenDSP-test-silence384.wav");values.put(android.provider.MediaStore.MediaColumns.MIME_TYPE,"audio/wav");values.put(android.provider.MediaStore.MediaColumns.RELATIVE_PATH,"Download/OpenDSP-test");values.put(android.provider.MediaStore.MediaColumns.IS_PENDING,1);
            uri=resolver.insert(android.provider.MediaStore.Downloads.EXTERNAL_CONTENT_URI,values);check(uri!=null,"Fixture URI");
            try(DataOutputStream out=new DataOutputStream(resolver.openOutputStream(uri))){
                int bytes=384000*3*8;out.writeBytes("RIFF");le(out,36+bytes,4);out.writeBytes("WAVEfmt ");le(out,16,4);le(out,1,2);le(out,2,2);le(out,384000,4);le(out,384000*8,4);le(out,8,2);le(out,32,2);out.writeBytes("data");le(out,bytes,4);
                byte[] zeros=new byte[32768];for(int remaining=bytes;remaining>0;){int n=Math.min(remaining,zeros.length);out.write(zeros,0,n);remaining-=n;}
            }
            values.clear();values.put(android.provider.MediaStore.MediaColumns.IS_PENDING,0);resolver.update(uri,values,null,null);
            final android.net.Uri source=uri;final String[] before={null};
            runOnMainSync(()->{try{before[0]=main==null?Preset.parse(savedPreset).encode():main.preset.encode();}catch(Exception e){throw new RuntimeException(e);}owner.startForegroundService(new Intent(owner,UsbAudioService.class).setData(source).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION).putExtra("device",name).putExtra("mode",0).putExtra("rate",384000).putExtra("bits",32).putExtra("preset",before[0]));});
            boolean started=false;for(int n=0;n<120;n++){if(UsbAudioService.processing){started=true;break;}Thread.sleep(100);}
            check(started,"USB service failed to start: "+UsbAudioService.display);
            runOnMainSync(()->{
                UsbAudioService s=UsbAudioService.instance;check(s!=null&&s.mediaVolume!=null,"Remote volume session");
                s.mediaVolume.provider.onSetVolumeTo(37);check(UsbVolume.state.percent==37&&!UsbVolume.state.muted,"Remote volume setting");
                if(main!=null){main.dispatchKeyEvent(new android.view.KeyEvent(android.view.KeyEvent.ACTION_DOWN,android.view.KeyEvent.KEYCODE_VOLUME_DOWN));main.dispatchKeyEvent(new android.view.KeyEvent(android.view.KeyEvent.ACTION_UP,android.view.KeyEvent.KEYCODE_VOLUME_DOWN));check(UsbVolume.state.percent==35,"Foreground volume key");}
                int level=UsbVolume.state.percent;UsbVolume.toggle(owner);check(UsbVolume.state.muted,"USB mute");UsbVolume.toggle(owner);check(!UsbVolume.state.muted&&UsbVolume.state.percent==level,"Mute preserves level");
            });
            report.put("volume_controls","PASS").put("foreground_volume_key_tested",main!=null);
            if(main!=null)runOnMainSync(()->main.moveTaskToBack(true));
            for(int n=0;n<150&&UsbAudioService.busy();n++)Thread.sleep(100);
            check(!UsbAudioService.busy(),"USB service did not drain/stop");
            JSONObject status=new JSONObject(UsbAudioService.diagnostics);report.put("status",status);
            check(status.getInt("rate")==384000&&status.getInt("valid_bits")==32,"Service format");
            check(status.getInt("usb_queue_budget_us")==64000,"USB queue coverage");
            check(status.getLong("completed_frames")==384000*3L&&status.getLong("errors")==0&&status.getLong("underrun_frames")==0,"Service packet completion");
            check(status.getBoolean("session_closed")&&!status.getBoolean("usb_claimed"),"Service release");
            check(main!=null?main.preset.encode().equals(before[0]):owner.getSharedPreferences("MainActivity",0).getString("current","").equals(savedPreset),"Service changed active preset");
            report.put("result","PASS_SERVICE_SILENCE").put("background_playback",true);result.putString("result","PASS_SERVICE_SILENCE: full PCM DSP/foreground service/background/drain/release; digital silence only");
        }catch(Throwable error){report.put("result","FAIL").put("error",error.toString());throw error;}
        finally{
            runOnMainSync(()->UsbAudioService.stop(owner));
            for(int n=0;n<60&&UsbAudioService.busy();n++)Thread.sleep(100);
            restorePreferences(volumePrefs,savedVolume);UsbVolume.state=originalVolume;
            if(uri!=null)resolver.delete(uri,null,null);
            try(FileOutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"usb-service-silence-checks.json"))){out.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
            if(main!=null)runOnMainSync(main::finish);
        }
    }
    void headphones(Bundle result)throws Exception{
        boolean locked=((KeyguardManager)getTargetContext().getSystemService(android.content.Context.KEYGUARD_SERVICE)).isKeyguardLocked();
        android.content.SharedPreferences prefs=getTargetContext().getSharedPreferences("MainActivity",0),routing=getTargetContext().getSharedPreferences("headphone-routing",0);
        java.util.Map<String,?> saved=new java.util.HashMap<>(prefs.getAll()),savedRouting=new java.util.HashMap<>(routing.getAll());
        MainActivity main=locked?null:(MainActivity)startActivitySync(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));if(main!=null)waitForIdleSync();
        JSONObject report=new JSONObject().put("audio_generated",false).put("physical_bluetooth_switch_tested",false).put("ui_locked",locked);
        try{
            routing.edit().putBoolean("pause_on_disconnect",false).apply();prefs.edit().putBoolean("autoDeviceProfiles",true).apply();
            runOnMainSync(()->{if(main!=null){main.getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);if(main.routes!=null)main.routes.close();}HeadphoneService.start(getTargetContext());});
            for(int i=0;i<50&&HeadphoneService.instance==null;i++)Thread.sleep(100);HeadphoneService s=HeadphoneService.instance;check(s!=null,"Foreground headphone service");
            runOnMainSync(()->s.routes.close());
            java.util.concurrent.FutureTask<Void> sequence=new java.util.concurrent.FutureTask<>(()->{
                s.route("test-bt-a","模拟蓝牙A",android.media.AudioDeviceInfo.TYPE_BLUETOOTH_A2DP);check(s.backend!=null&&s.backend.controlled(),"System effects unavailable: "+HeadphoneService.display);
                Preset a=Preset.defaults();a.name="Test headphone A";a.preamp=-4;a.bands.clear();Preset.Band boost=new Preset.Band();boost.hz=1000;boost.gain=3;boost.q=.7;boost.channel="left";a.bands.add(boost);s.preset=a;s.saveCurrent();s.apply();
                s.route("test-wired-b","模拟有线B",android.media.AudioDeviceInfo.TYPE_WIRED_HEADPHONES);
                Preset b=Preset.defaults();b.name="Test headphone B";b.preamp=-7;s.preset=b;s.saveCurrent();s.apply();
                s.route("test-bt-a","模拟蓝牙A",android.media.AudioDeviceInfo.TYPE_BLUETOOTH_A2DP);check(s.preset.preamp==-4,"Device A preset not restored");
                JSONObject first=s.backend.snapshot();check(first.getBoolean("enabled"),"Effect not enabled");
                if(s.backend.mapped()){check(Math.abs(first.getDouble("input_gain_left_db")+4)<.01,"System gain readback");double maximum=0;JSONArray values=first.getJSONArray("bands_readback");for(int n=0;n<values.length();n++){JSONObject row=values.getJSONObject(n);maximum=Math.max(maximum,row.getDouble("left_db"));check(Math.abs(row.getDouble("right_db"))<.001,"System right channel isolation");}check(maximum>2.8&&maximum<=3.01,"Non-flat EQ projection readback");}report.put("system_effect",first);
                s.route("test-speaker","模拟断开",android.media.AudioDeviceInfo.TYPE_BUILTIN_SPEAKER);check(!s.backend.snapshot().getBoolean("enabled")&&HeadphoneService.phase.equals("waiting_headphones"),"Speaker fallback must pause own effect");
                s.route("test-wired-b","模拟有线B",android.media.AudioDeviceInfo.TYPE_WIRED_HEADPHONES);check(s.preset.preamp==-7&&s.backend.snapshot().getBoolean("enabled"),"Reconnect/device B restore");
                return null;
            });s.control.post(sequence);sequence.get(15,TimeUnit.SECONDS);
            if(main!=null)runOnMainSync(()->main.moveTaskToBack(true));Thread.sleep(1300);check(HeadphoneService.running&&HeadphoneService.instance==s,"Background owner lost");
            report.put("simulated_route_profiles","PASS").put("background_service","PASS").put("result","PASS_SYSTEM_EFFECT_AND_SIMULATED_ROUTING");
            result.putString("result",report.toString());
        }finally{
            runOnMainSync(()->HeadphoneService.stop(getTargetContext()));for(int i=0;i<100&&(HeadphoneService.running||HeadphoneService.stopping);i++)Thread.sleep(100);
            check(!HeadphoneService.running&&!HeadphoneService.stopping,"Effect cleanup did not finish");report.put("stopped",true);
            if(main!=null)runOnMainSync(main::finish);restorePreferences(prefs,saved);restorePreferences(routing,savedRouting);
            try(FileOutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"headphone-checks.json"))){out.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        }
    }
    void volumeCurve(Bundle result)throws Exception{
        android.content.Context context=getTargetContext();
        check(!UsbAudioService.processing,"Do not change volume while a USB session is live");
        check(UsbVolume.STEP==1,"Volume step must be 1%");
        JSONObject report=new JSONObject();
        double top=new UsbVolume.State(100,false).gain();
        check(top==1.0,"100% must stay exactly unity, got "+top);
        check(Math.abs(new UsbVolume.State(20,false).gain()-0.2)<1e-15,"Knee must join legacy linear mapping");
        check(new UsbVolume.State(0,false).gain()==0,"0% must be silent");
        check(new UsbVolume.State(100,true).gain()==0,"Mute must be silent");
        JSONArray table=new JSONArray();double previous=-1;
        for(int p=0;p<=100;p++){
            double g=new UsbVolume.State(p,false).gain();
            check(Double.isFinite(g)&&g>=0&&g<=1,"Gain must stay in [0,1] at "+p);
            if(p>0)check(g>=previous,"Gain must be monotonic at "+p);
            previous=g;
            if(p<=3||p==10||p==19||p==20||p==21||p==50||p==100)table.put(new JSONObject().put("percent",p).put("gain",g).put("db",g>0?20*Math.log10(g):-999));
        }
        double p1=new UsbVolume.State(1,false).gain(),p2=new UsbVolume.State(2,false).gain();
        double lowStepDb=20*Math.log10(p2/p1);
        check(Math.abs(lowStepDb-2.0)<0.001,"Low-end 1% step must be 2 dB, got "+lowStepDb);
        check(p2<0.02,"2% must be quieter than the legacy 0.02 linear gain");
        UsbVolume.load(context);int before=UsbVolume.state.percent;boolean wasMuted=UsbVolume.state.muted;
        try{
            UsbVolume.set(context,50,false);UsbVolume.adjust(context,UsbVolume.STEP);
            check(UsbVolume.state.percent==51,"One up press must move exactly 1%");
            UsbVolume.adjust(context,-UsbVolume.STEP);
            check(UsbVolume.state.percent==50,"One down press must move exactly 1%");
            report.put("step_verified",true);
        }finally{UsbVolume.set(context,before,wasMuted);}
        report.put("result","PASS_VOLUME_CURVE").put("step_percent",UsbVolume.STEP).put("low_db_per_step",lowStepDb).put("gain_100",top).put("gain_1pct",p1).put("gain_2pct",p2).put("table",table);
        try(FileOutputStream out=new FileOutputStream(new File(context.getExternalFilesDir("interop"),"volume-curve-checks.json"))){out.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        result.putString("result",report.toString());
    }
    /** Verifies the exclusive-USB system output lock without needing a DAC. */
    void systemVolumeGuard(Bundle result)throws Exception{
        android.content.Context context=getTargetContext();
        check(!UsbAudioService.processing,"Do not change system volume while a USB session is live");
        android.media.AudioManager audio=(android.media.AudioManager)context.getSystemService(android.content.Context.AUDIO_SERVICE);
        int stream=android.media.AudioManager.STREAM_MUSIC;
        int beforeMusic=audio.getStreamVolume(stream);
        boolean beforeMute=audio.isStreamMute(stream);
        int beforeNotif=audio.getStreamVolume(android.media.AudioManager.STREAM_NOTIFICATION);
        int beforeSystem=audio.getStreamVolume(android.media.AudioManager.STREAM_SYSTEM);
        int beforeRing=audio.getStreamVolume(android.media.AudioManager.STREAM_RING);
        int beforeMode=audio.getRingerMode();
        android.content.SharedPreferences guardPrefs=context.getSharedPreferences(SystemVolumeGuard.PREF,0);
        java.util.Map<String,?> savedGuard=new java.util.HashMap<>(guardPrefs.getAll());
        android.content.SharedPreferences volumePrefs=context.getSharedPreferences("usb-volume",0);
        java.util.Map<String,?> savedVolume=new java.util.HashMap<>(volumePrefs.getAll());
        UsbVolume.load(context);UsbVolume.State originalVolume=UsbVolume.state;
        JSONObject report=new JSONObject();
        SystemVolumeGuard guard=null;
        try{
            guardPrefs.edit().putBoolean("enabled",true).commit();
            guard=new SystemVolumeGuard(context);guard.start();
            Thread.sleep(400);
            check(guard.active(),"Guard must be active");
            check(guardPrefs.getBoolean("active",false),"Guard must persist its restore flag");
            int sentinel=guardPrefs.getInt("sentinel",-1);
            int max=audio.getStreamMaxVolume(stream);
            check(sentinel>=1&&sentinel<=max-1,"Sentinel must leave room in both directions: "+sentinel);
            check(audio.isStreamMute(stream),"Media stream must be muted");
            check(audio.isStreamMute(stream),"Media stream must be muted");
            check(audio.getStreamVolume(android.media.AudioManager.STREAM_NOTIFICATION)==beforeNotif,"Notification volume must not be touched");
            check(audio.getStreamVolume(android.media.AudioManager.STREAM_SYSTEM)==beforeSystem,"System volume must not be touched");
            check(audio.getStreamVolume(android.media.AudioManager.STREAM_RING)==beforeRing,"Ring volume must not be touched");
            check(audio.getRingerMode()==beforeMode,"Ringer mode must not change (no forced vibrate)");
            report.put("sentinel",sentinel).put("muted",true).put("ringer_untouched",true);
            UsbAudioService.processing=true;UsbAudioService.dopActive=false;
            UsbVolume.set(context,50,false);
            audio.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_UNMUTE,0);
            audio.setStreamVolume(stream,sentinel+2,0);
            Thread.sleep(900);
            check(audio.isStreamMute(stream),"Raise must be re-muted");
            check(UsbVolume.state.percent==51,"Raise must become exactly one USB step");
            report.put("raise_reverted",true);
            audio.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_UNMUTE,0);
            audio.setStreamVolume(stream,sentinel-1,0);
            Thread.sleep(900);
            check(audio.isStreamMute(stream),"Lower must be re-muted");
            check(UsbVolume.state.percent==50,"Lower must become exactly one USB step down");
            report.put("lower_reverted",true);
            audio.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_UNMUTE,0);
            Thread.sleep(900);
            check(audio.isStreamMute(stream),"Unmute-only must be re-muted");
            check(UsbVolume.state.percent==51,"Unmute-only press must count as one step up");
            report.put("unmute_only_mapped",true);
            guard.close();guard=null;
            check(!audio.isStreamMute(stream),"Exit must clear the mute");
            check(audio.getStreamVolume(stream)==beforeMusic,"Exit must restore the media volume");
            check(audio.getRingerMode()==beforeMode,"Exit must not change ringer mode");
            guardPrefs.edit().putBoolean("enabled",false).commit();
            SystemVolumeGuard off=new SystemVolumeGuard(context);off.start();
            check(!off.active(),"Guard must respect the opt-out switch");
            off.close();
            guardPrefs.edit().putBoolean("enabled",true).commit();
            report.put("restored",true).put("opt_out_honoured",true).put("result","PASS_SYSTEM_VOLUME_GUARD");
        }finally{
            if(guard!=null)try{guard.close();}catch(Exception ignored){}
            UsbAudioService.processing=false;UsbAudioService.dopActive=false;
            UsbVolume.state=originalVolume;
            restorePreferences(volumePrefs,savedVolume);restorePreferences(guardPrefs,savedGuard);
            try{
                audio.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_UNMUTE,0);
                audio.setStreamVolume(stream,beforeMusic,0);
                if(beforeMute)audio.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_MUTE,0);
                audio.setStreamVolume(android.media.AudioManager.STREAM_NOTIFICATION,beforeNotif,0);
                audio.setStreamVolume(android.media.AudioManager.STREAM_SYSTEM,beforeSystem,0);
                audio.setStreamVolume(android.media.AudioManager.STREAM_RING,beforeRing,0);
            }catch(Exception ignored){}
        }
        try(FileOutputStream out=new FileOutputStream(new File(context.getExternalFilesDir("interop"),"system-volume-guard-checks.json"))){out.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        result.putString("result",report.toString());
    }
    void volumeRouting(Bundle result)throws Exception{
        android.content.Context context=getTargetContext();
        final java.util.concurrent.atomic.AtomicInteger ups=new java.util.concurrent.atomic.AtomicInteger();
        final java.util.concurrent.atomic.AtomicInteger downs=new java.util.concurrent.atomic.AtomicInteger();
        final java.util.concurrent.atomic.AtomicInteger sets=new java.util.concurrent.atomic.AtomicInteger();
        android.media.AudioManager audio=(android.media.AudioManager)context.getSystemService(android.content.Context.AUDIO_SERVICE);
        int stream=android.media.AudioManager.STREAM_MUSIC;
        int before=audio.getStreamVolume(stream);
        android.media.session.MediaSession session=new android.media.session.MediaSession(context,"opendsp-route-test");
        android.media.VolumeProvider provider=new android.media.VolumeProvider(android.media.VolumeProvider.VOLUME_CONTROL_ABSOLUTE,100,50){
            public void onAdjustVolume(int direction){if(direction>0)ups.incrementAndGet();else if(direction<0)downs.incrementAndGet();}
            public void onSetVolumeTo(int value){sets.incrementAndGet();}
        };
        session.setPlaybackToRemote(provider);
        session.setPlaybackState(new android.media.session.PlaybackState.Builder().setActions(android.media.session.PlaybackState.ACTION_PLAY_PAUSE).setState(android.media.session.PlaybackState.STATE_PLAYING,0L,1f).build());
        session.setActive(true);
        Thread.sleep(400);
        int volumeBefore=aio(context,stream);
        Thread.sleep(9000);   /* host injects VOLUME_UP then VOLUME_DOWN meanwhile */
        int volumeAfterUp=aio(context,stream);
        int volumeAfterDown=aio(context,stream);
        session.setActive(false);session.release();
        audio.setStreamVolume(stream,before,0);
        JSONObject report=new JSONObject();
        report.put("session_ups",ups.get()).put("session_downs",downs.get()).put("session_sets",sets.get());
        report.put("system_volume_before",volumeBefore).put("system_volume_after_up",volumeAfterUp).put("system_volume_after_down",volumeAfterDown);
        report.put("result",ups.get()+downs.get()>0?"SESSION_GOT_KEYS":"SESSION_BYPASSED");
        result.putString("result",report.toString());
    }
    private static int musicIndex(android.content.Context context){
        android.media.AudioManager a=(android.media.AudioManager)context.getSystemService(android.content.Context.AUDIO_SERVICE);
        int stream=android.media.AudioManager.STREAM_MUSIC;boolean m=false;
        try{m=a.isStreamMute(stream);}catch(Exception ignored){}
        if(m)try{a.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_UNMUTE,0);Thread.sleep(400);}catch(Exception ignored){}
        int v=a.getStreamVolume(stream);
        if(m)try{a.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_MUTE,0);}catch(Exception ignored){}
        return v;
    }
    private static int aio(android.content.Context context,int stream){android.media.AudioManager a=(android.media.AudioManager)context.getSystemService(android.content.Context.AUDIO_SERVICE);return a.getStreamVolume(stream);}
    private void injectKey(int keyCode){
        long now=android.os.SystemClock.uptimeMillis();
        android.app.UiAutomation ui=getUiAutomation();
        ui.injectInputEvent(new android.view.KeyEvent(now,now,android.view.KeyEvent.ACTION_DOWN,keyCode,0),true);
        ui.injectInputEvent(new android.view.KeyEvent(now,now+30,android.view.KeyEvent.ACTION_UP,keyCode,0),true);
    }
    void muteProbe(Bundle result)throws Exception{
        android.content.Context c=getTargetContext();
        android.media.AudioManager a=(android.media.AudioManager)c.getSystemService(android.content.Context.AUDIO_SERVICE);
        int stream=android.media.AudioManager.STREAM_MUSIC;
        int before=a.getStreamVolume(stream);boolean beforeMute=a.isStreamMute(stream);
        JSONObject r=new JSONObject();
        a.setStreamVolume(stream,7,0);Thread.sleep(200);
        r.put("set7_index",a.getStreamVolume(stream));
        a.setStreamVolume(stream,1,0);Thread.sleep(400);
        r.put("set1_index",a.getStreamVolume(stream));
        a.setStreamVolume(stream,7,0);Thread.sleep(200);
        a.setStreamMute(stream,true);Thread.sleep(400);
        r.put("setMute_index",a.getStreamVolume(stream)).put("setMute_muted",a.isStreamMute(stream));
        a.setStreamMute(stream,false);Thread.sleep(400);
        r.put("unsetMute_index",a.getStreamVolume(stream)).put("unsetMute_muted",a.isStreamMute(stream));
        a.setStreamVolume(stream,7,0);
        a.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_MUTE,0);Thread.sleep(400);
        r.put("adjustMute_index",a.getStreamVolume(stream)).put("adjustMute_muted",a.isStreamMute(stream));
        a.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_UNMUTE,0);Thread.sleep(400);
        r.put("adjustUnmute_index",a.getStreamVolume(stream)).put("adjustUnmute_muted",a.isStreamMute(stream));
        a.setStreamVolume(stream,before,0);if(beforeMute)a.adjustStreamVolume(stream,android.media.AudioManager.ADJUST_MUTE,0);
        r.put("result","MUTE_PROBE");
        result.putString("result",r.toString());
    }
    void restorePreferences(android.content.SharedPreferences preferences,java.util.Map<String,?> saved){
        android.content.SharedPreferences.Editor edit=preferences.edit().clear();for(java.util.Map.Entry<String,?> entry:saved.entrySet()){Object v=entry.getValue();String k=entry.getKey();if(v instanceof String)edit.putString(k,(String)v);else if(v instanceof Boolean)edit.putBoolean(k,(Boolean)v);else if(v instanceof Integer)edit.putInt(k,(Integer)v);else if(v instanceof Long)edit.putLong(k,(Long)v);else if(v instanceof Float)edit.putFloat(k,(Float)v);}edit.commit();
    }
    @Override public void onStart(){Bundle result=new Bundle();try{
        if(arguments!=null&&arguments.getString("volume_curve")!=null){volumeCurve(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("system_volume_guard")!=null){systemVolumeGuard(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("volume_routing")!=null){volumeRouting(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("mute_probe")!=null){muteProbe(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("import_pc_library")!=null){
            android.content.Context context=getTargetContext();android.content.SharedPreferences prefs=PresetLibrary.prefs(context);
            String current=prefs.getString("current",null);java.util.Map<String,?> original=new java.util.HashMap<>(prefs.getAll());
            JSONObject pack;try(InputStream input=new FileInputStream(new File(context.getExternalFilesDir("interop"),"pc-preset-library.json"))){pack=new JSONObject(MainActivity.readText(input,2097152));}
            JSONObject report=PresetLibrary.merge(context,pack);check(report.getBoolean("current_unchanged")&&java.util.Objects.equals(current,prefs.getString("current",null)),"Library import must not apply a preset");
            JSONArray names=report.getJSONArray("entries"),incoming=pack.getJSONArray("presets");
            java.util.TreeMap<String,Preset> visible=PresetLibrary.all(context);
            for(int i=0;i<incoming.length();i++){Preset expected=Preset.parse(incoming.getJSONObject(i).toString());String name=names.getJSONObject(i).getString("phone_name");expected.name=name;check(visible.containsKey(name),"Imported preset visible "+name);check(PresetSync.hash(expected).equals(PresetSync.hash(visible.get(name))),"Exact PC parameters "+name);}
            for(java.util.Map.Entry<String,?> entry:original.entrySet())if(entry.getKey().startsWith("saved."))check(entry.getValue().equals(prefs.getString(entry.getKey(),null)),"Existing phone preset overwritten");
            JSONObject repeated=PresetLibrary.merge(context,pack);check(repeated.getInt("added")==0,"Repeated library import duplicates presets");
            report.put("result","PASS_PC_LIBRARY_IMPORTED").put("pc_presets",incoming.length()).put("visible_presets",visible.size()).put("exact_parameters_verified",true).put("idempotent",true);
            try(OutputStream output=new FileOutputStream(new File(context.getExternalFilesDir("interop"),"pc-library-import-report.json"))){output.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
            result.putString("result",report.toString());finish(Activity.RESULT_OK,result);return;
        }
        if(arguments!=null&&arguments.getString("mobile_features")!=null){mobileFeatures(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("preset_roundtrip")!=null){
            check(!UsbAudioService.busy(),"Do not interrupt playback for preset fixture test");
            File directory=getTargetContext().getExternalFilesDir("interop");
            for(String name:new String[]{"desktop-complete","desktop-apo"}){
                Preset preset;try(InputStream in=new FileInputStream(new File(directory,name+".json"))){preset=PresetImport.parse(MainActivity.readText(in,262144));}
                try(OutputStream out=new FileOutputStream(new File(directory,name+"-android-returned.json"))){out.write(preset.encode().getBytes(java.nio.charset.StandardCharsets.UTF_8));}
            }
            result.putString("result","PASS: real Android JSON parser imported and exported PC fixtures without applying settings");finish(Activity.RESULT_OK,result);return;
        }
        if(arguments!=null&&arguments.getString("native_src")!=null){nativeSrc(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("read_last_report")!=null){
            check(!UsbAudioService.busy(),"Do not inspect persisted report during a live service");
            File from=new File(getTargetContext().getFilesDir(),"usb-last-report.json"),to=new File(getTargetContext().getExternalFilesDir("interop"),"last-usb-report-export.json");
            try(InputStream in=new FileInputStream(from);OutputStream out=new FileOutputStream(to)){byte[] buffer=new byte[8192];int count;while((count=in.read(buffer))>0)out.write(buffer,0,count);}
            result.putString("result","Exported prior USB stop report only; no capture or playback");finish(Activity.RESULT_OK,result);return;
        }
        if(arguments!=null&&arguments.getString("headphones")!=null){headphones(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("workbench_layout")!=null){workbenchLayout(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("usb_service_silence")!=null){usbServiceSilence(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("usb_prepare")!=null){usbPrepare(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("no_audio")!=null){noAudio(result);finish(Activity.RESULT_OK,result);return;}
        if(arguments!=null&&arguments.getString("validate_desktop")!=null){File directory=getTargetContext().getExternalFilesDir("interop");Preset desktop;try(InputStream in=new FileInputStream(new File(directory,"desktop.json"))){desktop=Preset.parse(MainActivity.readText(in,262144));}
            check(desktop.bands.size()==1,"desktop band count");check(desktop.bands.get(0).channel.equals("left"),"desktop left channel");check(desktop.bands.get(0).gain==-12,"desktop gain");check(desktop.output==-24,"desktop output gain");
            try(OutputStream out=new FileOutputStream(new File(directory,"android-roundtrip.json"))){out.write(desktop.encode().getBytes(java.nio.charset.StandardCharsets.UTF_8));}result.putString("result","PASS: imported desktop preset on Android and exported all processing parameters");finish(Activity.RESULT_OK,result);return;
        }
        if(arguments!=null&&arguments.getString("root_service")!=null){
            Activity activity=startActivitySync(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));waitForIdleSync();final Preset[] backup={null};JSONObject report=new JSONObject();
            try{runOnMainSync(()->{MainActivity main=(MainActivity)activity;backup[0]=main.preset.copy();Preset p=Preset.defaults();p.name="Root service real-path check";p.bands.clear();Preset.Band b=new Preset.Band();b.hz=1000;b.gain=-12;b.q=2;b.channel="left";p.bands.add(b);main.preset=p;main.changed();RootEqService.start(main);});
                for(int n=0;n<200&&!RootEqService.processing;n++){Thread.sleep(100);if(n>20&&RootEqService.instance==null)break;}check(RootEqService.processing,"Root service not ready: "+RootEqService.display);runOnMainSync(()->activity.moveTaskToBack(true));
                int rate=48000;android.media.AudioTrack t=new android.media.AudioTrack.Builder().setAudioAttributes(new android.media.AudioAttributes.Builder().setUsage(android.media.AudioAttributes.USAGE_MEDIA).setAllowedCapturePolicy(android.media.AudioAttributes.ALLOW_CAPTURE_BY_ALL).build()).setAudioFormat(new android.media.AudioFormat.Builder().setSampleRate(rate).setChannelMask(android.media.AudioFormat.CHANNEL_OUT_STEREO).setEncoding(android.media.AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(19200).build();
                try{short[] data=new short[9600];for(int n=0;n<data.length/2;n++){short v=(short)(64*Math.sin(2*Math.PI*1000*n/rate));data[n*2]=v;data[n*2+1]=v;}t.play();for(int n=0;n<30;n++)t.write(data,0,data.length,android.media.AudioTrack.WRITE_BLOCKING);}finally{t.stop();t.release();}Thread.sleep(700);
                File status=new File(RootEqService.instance.directory,"status.json");JSONObject state=new JSONObject(RootDspProcess.read(status));double left=10*Math.log10(state.getDouble("output_energy_left")/state.getDouble("input_energy_left")),right=10*Math.log10(state.getDouble("output_energy_right")/state.getDouble("input_energy_right"));check(Math.abs(left+12)<.15&&Math.abs(right)<.02,"Root live PEQ transfer L="+left+" R="+right);check(state.getLong("rendered_frames")>1000,"Root output clock did not advance");check(state.getInt("underruns")==0,"Root audio underrun");report.put("result","PASS").put("left_db",left).put("right_db",right).put("state",state).put("background_service","PASS");
                runOnMainSync(()->{MainActivity main=(MainActivity)activity;main.preset.bands.get(0).gain=-6.1;main.changed();});Thread.sleep(900);
                JSONObject before=new JSONObject(RootDspProcess.read(status));rootTone(30);Thread.sleep(700);JSONObject after=new JSONObject(RootDspProcess.read(status));double switched=10*Math.log10((after.getDouble("output_energy_left")-before.getDouble("output_energy_left"))/(after.getDouble("input_energy_left")-before.getDouble("input_energy_left")));check(Math.abs(switched+6.1)<.15,"Live 0.1 dB preset update: "+switched);report.put("updated_left_db",switched);
                final boolean[] blocked={false};runOnMainSync(()->{RootEqService.stop(getTargetContext());try{RootEqService.start(getTargetContext());}catch(IllegalStateException expected){blocked[0]=true;}});check(blocked[0],"Restart allowed while previous route is stopping");
                for(int n=0;n<100&&RootEqService.stopping;n++)Thread.sleep(100);check(!RootEqService.stopping,"Root stop timed out");check(new JSONObject(RootDspProcess.read(status)).optBoolean("policy_unregistered"),"First route leaked");report.put("rapid_restart_guard","PASS");
                // Starting an FGS requires a foreground activity; return through the real Activity lifecycle.
                getTargetContext().startActivity(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_REORDER_TO_FRONT));Thread.sleep(700);
                runOnMainSync(()->RootEqService.start(getTargetContext()));for(int n=0;n<150&&!RootEqService.processing;n++)Thread.sleep(100);check(RootEqService.processing,"Second Root session failed: "+RootEqService.display);rootTone(15);check(RootEqService.instance!=null,"Restarted Root service lost");report.put("restart_after_cleanup","PASS");

            }finally{File status=RootEqService.instance==null?null:new File(RootEqService.instance.directory,"status.json");runOnMainSync(()->{RootEqService.stop(getTargetContext());if(backup[0]!=null){MainActivity main=(MainActivity)activity;main.preset=backup[0];main.changed();}});for(int n=0;n<30&&status!=null;n++){Thread.sleep(100);if(status.exists()){JSONObject end=new JSONObject(RootDspProcess.read(status));if(end.optBoolean("policy_unregistered")){report.put("policy_unregistered",true);break;}}}}
            check(report.optBoolean("policy_unregistered"),"Final Root policy cleanup not confirmed");
            try(OutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"root-service-test.json"))){out.write(report.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}result.putString("result",report.toString());finish(Activity.RESULT_OK,result);return;
        }
        if(arguments!=null&&arguments.getString("external_tone")!=null){
            int rate=48000;android.media.AudioTrack t=new android.media.AudioTrack.Builder().setAudioAttributes(new android.media.AudioAttributes.Builder().setUsage(android.media.AudioAttributes.USAGE_MEDIA).setAllowedCapturePolicy(android.media.AudioAttributes.ALLOW_CAPTURE_BY_ALL).build()).setAudioFormat(new android.media.AudioFormat.Builder().setSampleRate(rate).setChannelMask(android.media.AudioFormat.CHANNEL_OUT_STEREO).setEncoding(android.media.AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(192000).build();
            short[] data=new short[9600];for(int n=0;n<data.length/2;n++){short value=(short)(64*Math.sin(2*Math.PI*1000*n/rate));data[2*n]=value;data[2*n+1]=value;}t.play();for(int n=0;n<60;n++)t.write(data,0,data.length,android.media.AudioTrack.WRITE_BLOCKING);t.stop();t.release();result.putString("result","External UID test tone completed");finish(Activity.RESULT_OK,result);return;
        }
        check(UsbNative.version().equals("1.0.30"),"Native USB library version");try{UsbNative.probe(-1);throw new AssertionError("Invalid USB descriptor accepted");}catch(IOException expected){}
        Preset p=Preset.defaults();p.name="幻音 4X · 中文往返";p.bands.get(0).channel="left";p.bands.get(0).gain=4.5;
        Preset loaded=Preset.parse(p.encode());check(loaded.name.equals(p.name),"UTF-8 name");check(loaded.bands.get(0).gain==4.5,"gain round-trip");check(loaded.bands.get(0).channel.equals("left"),"channel round-trip");
        check(Math.abs(loaded.response(60,"left")-loaded.response(60,"right")-4.5)<0.000001,"independent channels");
        reject(p.json().put("version",2));reject(p.json().put("preamp_db",1));
        JSONObject badOutput=p.json();badOutput.getJSONObject("output").put("left_delay_ms",101);reject(badOutput);
        JSONObject invalid=p.json();invalid.getJSONArray("filters").getJSONObject(0).put("type","unknown");reject(invalid);
        invalid=p.json();invalid.getJSONArray("filters").getJSONObject(0).put("channel","rear");reject(invalid);
        invalid=p.json();for(int n=0;n<33;n++)invalid.getJSONArray("filters").put(p.json().getJSONArray("filters").get(0));reject(invalid);
        byte[] wav=wav();Player.Wav decoded=new Player.Wav(new ByteArrayInputStream(wav));check(decoded.rate==48000&&decoded.bits==16&&decoded.channels==2,"WAV header");check(decoded.remaining==48000*4,"WAV data size");
        byte[] bad=wav.clone();bad[20]=3;try{new Player.Wav(new ByteArrayInputStream(bad));throw new AssertionError("Invalid WAV accepted");}catch(IOException expected){}
        Player engine=new Player();CountDownLatch ended=new CountDownLatch(1);StringBuilder messages=new StringBuilder();
        engine.play(new ByteArrayInputStream(wav),loaded,msg->{messages.append(msg).append('\n');if(msg.startsWith("试听结束")||msg.startsWith("试听失败"))ended.countDown();});
        check(ended.await(8,TimeUnit.SECONDS),"Playback timeout");check(messages.toString().contains("试听结束"),messages.toString());check(!messages.toString().contains("失败"),messages.toString());
        // Allow the player's finally block to release the device before the next test.
        Thread.sleep(100);
        Activity a=startActivitySync(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));check(a!=null,"Activity launch");waitForIdleSync();
        runOnMainSync(()->{MainActivity main=(MainActivity)a;android.widget.EditText input=new android.widget.EditText(main);input.setText("0");for(int n=0;n<100;n++)main.step(input,1,-24,24);check(input.getText().toString().equals("10"),"0.1 decimal stepping without drift");input.setText("24");main.step(input,1,-24,24);check(input.getText().toString().equals("24"),"Upper clamp");main.page="均衡";main.render();});
        String keyA=DeviceRoutes.key(android.media.AudioDeviceInfo.TYPE_BLUETOOTH_A2DP,"Headset","AA:BB:CC:DD:EE:01"),keyB=DeviceRoutes.key(android.media.AudioDeviceInfo.TYPE_BLUETOOTH_A2DP,"Headset","AA:BB:CC:DD:EE:02");check(!keyA.equals(keyB),"Different Bluetooth identities");check(keyA.equals(DeviceRoutes.key(android.media.AudioDeviceInfo.TYPE_BLUETOOTH_A2DP,"Headset","AA:BB:CC:DD:EE:01")),"Stable device identity");
        String[] factories=getTargetContext().getAssets().list("presets");check(factories.length>=20,"Factory and Luna presets");for(String file:factories)try(InputStream in=getTargetContext().getAssets().open("presets/"+file)){Preset.parse(MainActivity.readText(in,262144));}
        Preset legacy=Preset.defaults();legacy.preamp=-6;legacy.output=-12;check(legacy.upgradeLegacyStarter()&&legacy.preamp==0&&legacy.output==0,"Legacy volume migration");Preset custom=Preset.defaults();custom.preamp=-6;custom.output=-12;custom.bands.get(0).gain=.1;check(!custom.upgradeLegacyStarter()&&custom.output==-12,"Custom EQ preservation");
        JSONObject routeReport=new JSONObject();android.media.AudioManager am=(android.media.AudioManager)getTargetContext().getSystemService(android.content.Context.AUDIO_SERVICE);JSONArray outputs=new JSONArray();for(android.media.AudioDeviceInfo d:am.getDevices(android.media.AudioManager.GET_DEVICES_OUTPUTS)){JSONArray rates=new JSONArray();for(int rate:d.getSampleRates())rates.put(rate);JSONArray encodings=new JSONArray();for(int encoding:d.getEncodings())encodings.put(encoding);outputs.put(new JSONObject().put("name",d.getProductName()).put("type",d.getType()).put("sample_rates",rates).put("encodings",encodings));}routeReport.put("outputs",outputs).put("system_output_rate",am.getProperty(android.media.AudioManager.PROPERTY_OUTPUT_SAMPLE_RATE)).put("legacy_volume_migration","PASS");try(OutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"device-capabilities.json"))){out.write(routeReport.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        JSONObject nativeReport=new JSONObject();final android.media.audiofx.Equalizer[] nativeEffect={null};final Throwable[] nativeFailure={null};final short[][] originalLevels={null};final boolean[] originalEnabled={false};
        runOnMainSync(()->{try{android.media.audiofx.Equalizer e=NativeEqService.open(getTargetContext());nativeEffect[0]=e;originalEnabled[0]=e.getEnabled();originalLevels[0]=new short[e.getNumberOfBands()];for(short i=0;i<e.getNumberOfBands();i++)originalLevels[0][i]=e.getBandLevel(i);}catch(Throwable e){nativeFailure[0]=e;}});
        try{if(nativeEffect[0]!=null){for(int n=0;n<20&&!NativeEqService.running;n++)Thread.sleep(100);check(NativeEqService.running,"Native EQ foreground service not running");android.media.audiofx.Equalizer e=nativeEffect[0];nativeReport.put("bands",e.getNumberOfBands()).put("has_control",e.hasControl()).put("foreground_service",true);if(e.hasControl()){short[] range=e.getBandLevelRange();Thread.sleep(500);short next=(short)(e.getBandLevel((short)0)+10<=range[1]?originalLevels[0][0]+10:originalLevels[0][0]-10);e.setBandLevel((short)0,next);nativeReport.put("requested_level_mb",next).put("actual_level_mb",e.getBandLevel((short)0));nativeReport.put("exact_0_1_db_supported",e.getBandLevel((short)0)==next);short whole=(short)(originalLevels[0][0]+100<=range[1]?originalLevels[0][0]+100:originalLevels[0][0]-100);e.setBandLevel((short)0,whole);check(Math.abs(e.getBandLevel((short)0)-whole)<=50,"Native EQ did not accept a 1 dB change");check(e.setEnabled(true)==0,"Enable native EQ");runOnMainSync(()->a.moveTaskToBack(true));Thread.sleep(400);check(NativeEqService.running&&e.getEnabled(),"EQ lost when Activity went to background");nativeReport.put("background_test","PASS").put("requested_step_db",0.1);}}else nativeReport.put("unavailable",nativeFailure[0]==null?"unknown":nativeFailure[0].toString());}
        finally{runOnMainSync(()->{try{if(nativeEffect[0]!=null){for(short i=0;i<originalLevels[0].length;i++)nativeEffect[0].setBandLevel(i,originalLevels[0][i]);nativeEffect[0].setEnabled(originalEnabled[0]);}}catch(Exception ignored){}getTargetContext().stopService(new Intent(getTargetContext(),NativeEqService.class));});}
        try(OutputStream out=new FileOutputStream(new File(getTargetContext().getExternalFilesDir("interop"),"native-eq-capability.json"))){out.write(nativeReport.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        getTargetContext().startActivity(new Intent(getTargetContext(),MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        result.putString("result","PASS: preset UTF-8 round-trip, independent channels, invalid config rejection, WAV validation, real AudioTrack playback, Activity launch, decimal stepping, stable/distinct Bluetooth profile keys, 4 shared factory presets\n"+messages);
        result.putString("native_eq",nativeReport.toString());
        finish(Activity.RESULT_OK,result);
    }catch(Throwable e){result.putString("failure",e.toString());finish(Activity.RESULT_CANCELED,result);}}
    static void rootTone(int blocks){int rate=48000;android.media.AudioTrack t=new android.media.AudioTrack.Builder().setAudioAttributes(new android.media.AudioAttributes.Builder().setUsage(android.media.AudioAttributes.USAGE_MEDIA).setAllowedCapturePolicy(android.media.AudioAttributes.ALLOW_CAPTURE_BY_ALL).build()).setAudioFormat(new android.media.AudioFormat.Builder().setSampleRate(rate).setChannelMask(android.media.AudioFormat.CHANNEL_OUT_STEREO).setEncoding(android.media.AudioFormat.ENCODING_PCM_16BIT).build()).setBufferSizeInBytes(19200).build();try{short[] data=new short[9600];for(int n=0;n<data.length/2;n++){short v=(short)(64*Math.sin(2*Math.PI*1000*n/rate));data[n*2]=v;data[n*2+1]=v;}t.play();for(int n=0;n<blocks;n++){int written=t.write(data,0,data.length,android.media.AudioTrack.WRITE_BLOCKING);if(written!=data.length)throw new IllegalStateException("Test tone write failed: "+written);}}finally{t.stop();t.release();}}
    static byte[] wav()throws IOException{
        ByteArrayOutputStream out=new ByteArrayOutputStream();DataOutputStream d=new DataOutputStream(out);d.writeBytes("RIFF");le(d,36+48000*4,4);d.writeBytes("WAVEfmt ");le(d,16,4);le(d,1,2);le(d,2,2);le(d,48000,4);le(d,192000,4);le(d,4,2);le(d,16,2);d.writeBytes("data");le(d,48000*4,4);
        for(int n=0;n<48000;n++){int v=(int)(16*Math.sin(2*Math.PI*1000*n/48000));le(d,v,2);le(d,v,2);}return out.toByteArray();
    }
    static void le(DataOutputStream d,int v,int count)throws IOException{for(int n=0;n<count;n++)d.writeByte(v>>>(n*8));}
}
