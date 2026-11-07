package org.openeq.app;
import android.content.*;
import android.hardware.usb.*;
import android.graphics.Color;
import android.view.View;
import android.widget.*;
import org.json.*;
import java.util.*;

/** Capture-first routing console. Local file playback is deliberately a secondary tool. */
final class AudioWorkbench {
 final MainActivity a;final UsbPanel usb;final SystemRoutePanel system;final ArrayList<View> selectors=new ArrayList<>();String mode;Spinner outputMode;
 TextView state,detail,inputLevel,outputLevel,inputFormat,outputFormat,counters,compatibility,deviceLabel,header,volumeLabel,backgroundLabel,guardLabel;
 SeekBar volumeSlider;Button mute;CheckBox guardSwitch;
 ProgressBar inputMeter,outputMeter;Button start,stop,openSource;Spinner sourcePick;
 String lastDiagnostics,lastSource;JSONObject parsed=new JSONObject();boolean wasBusy,rendering;long nextDevices;boolean sourceAvailable;
 AudioWorkbench(MainActivity a,UsbPanel usb){this.a=a;this.usb=usb;system=new SystemRoutePanel(a,this);mode=a.getPreferences(0).getString("output.mode","system");}
 void resetViews(){selectors.clear();system.clear();state=detail=inputLevel=outputLevel=inputFormat=outputFormat=counters=compatibility=deviceLabel=header=volumeLabel=backgroundLabel=guardLabel=null;volumeSlider=null;mute=null;guardSwitch=null;inputMeter=outputMeter=null;start=stop=openSource=null;sourcePick=null;lastDiagnostics=lastSource=null;nextDevices=0;outputMode=null;}
 void header(LinearLayout parent){
  header=a.text("耳机自动切换 · 后台处理 · USB可选",12,a.MUTED);parent.addView(header);
 }
 static String title(String phase){
  switch(phase){case "preparing":return "正在连接输出";case "buffering":return "USB已连接 · 正在缓冲";case "waiting":return "等待音源 · 暂未捕获到声音";case "processing":return "正在处理音频";case "stopping":return "正在停止并释放USB";case "error":return "链路未启动或已中断";default:return "待机";}
 }
 void page(){
  rendering=true;
  LinearLayout output=a.card();outputMode=usb.spinner(output,"输出与处理路径",new String[]{"自动跟随耳机 · 系统EQ","USB独占 · 完整DSP"});
  outputMode.setSelection(mode.equals("usb")?1:0);
  outputMode.setEnabled(!usb.activeOrPending()&&!HeadphoneService.running&&!HeadphoneService.stopping);
  outputMode.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onNothingSelected(android.widget.AdapterView<?> p){}public void onItemSelected(android.widget.AdapterView<?> p,View v,int n,long id){String next=n==1?"usb":"system";if(!next.equals(mode)){mode=next;a.getPreferences(0).edit().putString("output.mode",mode).apply();a.render();}}});
  if(mode.equals("system")){system.page();rendering=false;refresh();return;}
  LinearLayout summary=a.card();state=a.text("待机",23,a.INK);summary.addView(state);detail=a.text("选择音源和DAC，开始处理后切到音源应用播放。",12,a.MUTED);summary.addView(detail);
  LinearLayout buttons=new LinearLayout(a);start=a.button("开始处理",()->usb.beginCapture());stop=a.button("停止处理",()->{usb.cancelPending();UsbAudioService.stop(a);refresh();});
  start.setTextColor(Color.WHITE);start.setBackground(a.shape(a.ACCENT));
  LinearLayout.LayoutParams left=new LinearLayout.LayoutParams(0,-2,1),right=new LinearLayout.LayoutParams(0,-2,1);left.setMargins(0,0,a.dp(8),0);buttons.addView(start,left);buttons.addView(stop,right);summary.addView(buttons);
  volumeLabel=a.text("USB主音量 · "+UsbVolume.label(),16,a.INK);summary.addView(volumeLabel);
  volumeSlider=new SeekBar(a);volumeSlider.setMax(100);volumeSlider.setProgress(UsbVolume.state.percent);summary.addView(volumeSlider);
  volumeSlider.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}public void onProgressChanged(SeekBar b,int value,boolean user){if(user){UsbVolume.set(a,value,false);refresh();}}});
  mute=a.button("静音",()->{UsbVolume.toggle(a);refresh();});summary.addView(mute);
  guardLabel=a.text("系统输出：待机",12,a.MUTED);summary.addView(guardLabel);
  guardSwitch=new CheckBox(a);guardSwitch.setText("USB 独占时屏蔽系统输出（防止扬声器同时出声）");guardSwitch.setTextSize(12);guardSwitch.setTextColor(a.MUTED);
  guardSwitch.setChecked(a.getSharedPreferences(SystemVolumeGuard.PREF,0).getBoolean("enabled",true));
  guardSwitch.setEnabled(!usb.activeOrPending());
  guardSwitch.setOnCheckedChangeListener((btn,checked)->a.getSharedPreferences(SystemVolumeGuard.PREF,0).edit().putBoolean("enabled",checked).apply());
  summary.addView(guardSwitch);
  summary.addView(a.text("独立于EQ前级，PCM音量20ms平滑变化。音量键与通知按钮每档1%；20%以下每档约2dB，低阻高敏耳机可从1%起步，100%保持原最大输出。USB独占期间系统媒体流自动静音并原样恢复，不动铃声与通知、不会切震动模式；前台、通知栏与后台音量键统一调USB主音量，加减都生效。DSD直送请用DAC硬件音量。",11,a.MUTED));
  LinearLayout route=a.card();route.addView(a.text("输入 / 输出",17,a.INK));
  sourcePick=usb.spinner(route,"音源应用",CaptureSources.LABELS);sourcePick.setSelection(CaptureSources.index(usb.sourcePackage));selectors.add(sourcePick);
  sourcePick.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onNothingSelected(android.widget.AdapterView<?> p){}public void onItemSelected(android.widget.AdapterView<?> p,View v,int position,long id){if(!usb.activeOrPending()){usb.sourcePackage=CaptureSources.PACKAGES[position];usb.saveWorkbench();refresh();}}});
  openSource=a.button("打开音源应用",()->{String source=UsbAudioService.busy()?UsbAudioService.activeSourcePackage:usb.sourcePackage;if(source==null||source.isEmpty()){a.toast("请自行切到允许播放捕获的媒体应用");return;}Intent launch=a.getPackageManager().getLaunchIntentForPackage(source);if(launch==null){a.toast("该应用未安装或没有可打开的入口");return;}a.startActivity(launch);});route.addView(openSource);
  deviceLabel=a.text("USB输出：检测中",12,a.MUTED);route.addView(deviceLabel);Button choose=a.button("选择输出 DAC",()->usb.selectDac());selectors.add(choose);route.addView(choose);
  final int[] rates={44100,48000,88200,96000,176400,192000,352800,384000};String[] labels=new String[rates.length];int selected=7;for(int i=0;i<rates.length;i++){labels[i]=rates[i]+" Hz";if(rates[i]==usb.rate)selected=i;}
  Spinner rate=usb.spinner(route,"USB目标采样率",labels);rate.setSelection(selected);selectors.add(rate);
  rate.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onNothingSelected(android.widget.AdapterView<?> p){}public void onItemSelected(android.widget.AdapterView<?> p,View v,int n,long id){if(!usb.activeOrPending()){usb.rate=rates[n];usb.saveWorkbench();}}});
  final int[] bits={16,24,32};Spinner width=usb.spinner(route,"USB PCM位数",new String[]{"16-bit","24-bit","32-bit"});width.setSelection(usb.bits==16?0:usb.bits==24?1:2);selectors.add(width);
  width.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){public void onNothingSelected(android.widget.AdapterView<?> p){}public void onItemSelected(android.widget.AdapterView<?> p,View v,int n,long id){if(!usb.activeOrPending()){usb.bits=bits[n];usb.saveWorkbench();}}});
  compatibility=a.text("",12,a.MUTED);route.addView(compatibility);
  route.addView(a.text("音源 → 系统允许的播放捕获 → 主EQ / 压缩 / 矩阵 / 声场 → USB直连\n输入和输出格式分别显示，384k输出不代表来源是384k。",12,a.MUTED));
  LinearLayout levels=a.card();levels.addView(a.text("实时信号与链路",17,a.INK));inputFormat=a.text("输入：尚未启动",12,a.MUTED);levels.addView(inputFormat);inputLevel=a.text("输入：—",14,a.INK);levels.addView(inputLevel);
  inputMeter=new ProgressBar(a,null,android.R.attr.progressBarStyleHorizontal);inputMeter.setMax(600);levels.addView(inputMeter);
  outputFormat=a.text("USB输出：尚未协商",12,a.MUTED);levels.addView(outputFormat);outputLevel=a.text("DSP输出：—",14,a.INK);levels.addView(outputLevel);
  outputMeter=new ProgressBar(a,null,android.R.attr.progressBarStyleHorizontal);outputMeter.setMax(600);levels.addView(outputMeter);counters=a.text("",12,a.MUTED);levels.addView(counters);
  LinearLayout preset=a.card();preset.addView(a.text("当前调音",17,a.INK));preset.addView(a.text(a.preset.name+" · "+a.preset.bands.size()+"段 · 前级 "+a.preset.preamp+"dB",13,a.MUTED));
  a.rowButtons(preset,"均衡 / 输出处理",()->{a.page="均衡";a.render();},"预设 / A-B",()->{a.page="预设";a.render();});
  LinearLayout notes=a.card();notes.addView(a.text("使用说明",17,a.INK));notes.addView(a.text("1. 选音源和DAC，点“开始处理”，完成系统授权。\n2. 切到网易云或B站播放，回来看输入电平与USB计数。\n3. EQ等参数实时生效，切到后台也可继续处理。\nUSB独占期间仅媒体流被静音（保留非零音量便于音量键加减），退出后按原值恢复并取消静音，避免DAC与扬声器同时出声；铃声、通知与闹钟不受影响。",12,a.MUTED));
  backgroundLabel=a.text(BackgroundStatus.describe(a),12,a.MUTED);notes.addView(backgroundLabel);
  notes.addView(a.button("后台 / 电池优化设置",()->{try{BackgroundStatus.requestBatteryExemption(a);}catch(Exception error){a.error(error);}}));
  notes.addView(a.text("系统授权弹窗可能写“屏幕共享”；OpenDSP只建立音频捕获通道，不创建屏幕图像、不读取播放记录、不保存录音。",11,a.MUTED));
  notes.addView(a.button("诊断与辅助工具",()->{a.page="工具";a.render();}));rendering=false;refresh();
 }
 void refresh(){
  boolean busy=usb.activeOrPending();String phase=UsbAudioService.phase;String display=UsbAudioService.display;
  if(header!=null){String text=HeadphoneService.running?HeadphoneService.display:UsbAudioService.busy()?title(phase):"耳机自动切换 · 后台处理 · USB可选";if(!text.contentEquals(header.getText()))header.setText(text);}
  if(outputMode!=null)outputMode.setEnabled(!busy&&!HeadphoneService.running&&!HeadphoneService.stopping);
  if(mode.equals("system")){system.refresh();return;}
  if(state==null||rendering)return;
  boolean volumeAllowed=!UsbAudioService.processing||!UsbAudioService.dopActive;
  text(volumeLabel,volumeAllowed?"USB主音量 · "+UsbVolume.label():"DSD直送 · 软件音量不生效");
  if(volumeSlider.getProgress()!=UsbVolume.state.percent)volumeSlider.setProgress(UsbVolume.state.percent);
  volumeSlider.setEnabled(volumeAllowed);mute.setEnabled(volumeAllowed);text(mute,UsbVolume.state.muted?"取消静音":"静音");
  String raw=UsbAudioService.diagnostics;if(!raw.equals(lastDiagnostics)){try{parsed=new JSONObject(raw);}catch(Exception e){parsed=new JSONObject();}lastDiagnostics=raw;}JSONObject status=parsed;
  UsbAudioService svc=UsbAudioService.instance;boolean locked=svc!=null&&svc.systemGuard!=null&&svc.systemGuard.active();
  boolean guardEnabled=a.getSharedPreferences(SystemVolumeGuard.PREF,0).getBoolean("enabled",true);
  text(guardLabel,!guardEnabled?"系统输出：屏蔽已关闭（可在下方开关打开）":locked?"系统输出：媒体流已静音 · 不会从扬声器出声 · 退出后自动恢复":UsbAudioService.processing?"系统输出：正在接管…":"系统输出：正常（未处理时不影响手机音量）");
  if(guardSwitch!=null)guardSwitch.setEnabled(!busy);
  if(usb.pending){text(state,"等待系统授权");text(detail,usb.message);}else if(!UsbAudioService.busy()&&!usb.message.isEmpty()){text(state,"待机");text(detail,usb.message);}else{text(state,title(phase));text(detail,display);}
  boolean processing="processing".equals(phase)&&UsbAudioService.busy();state.setTextColor(processing?Color.rgb(22,141,122):"error".equals(phase)?Color.rgb(184,83,62):a.INK);
  start.setEnabled(!busy);start.setText(busy?"处理中 / 等待中":"开始处理");stop.setEnabled(busy);
  if(busy!=wasBusy){for(View control:selectors)control.setEnabled(!busy);wasBusy=busy;}
  // Initialize disabled selectors after a page rebuild while a session is active.
  if(busy)for(View control:selectors)control.setEnabled(false);
  boolean query=System.currentTimeMillis()>=nextDevices;
  if(query){UsbDevice selected=usb.selectedDevice();text(deviceLabel,selected==null?"USB输出：未连接或尚未选择":("USB输出："+selected.getProductName()));text(backgroundLabel,BackgroundStatus.describe(a));nextDevices=System.currentTimeMillis()+2000;}
  String active=UsbAudioService.busy()?UsbAudioService.activeSourcePackage:usb.sourcePackage;
  if(active==null)active="";
  boolean local=UsbAudioService.busy()&&UsbAudioService.instance!=null&&!UsbAudioService.instance.captureSession;
  if(local){lastSource=null;text(compatibility,"当前为辅助文件路径，未启用应用音频捕获。");openSource.setEnabled(false);}
  else if(query||!active.equals(lastSource)){lastSource=active;try{CaptureSources.uid(a,active);sourceAvailable=true;text(compatibility,active.isEmpty()?"请求所有允许捕获的媒体音频；排除OpenDSP自身。":"仅请求"+CaptureSources.label(active)+"允许的媒体音频。单次播放能否捕获以输入电平为准。");openSource.setEnabled(!active.isEmpty());}catch(Exception e){sourceAvailable=false;text(compatibility,"音源应用未安装或不可用，请重新选择。");openSource.setEnabled(false);}}
  start.setEnabled(!busy&&sourceAvailable&&android.os.Build.VERSION.SDK_INT>=29);
  boolean live=UsbAudioService.busy()&&UsbAudioService.processing;
  text(inputFormat,live?"输入："+status.optInt("input_rate",48000)+" Hz · "+status.optString("input_format","等待回读"):"输入：未运行");
  text(outputFormat,live?"USB输出："+status.optInt("rate",UsbAudioService.actualRate)+" Hz / "+status.optInt("valid_bits",usb.bits)+"bit · "+(status.optLong("packets",0)>0?"已有传输":"等待缓冲"):"USB输出：未运行");
  double in=live&&!status.isNull("input_rms_dbfs")?status.optDouble("input_rms_dbfs",Double.NEGATIVE_INFINITY):Double.NEGATIVE_INFINITY;
  double out=live&&!status.isNull("output_rms_dbfs")?status.optDouble("output_rms_dbfs",Double.NEGATIVE_INFINITY):Double.NEGATIVE_INFINITY;
  text(inputLevel,"输入："+level(in));text(outputLevel,"DSP输出："+level(out));inputMeter.setProgress(meter(in));outputMeter.setProgress(meter(out));
  text(counters,live?"完成帧 "+status.optLong("completed_frames",0)+" · 反馈 "+status.optLong("feedback_packets",0)+"\n欠载帧 "+status.optLong("underrun_frames",0)+" · USB错误 "+status.optLong("errors",0)+" · 已跳过源帧 "+status.optLong("lost_audio_frames",0):"链路计数将在开始处理后显示。");
 }
 static void text(TextView view,String value){if(!value.contentEquals(view.getText()))view.setText(value);}
 static String level(double db){return Double.isFinite(db)?String.format(Locale.US,"%.1f dBFS",db):"— / 静音";}
 static int meter(double db){return Double.isFinite(db)?(int)Math.max(0,Math.min(600,(db+60)*10)):0;}
}
