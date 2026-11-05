package org.openeq.app;

import android.app.*;
import android.os.*;
import android.content.*;
import android.graphics.*;
import android.graphics.drawable.GradientDrawable;
import android.media.*;
import android.media.audiofx.*;
import android.hardware.usb.*;
import android.net.Uri;
import android.view.*;
import android.widget.*;
import android.text.InputType;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.TimeUnit;

public final class MainActivity extends Activity {
    static java.lang.ref.WeakReference<MainActivity> live=new java.lang.ref.WeakReference<>(null);
    TextView syncStatus;String responseExport;boolean visible,needsRender;final ArrayDeque<String> redo=new ArrayDeque<>();
    final int INK=Color.rgb(32,43,67),MUTED=Color.rgb(124,138,162),ACCENT=Color.rgb(88,111,244),BG=Color.rgb(244,246,250);
    Preset preset;final Player player=new Player();UsbPanel usb;LinearLayout root,body;Chart chart;TextView playback,rootStatus;
    final Handler statusHandler=new Handler(Looper.getMainLooper());
    String systemDeviceKey="";
    final Runnable refreshStatus=new Runnable(){public void run(){if(rootStatus!=null)rootStatus.setText(RootEqService.display);if(syncStatus!=null)syncStatus.setText((SyncService.running?"自动同步 · ":"")+PresetSync.status);syncSystemRoute();if(usb!=null)usb.refresh();statusHandler.postDelayed(this,500);}};
    String page="工作台", status="共享诊断播放器尚未运行";
    DeviceRoutes routes;String deviceKey="",deviceLabel="检测音频设备…";int deviceType=-1;Equalizer globalEq;
    final BroadcastReceiver noisy=new BroadcastReceiver(){public void onReceive(Context c,Intent i){player.stop();}};
    final ArrayDeque<String> undo=new ArrayDeque<>();final HashMap<Integer,Equalizer> effects=new HashMap<>();
    final BroadcastReceiver sessions=new BroadcastReceiver(){public void onReceive(Context c,Intent i){int id=i.getIntExtra(AudioEffect.EXTRA_AUDIO_SESSION,-1);if(id<=0)return;
        if(AudioEffect.ACTION_OPEN_AUDIO_EFFECT_CONTROL_SESSION.equals(i.getAction())){if(!effects.containsKey(id))try{effects.put(id,new Equalizer(0,id));}catch(Exception ignored){}}
        else{Equalizer e=effects.remove(id);if(e!=null)e.release();}
    }};
    @Override public void onCreate(Bundle state){super.onCreate(state);
        live=new java.lang.ref.WeakReference<>(this);
        UsbVolume.load(this);
        try{preset=loadPreset(getPreferences(0).getString("current",""));}catch(Exception e){preset=Preset.defaults();}
        getWindow().setStatusBarColor(BG);getWindow().setNavigationBarColor(BG);
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR|View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR);
        IntentFilter f=new IntentFilter();f.addAction(AudioEffect.ACTION_OPEN_AUDIO_EFFECT_CONTROL_SESSION);f.addAction(AudioEffect.ACTION_CLOSE_AUDIO_EFFECT_CONTROL_SESSION);
        if(Build.VERSION.SDK_INT>=33)registerReceiver(sessions,f,Context.RECEIVER_EXPORTED);else registerReceiver(sessions,f);
        if(state!=null)page=state.getString("page","工作台");else if(getIntent().hasExtra("page"))page=getIntent().getStringExtra("page");
        String rollback=getPreferences(0).getString("sync.rollback",null);if(rollback!=null){undo.push(rollback);getPreferences(0).edit().remove("sync.rollback").apply();}
        usb=new UsbPanel(this,state);render();
        routes=new DeviceRoutes(this,(key,label,type)->{if(usb.probing||UsbAudioService.instance!=null&&UsbAudioService.instance.worker!=null)return;if(HeadphoneService.running){deviceLabel=label;deviceType=type;return;}if(type<0){deviceLabel=label;deviceType=type;return;}if(key.equals(deviceKey))return;boolean first=deviceKey.isEmpty();if(!first&&getPreferences(0).getBoolean("autoDeviceProfiles",true))getPreferences(0).edit().putString("device."+deviceKey,preset.encode()).apply();
            if(!first&&DeviceRoutes.priority(deviceType)>1&&type==AudioDeviceInfo.TYPE_BUILTIN_SPEAKER)player.stop();deviceKey=key;deviceLabel=label;deviceType=type;
            if(getPreferences(0).getBoolean("autoDeviceProfiles",true)){String saved=getPreferences(0).getString("device."+key,null);try{if(saved!=null)preset=loadPreset(saved);else if(!first)preset=Preset.defaults();}catch(Exception ignored){}changed();}render();if(first){root.setAlpha(0);root.animate().alpha(1).setDuration(360).start();}});
        if(Build.VERSION.SDK_INT>=33)registerReceiver(noisy,new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY),Context.RECEIVER_NOT_EXPORTED);else registerReceiver(noisy,new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY));
        root.setAlpha(0);root.setTranslationY(dp(12));root.animate().alpha(1).translationY(0).setDuration(360).start();
    }
    void syncSystemRoute(){if(HeadphoneService.running&&!HeadphoneService.activeKey.isEmpty()&&!HeadphoneService.activeKey.equals(systemDeviceKey)&&HeadphoneService.activePreset!=null){systemDeviceKey=HeadphoneService.activeKey;deviceKey=systemDeviceKey;deviceLabel=HeadphoneService.routeLabel;preset=HeadphoneService.activePreset.copy();render();}}
    @Override protected void onSaveInstanceState(Bundle state){super.onSaveInstanceState(state);state.putString("page",page);if(usb!=null)usb.saveState(state);}
    Preset loadPreset(String text)throws Exception{Preset p=Preset.parse(text);if(p.upgradeLegacyStarter()){getPreferences(0).edit().putString("legacy-before-volume-fix."+Integer.toHexString(text.hashCode()),text).apply();}return p;}
    int dp(float n){return (int)(n*getResources().getDisplayMetrics().density+0.5f);}
    TextView text(String s,int size,int color){TextView t=new TextView(this);t.setText(s);t.setTextSize(size);t.setTextColor(color);t.setPadding(0,dp(6),0,dp(6));return t;}
    LinearLayout col(){LinearLayout l=new LinearLayout(this);l.setOrientation(1);return l;}
    GradientDrawable shape(int color){GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(16));return d;}
    Button button(String label,Runnable action){Button b=new Button(this);b.setText(label);b.setAllCaps(false);b.setTextColor(ACCENT);b.setTextSize(14);b.setMinHeight(dp(46));b.setBackground(shape(Color.rgb(234,238,255)));b.setOnClickListener(v->action.run());return b;}
    void gap(LinearLayout l,int n){Space s=new Space(this);l.addView(s,new LinearLayout.LayoutParams(1,dp(n)));}
    LinearLayout card(){LinearLayout c=col();c.setPadding(dp(16),dp(12),dp(16),dp(12));c.setBackground(shape(Color.WHITE));body.addView(c,new LinearLayout.LayoutParams(-1,-2));gap(body,12);return c;}
    void rowButtons(LinearLayout c,String a,Runnable aa,String b,Runnable bb){LinearLayout r=new LinearLayout(this);Button x=button(a,aa),y=button(b,bb);LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.setMargins(dp(3),dp(4),dp(3),dp(4));r.addView(x,lp);r.addView(y,lp);c.addView(r);}
    void render(){
        if(page.equals("USB"))page="工作台";syncStatus=null;rootStatus=null;playback=null;chart=null;usb.resetViews();root=col();root.setBackgroundColor(BG);root.setPadding(dp(20),0,dp(20),0);
        root.setOnApplyWindowInsetsListener((v,in)->{if(Build.VERSION.SDK_INT>=30){android.graphics.Insets x=in.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());v.setPadding(dp(20),x.top,dp(20),x.bottom);}else v.setPadding(dp(20),in.getSystemWindowInsetTop(),dp(20),in.getSystemWindowInsetBottom());return in;});
        setContentView(root);root.requestApplyInsets();
        LinearLayout head=new LinearLayout(this);head.setGravity(Gravity.CENTER_VERTICAL);TextView title=text("OpenDSP",30,INK);title.setTypeface(null,1);head.addView(title,new LinearLayout.LayoutParams(0,-2,1));head.addView(button("关于",this::about));root.addView(head);
        LinearLayout exclusive=col();exclusive.setPadding(dp(14),dp(5),dp(14),dp(5));exclusive.setBackground(shape(Color.WHITE));
        usb.header(exclusive);root.addView(exclusive);
        LinearLayout nav=new LinearLayout(this);for(String p:new String[]{"工作台","均衡","机架","预设","目标","同步","设备"}){Button b=button(p,()->{page=p;render();});b.setPadding(dp(10),0,dp(10),0);b.setTextSize(13);if(p.equals(page)){b.setTextColor(Color.WHITE);b.setBackground(shape(ACCENT));}LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(dp(82),dp(44));lp.setMargins(dp(3),dp(6),dp(3),dp(12));nav.addView(b,lp);}HorizontalScrollView tabs=new HorizontalScrollView(this);tabs.setHorizontalScrollBarEnabled(false);tabs.addView(nav);root.addView(tabs);
        ScrollView sc=new ScrollView(this);sc.setFillViewport(false);body=col();sc.addView(body);root.addView(sc,new LinearLayout.LayoutParams(-1,0,1));
        if(page.equals("工作台"))usb.page();else if(page.equals("均衡"))eq();else if(page.equals("预设"))presets();else if(page.equals("工具"))tools();else if(page.equals("同步"))SyncPanel.page(this);else if(page.equals("机架"))rackPage();else if(page.equals("目标"))MobileTargets.page(this);else devices();
    }
    void eq(){
        if(HeadphoneService.running){LinearLayout mode=card();mode.addView(text(HeadphoneService.backendName,17,INK));mode.addView(text(HeadphoneService.mapped?"主EQ幅频近似到系统频段；空间、延时、相位与自研压缩仅在完整DSP路径生效。":"当前仅设备原生频段生效，下方完整DSP预设不会映射到这些频段。",12,MUTED));mode.addView(button("编辑当前系统效果",()->usb.workbench.system.edit()));}
        LinearLayout c=card();Switch sw=new Switch(this);sw.setText("参数均衡器");sw.setTextColor(INK);sw.setTextSize(17);sw.setChecked(preset.enabled);sw.setOnCheckedChangeListener((v,on)->{remember();preset.enabled=on;changed();});c.addView(sw);
        c.addView(text(preset.name+" · "+preset.bands.size()+" / 32 段",13,MUTED));
        chart=new Chart();c.addView(chart,new LinearLayout.LayoutParams(-1,dp(235)));c.addView(text("蓝：左声道 · 灰：右声道 · 含前级\n拖动频段圆点调频率/增益；点圆点编辑Q、类型与声道。",11,MUTED));
        rowButtons(c,String.format(Locale.US,"前级  %.1f dB",preset.preamp),this::preamp,"撤销",()->history(false));
        rowButtons(c,"重做",()->history(true),"响应分析",()->ResponsePanel.show(this));
        c.addView(text("参数作用于工作台正在运行的应用音频链路。无需打开本地文件；开始/停止请回工作台。",12,MUTED));
        LinearLayout advanced=card();rowButtons(advanced,"输出与声道",this::outputSettings,"预设 A/B 对比",this::abCompare);
        new MobileRackEditor(this).addControls(advanced);
        LinearLayout label=new LinearLayout(this);label.setGravity(Gravity.CENTER_VERTICAL);label.addView(text("滤波器",18,INK),new LinearLayout.LayoutParams(0,-2,1));label.addView(button("＋ 添加",()->{if(preset.bands.size()>=32){toast("最多 32 段");return;}edit(-1);}));body.addView(label);gap(body,8);
        for(int i=0;i<preset.bands.size();i++){final int n=i;Preset.Band b=preset.bands.get(i);LinearLayout item=card();
            int ti=Arrays.asList(Preset.TYPES).indexOf(b.type);TextView t=text(String.format(Locale.US,"%02d   %s   %s",i+1,Preset.LABELS[ti],b.enabled?"":"已旁路"),14,b.enabled?INK:MUTED);item.addView(t);
            item.addView(text(String.format(Locale.US,"%.1f Hz     %+.1f dB     Q %.3f     %s",b.hz,b.gain,b.q,b.channel.equals("both")?"双声道":b.channel.equals("left")?"左":"右"),13,MUTED));
            item.setOnClickListener(v->edit(n));item.setContentDescription("编辑滤波器 "+(i+1));}
        gap(body,20);
    }
    void tools(){
        usb.tools();
        LinearLayout play=card();play.addView(text("共享输出诊断（辅助）",17,INK));playback=text(status,12,MUTED);play.addView(playback);
        rowButtons(play,"打开诊断 WAV",()->open(12,"audio/*"),"停止共享诊断",()->player.stop());
        play.addView(text("该工具使用Android共享输出，只用于对照排障，不代表应用音频USB链路。",12,MUTED));
    }
    void rackPage(){LinearLayout c=card();c.addView(text("输出处理机架",23,INK));c.addView(text("与PC相同的输出、压缩、矩阵、M/S与空间模块。每组可恢复默认并撤销。",13,MUTED));rowButtons(c,"输出级 / 声道",this::outputSettings,"A / B 比较",this::abCompare);new MobileRackEditor(this).addControls(c);rowButtons(c,"撤销",()->history(false),"重做",()->history(true));}
    void history(boolean forward){ArrayDeque<String> from=forward?redo:undo,to=forward?undo:redo;if(from.isEmpty()){toast(forward?"没有可重做的操作":"没有可撤销的操作");return;}try{Preset restored=Preset.parse(from.peek());to.push(preset.encode());from.pop();preset=restored;changed();render();}catch(Exception e){error(e);}}
    void remember(){redo.clear();if(undo.size()>=30)undo.removeLast();undo.push(preset.encode());}
    void changed(){android.content.SharedPreferences.Editor e=getPreferences(0).edit().putString("current",preset.encode());if(!HeadphoneService.running&&!deviceKey.isEmpty()&&getPreferences(0).getBoolean("autoDeviceProfiles",true))e.putString("device."+deviceKey,preset.encode());e.apply();player.update(preset);RootEqService.update(preset);UsbAudioService.update(preset);HeadphoneService.update(preset,deviceKey);if(chart!=null)chart.invalidate();}
    void about(){LinearLayout c=col();c.setPadding(dp(24),dp(12),dp(24),dp(12));try(InputStream in=getAssets().open("fa-avatar.jpg")){ImageView image=new ImageView(this);image.setImageBitmap(BitmapFactory.decodeStream(in));c.addView(image,new LinearLayout.LayoutParams(dp(88),dp(88)));}catch(Exception ignored){}c.addView(text("作者 Fa",21,INK));c.addView(text("本软件为开源免费项目。\nOpenEQ · Windows / Android 通用配置\nGitHub 项目准备中",14,MUTED));new AlertDialog.Builder(this).setTitle("关于 OpenEQ").setView(c).setPositiveButton("知道了",null).show();}
    EditText field(LinearLayout c,String name,String value,boolean number){c.addView(text(name,12,MUTED));EditText e=new EditText(this);e.setText(value);e.setSingleLine();e.setTextColor(INK);if(number)e.setInputType(InputType.TYPE_CLASS_NUMBER|InputType.TYPE_NUMBER_FLAG_DECIMAL|InputType.TYPE_NUMBER_FLAG_SIGNED);c.addView(e);return e;}
    EditText stepper(LinearLayout c,String label,double value,double low,double high){
        c.addView(text(label+" · 步进 0.1",12,MUTED));LinearLayout row=new LinearLayout(this);row.setGravity(Gravity.CENTER_VERTICAL);
        EditText input=new EditText(this);input.setText(Double.toString(value));input.setSingleLine();input.setSelectAllOnFocus(true);input.setTextColor(INK);input.setInputType(InputType.TYPE_CLASS_NUMBER|InputType.TYPE_NUMBER_FLAG_DECIMAL|InputType.TYPE_NUMBER_FLAG_SIGNED);
        Button minus=button("−",()->step(input,-1,low,high)),plus=button("＋",()->step(input,1,low,high));minus.setContentDescription(label+" 减少 0.1");plus.setContentDescription(label+" 增加 0.1");
        row.addView(minus,new LinearLayout.LayoutParams(dp(62),dp(46)));row.addView(input,new LinearLayout.LayoutParams(0,dp(54),1));row.addView(plus,new LinearLayout.LayoutParams(dp(62),dp(46)));c.addView(row);return input;
    }
    void step(EditText input,int direction,double low,double high){try{java.math.BigDecimal v=new java.math.BigDecimal(input.getText().toString()).add(java.math.BigDecimal.valueOf(direction).movePointLeft(1));v=v.max(java.math.BigDecimal.valueOf(low)).min(java.math.BigDecimal.valueOf(high));input.setText(v.stripTrailingZeros().toPlainString());input.setSelection(input.length());}catch(Exception e){toast("请先输入有效数值");}}
    Switch toggle(LinearLayout c,String label,boolean value){Switch s=new Switch(this);s.setText(label);s.setChecked(value);s.setTextColor(INK);s.setPadding(0,dp(8),0,dp(8));c.addView(s);return s;}
    void outputSettings(){LinearLayout c=col();c.setPadding(dp(20),0,dp(20),dp(12));
        EditText gain=stepper(c,"输出增益 · −60 至 0 dB",preset.output,-60,0),balance=stepper(c,"声道平衡 · −1 左 / +1 右",preset.balance,-1,1),left=stepper(c,"左延时 · 0–100 ms",preset.leftDelay,0,100),right=stepper(c,"右延时 · 0–100 ms",preset.rightDelay,0,100);
        Switch il=toggle(c,"左声道反相",preset.invertLeft),ir=toggle(c,"右声道反相",preset.invertRight),protection=toggle(c,"立体声联动峰值保护",preset.protection);EditText ceiling=stepper(c,"峰值上限 · −12 至 0 dBFS",preset.ceiling,-12,0);
        c.addView(text("峰值保护使用即时衰减与平滑释放；不属于 true-peak 限幅。声道延时按采样点取整。",11,MUTED));ScrollView scroll=new ScrollView(this);scroll.addView(c);
        AlertDialog d=new AlertDialog.Builder(this).setTitle("输出与声道").setView(scroll).setNegativeButton("取消",null).setPositiveButton("保存",null).create();d.setOnShowListener(v->d.getButton(-1).setOnClickListener(w->{try{
            Preset next=preset.copy();next.output=Preset.bounded(Double.parseDouble(gain.getText().toString()),-60,0,"输出增益");next.balance=Preset.bounded(Double.parseDouble(balance.getText().toString()),-1,1,"声道平衡");
            next.leftDelay=Preset.bounded(Double.parseDouble(left.getText().toString()),0,100,"左延时");next.rightDelay=Preset.bounded(Double.parseDouble(right.getText().toString()),0,100,"右延时");next.ceiling=Preset.bounded(Double.parseDouble(ceiling.getText().toString()),-12,0,"峰值上限");next.invertLeft=il.isChecked();next.invertRight=ir.isChecked();next.protection=protection.isChecked();remember();preset=next;changed();d.dismiss();render();
        }catch(Exception e){error(e);}}));d.show();}
    void abCompare(){new AlertDialog.Builder(this).setTitle("预设 A/B 对比").setItems(new String[]{"将当前设置存为 A","将当前设置存为 B","切换到 A","切换到 B"},(v,n)->{
        if(n<2){getPreferences(0).edit().putString(n==0?"compare.a":"compare.b",preset.encode()).apply();toast(n==0?"已保存 A":"已保存 B");}
        else try{String s=getPreferences(0).getString(n==2?"compare.a":"compare.b",null);if(s==null)throw new Exception("请先保存该对比槽");Preset next=Preset.parse(s);remember();preset=next;changed();render();}catch(Exception e){error(e);}
    }).setNegativeButton("取消",null).show();}
    void preamp(){LinearLayout c=col();c.setPadding(dp(20),0,dp(20),0);EditText e=stepper(c,"前级增益 · −60 至 0 dB",preset.preamp,-60,0);AlertDialog d=new AlertDialog.Builder(this).setTitle("前级增益").setView(c).setNegativeButton("取消",null).setPositiveButton("保存",null).create();d.setOnShowListener(v->d.getButton(-1).setOnClickListener(w->{try{double x=Double.parseDouble(e.getText().toString());if(!Double.isFinite(x)||x < -60||x>0)throw new Exception("请输入 −60 至 0 dB");remember();preset.preamp=x;changed();d.dismiss();render();}catch(Exception ex){error(ex);}}));d.show();}
    void edit(int index){Preset.Band old=index<0?new Preset.Band():preset.bands.get(index);LinearLayout c=col();c.setPadding(dp(20),0,dp(20),dp(12));
        Spinner type=new Spinner(this);type.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,Preset.LABELS));type.setSelection(Arrays.asList(Preset.TYPES).indexOf(old.type));c.addView(type);
        EditText f=stepper(c,"频率 · 20–20000 Hz",old.hz,20,20000),g=stepper(c,"增益 · −24 至 +24 dB",old.gain,-24,24),q=stepper(c,"Q · 0.1–30",old.q,0.1,30);
        Spinner channel=new Spinner(this);channel.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,new String[]{"双声道","左声道","右声道"}));channel.setSelection(Arrays.asList("both","left","right").indexOf(old.channel));c.addView(channel);
        Switch on=new Switch(this);on.setText("启用此滤波器");on.setChecked(old.enabled);c.addView(on);c.addView(text("通 / 陷波 / 全通不使用增益参数；架式滤波器使用 Q。",11,MUTED));
        ScrollView scroll=new ScrollView(this);scroll.addView(c);
        AlertDialog.Builder builder=new AlertDialog.Builder(this).setTitle(index<0?"添加滤波器":"滤波器 "+(index+1)).setView(scroll).setNegativeButton("取消",null).setPositiveButton("保存",null);
        if(index>=0)builder.setNeutralButton("删除",(v,w)->{remember();preset.bands.remove(index);changed();render();});
        AlertDialog d=builder.create();d.setOnShowListener(v->d.getButton(-1).setOnClickListener(w->{try{Preset.Band b=new Preset.Band();b.type=Preset.TYPES[type.getSelectedItemPosition()];b.hz=Double.parseDouble(f.getText().toString());b.gain=Double.parseDouble(g.getText().toString());b.q=Double.parseDouble(q.getText().toString());b.channel=new String[]{"both","left","right"}[channel.getSelectedItemPosition()];b.enabled=on.isChecked();
            if(b.hz<20||b.hz>20000)throw new Exception("频率范围 20–20000 Hz");b.filter(48000);remember();if(index<0)preset.bands.add(b);else preset.bands.set(index,b);changed();d.dismiss();render();}catch(Exception e){error(e);}}));d.show();
    }
    void presets(){LinearLayout c=card();c.addView(text("当前预设",13,MUTED));c.addView(text(preset.name,24,INK));
        Switch automatic=new Switch(this);automatic.setText("按耳机 / 声卡自动记忆与切换 EQ");automatic.setChecked(getPreferences(0).getBoolean("autoDeviceProfiles",true));automatic.setOnCheckedChangeListener((v,b)->getPreferences(0).edit().putBoolean("autoDeviceProfiles",b).apply());c.addView(automatic);c.addView(text(deviceLabel,12,MUTED));
        rowButtons(c,"接收PC预设库",()->PresetLibrary.receivePc(this),"保存此设备 EQ",()->{changed();toast("已保存当前设备的 EQ");});
        rowButtons(c,"导入 JSON / APO",()->open(10,"*/*"),"导出通用 JSON",()->{Intent i=new Intent(Intent.ACTION_CREATE_DOCUMENT).setType("application/json").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_TITLE,"OpenDSP-preset.json");startActivityForResult(i,11);});
        rowButtons(c,"重命名",()->{EditText e=new EditText(this);e.setText(preset.name);new AlertDialog.Builder(this).setTitle("预设名称").setView(e).setNegativeButton("取消",null).setPositiveButton("保存",(v,w)->{String s=e.getText().toString().trim();if(s.isEmpty()||s.length()>100){toast("名称应为 1–100 字");return;}remember();preset.name=s;changed();render();}).show();},"保存到本机",()->{getPreferences(0).edit().putString("saved."+preset.name,preset.encode()).apply();render();toast("已保存");});
        c.addView(text("PC / Android共用配置 v1 · 左右独立 · 完整输出与空间参数\n通用JSON可双向直接使用，APO文本按PC同一子集导入；路径不支持的效果保留但不冒称生效。",12,MUTED));
        PresetLibrary.page(this);
        body.addView(button("恢复平直预设",()->new AlertDialog.Builder(this).setMessage("当前设置将恢复为默认值，可撤销。").setNegativeButton("取消",null).setPositiveButton("恢复",(v,w)->{remember();preset=Preset.defaults();changed();render();}).show()));
    }
    void factoryPresets(){try{String[] files=getAssets().list("presets");ArrayList<Preset> items=new ArrayList<>();ArrayList<String> names=new ArrayList<>();for(String file:files)try(InputStream in=getAssets().open("presets/"+file)){Preset p=Preset.parse(readText(in,262144));items.add(p);names.add(p.name);}new AlertDialog.Builder(this).setTitle("起点预设 · 可按听感微调").setItems(names.toArray(new String[0]),(d,n)->{remember();preset=items.get(n);changed();render();}).setNegativeButton("取消",null).show();}catch(Exception e){error(e);}}
    void devices(){LinearLayout c=card();c.addView(text(Build.MANUFACTURER+" "+Build.MODEL,21,INK));c.addView(text("Android "+Build.VERSION.RELEASE+" · API "+Build.VERSION.SDK_INT,13,MUTED));
        c.addView(button("诊断与本地辅助工具",()->{page="工具";render();}));
        c.addView(text("工作模式：免Root USB直连 / 应用内共享DSP / 系统EQ / Root全局PEQ\nUSB等时输出为新候选后端，仍需具体手机与DAC验收。\nRoot兼容链路继续使用Android共享输出。",14,INK));
        rowButtons(c,"刷新设备",this::render,"Root 检查",()->rootCheck());
        AudioManager am=(AudioManager)getSystemService(AUDIO_SERVICE);LinearLayout outputs=card();outputs.addView(text("系统输出设备",17,INK));
        for(AudioDeviceInfo d:am.getDevices(AudioManager.GET_DEVICES_OUTPUTS))outputs.addView(text(d.getProductName()+" · type "+d.getType()+"\n采样率 "+Arrays.toString(d.getSampleRates()),12,MUTED));
        UsbManager um=(UsbManager)getSystemService(USB_SERVICE);LinearLayout usb=card();usb.addView(text("USB 设备",17,INK));
        if(um.getDeviceList().isEmpty())usb.addView(text("未检测到 USB 主机设备",13,MUTED));
        for(UsbDevice d:um.getDeviceList().values())usb.addView(text(d.getProductName()+String.format(Locale.US,"\nVID %04x · PID %04x · %d interfaces",d.getVendorId(),d.getProductId(),d.getInterfaceCount()),13,MUTED));
        LinearLayout global=card();global.addView(text("Root 全局参数 EQ",17,INK));rootStatus=text(RootEqService.display,12,MUTED);global.addView(rootStatus);global.addView(text("需要 Magisk 授权。将普通媒体与游戏的混流送入完整 PEQ，再从 Android 当前设备共享输出；支持蓝牙。通话、闹钟、通知和受保护音频不承诺接管。此模式不是 USB 独占，也不宣称 bit-perfect。",12,MUTED));rowButtons(global,"启用 Root 全局 PEQ",()->{try{player.stop();stopService(new Intent(this,NativeEqService.class));changed();requestNotificationPermission();RootEqService.start(this);toast("正在启动，首次使用请在 Magisk 授权");}catch(Exception e){error(e);}},"停止 Root PEQ",()->{RootEqService.stop(this);toast("已请求停止并恢复系统路由");});
        LinearLayout sys=card();sys.addView(text("其他应用 EQ · 实验",17,INK));sys.addView(text("仅接入主动广播音效会话的应用。当前仅在 OpenEQ 前台期间监听；此处使用系统原生频段，与上面的 PEQ 配置独立。",12,MUTED));
        sys.addView(button("查看已发现的音频会话（"+effects.size()+"）",this::systemSessions));
        sys.addView(button("全局兼容模式 · 系统原生 EQ",()->{try{RootEqService.stop(this);requestNotificationPermission();globalEq=NativeEqService.open(this);systemEq(globalEq);}catch(Exception e){toast("当前系统不支持全局原生 EQ："+e.getMessage());}}));sys.addView(button("停止后台兼容 EQ",()->{stopService(new Intent(this,NativeEqService.class));globalEq=null;toast("后台兼容 EQ 已停止");}));sys.addView(text("兼容模式可用于蓝牙输出，并以通知保留后台处理、按设备记忆原生频段；覆盖率取决于系统与播放应用。实际精度由系统音效决定，可能按整 dB 量化；界面显示系统回读值。完整 PEQ 仍需独立音频路径。",12,MUTED));
        LinearLayout info=card();info.addView(text("目标测试设备",17,INK));info.addView(text("一加 13T · Android 16 · 免 Root\nGalaxy Z Flip5 · Android 15 · Magisk\n幻音 4X / 原道 NK1 Max\nWindows：原道 TEARDSP",13,MUTED));
    }
    void rootCheck(){toast("检查中；首次可能出现 Magisk 授权");new Thread(()->{java.lang.Process p=null;try{p=new ProcessBuilder("su","-c","id").redirectErrorStream(true).start();if(!p.waitFor(12,TimeUnit.SECONDS)){p.destroyForcibly();throw new Exception("授权超时或未允许");}String result=readText(p.getInputStream(),8192);boolean ok=p.exitValue()==0&&result.contains("uid=0(");runOnUiThread(()->new AlertDialog.Builder(this).setTitle(ok?"Root 可用":"Root 未获授权").setMessage(result+"\n\n此检查不修改系统设置。Root 可用不等于已启用全局音频接管。").setPositiveButton("知道了",null).show());}catch(Exception e){runOnUiThread(()->error(e));}},"OpenEQ-root-check").start();}
    void systemSessions(){if(effects.isEmpty()){toast("尚未发现会话；保持 OpenEQ 前台，再从另一应用开始播放");return;}Integer[] ids=effects.keySet().toArray(new Integer[0]);String[] names=new String[ids.length];for(int n=0;n<ids.length;n++)names[n]="音频会话 "+ids[n];new AlertDialog.Builder(this).setTitle("系统原生 EQ").setItems(names,(v,n)->systemEq(effects.get(ids[n]))).setNegativeButton("关闭",null).show();}
    void systemEq(Equalizer e){try{LinearLayout c=col();c.setPadding(dp(20),dp(8),dp(20),dp(8));Switch on=new Switch(this);on.setText("启用系统 EQ");on.setChecked(e.getEnabled());on.setOnCheckedChangeListener((v,b)->{try{if(e.setEnabled(b)!=AudioEffect.SUCCESS)throw new Exception("系统未接受启用请求");if(e==globalEq)NativeEqService.save(this);}catch(Exception x){error(x);}});c.addView(on);
        short[] range=e.getBandLevelRange();for(short i=0;i<e.getNumberOfBands();i++){final short band=i;TextView t=text(String.format(Locale.US,"%.0f Hz · %.1f dB",e.getCenterFreq(i)/1000.0,e.getBandLevel(i)/100.0),13,INK);c.addView(t);SeekBar s=new SeekBar(this);s.setMax((range[1]-range[0])/10);s.setProgress((e.getBandLevel(i)-range[0])/10);s.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onProgressChanged(SeekBar b,int value,boolean user){if(user)try{e.setBandLevel(band,(short)(range[0]+value*10));t.setText(String.format(Locale.US,"%.0f Hz · %.1f dB",e.getCenterFreq(band)/1000.0,e.getBandLevel(band)/100.0));if(e==globalEq)NativeEqService.save(MainActivity.this);}catch(Exception x){error(x);}}public void onStartTrackingTouch(SeekBar b){}public void onStopTrackingTouch(SeekBar b){}});c.addView(s);}
        ScrollView scroll=new ScrollView(this);scroll.addView(c);new AlertDialog.Builder(this).setTitle("系统 EQ · 非自研 PEQ").setView(scroll).setPositiveButton("完成",null).show();}catch(Exception x){error(x);}}
    void open(int request,String mime){startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType(mime).addCategory(Intent.CATEGORY_OPENABLE),request);}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(usb!=null&&usb.result(request,result,data))return;if(result!=RESULT_OK||data==null||data.getData()==null)return;Uri uri=data.getData();
        try{if(request==61){try(InputStream in=getContentResolver().openInputStream(uri)){PresetSync.pair(this,readText(in,8192));}page="同步";render();}
            else if(request==62||request==63){MobileTargets.imported(this,request,uri);}
            else if(request==64){if(responseExport==null)throw new IOException("请先生成响应分析");try(OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new IOException("无法打开文件");out.write(responseExport.getBytes(StandardCharsets.UTF_8));}toast("已导出完整响应");}
            else if(request==10){String content;try(InputStream in=getContentResolver().openInputStream(uri)){content=readText(in,2097152);}String clean=content.startsWith("\ufeff")?content.substring(1):content;
                if(clean.trim().startsWith("{")&&new org.json.JSONObject(clean).optString("format").equals("org.opendsp.preset-library")){org.json.JSONObject imported=PresetLibrary.merge(this,new org.json.JSONObject(clean));render();toast("预设库导入完成："+imported.getInt("added")+"份新增；当前调音未切换");}
                else{Preset next=PresetImport.parse(content);remember();preset=next;changed();render();toast("配置已导入");}}
            else if(request==11){try(OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new IOException("无法打开文件");out.write(preset.encode().getBytes(StandardCharsets.UTF_8));}toast("配置已导出");}
            else if(request==12){if(player.playing()||UsbAudioService.processing)throw new Exception("请先停止当前试听或USB会话");InputStream in=getContentResolver().openInputStream(uri);try{player.play(in,preset,msg->runOnUiThread(()->{status=msg;if(playback!=null)playback.setText(msg);}));}catch(Exception e){if(in!=null)in.close();throw e;}}
        }catch(Exception e){error(e);}}
    static String readText(InputStream in,int limit)throws IOException{if(in==null)throw new IOException("无法读取文件");ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] b=new byte[4096];int n;while((n=in.read(b))!=-1){if(out.size()+n>limit)throw new IOException("文件过大");out.write(b,0,n);}return new String(out.toByteArray(),StandardCharsets.UTF_8);}
    void toast(String s){Toast.makeText(this,s,Toast.LENGTH_LONG).show();}
    void error(Exception e){toast(e.getMessage()==null?e.getClass().getSimpleName():e.getMessage());}
    void requestNotificationPermission(){if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=android.content.pm.PackageManager.PERMISSION_GRANTED)requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},40);}
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] grants){super.onRequestPermissionsResult(request,permissions,grants);if(request==78&&usb!=null)usb.recordPermission(grants.length>0&&grants[0]==android.content.pm.PackageManager.PERMISSION_GRANTED);if(request==79&&usb!=null)usb.workbench.system.startGranted();}
    @Override public boolean dispatchKeyEvent(KeyEvent event){
        int key=event.getKeyCode();if(UsbAudioService.processing&&!UsbAudioService.dopActive&&(key==KeyEvent.KEYCODE_VOLUME_UP||key==KeyEvent.KEYCODE_VOLUME_DOWN||key==KeyEvent.KEYCODE_VOLUME_MUTE)){
            if(event.getAction()==KeyEvent.ACTION_DOWN){if(key==KeyEvent.KEYCODE_VOLUME_MUTE){if(event.getRepeatCount()==0)UsbVolume.toggle(this);}else UsbVolume.adjust(this,key==KeyEvent.KEYCODE_VOLUME_UP?UsbVolume.STEP:-UsbVolume.STEP);if(usb!=null)usb.refresh();}return true;
        }return super.dispatchKeyEvent(event);
    }
    @Override protected void onStart(){super.onStart();visible=true;if(needsRender){needsRender=false;render();}statusHandler.removeCallbacks(refreshStatus);statusHandler.post(refreshStatus);}
    @Override protected void onStop(){visible=false;statusHandler.removeCallbacks(refreshStatus);super.onStop();player.stop();}
    @Override protected void onDestroy(){if(live.get()==this)live.clear();player.stop();if(routes!=null)routes.close();if(usb!=null)usb.close();unregisterReceiver(noisy);unregisterReceiver(sessions);for(Equalizer e:effects.values())e.release();effects.clear();super.onDestroy();}
    final class Chart extends View {
        int dragging=-1;Preset owner;boolean moved;float downX,downY;long lastChange;
        final Paint paint=new Paint(3);Chart(){super(MainActivity.this);setContentDescription("20 Hz 至 20 kHz 的均衡器频率响应，含前级增益");}
        float x(double hz){return dp(26)+(getWidth()-dp(40))*(float)(Math.log10(hz/20)/3);}
        float y(double db){return dp(12)+(getHeight()-dp(38))*(float)((18-db)/60);}
        @Override protected void onDraw(Canvas c){super.onDraw(c);paint.setTypeface(Typeface.DEFAULT);paint.setTextSize(dp(10));paint.setStrokeWidth(dp(1));
            for(int db:new int[]{12,0,-12,-24,-36}){paint.setColor(Color.rgb(231,235,230));c.drawLine(dp(26),y(db),getWidth()-dp(14),y(db),paint);paint.setColor(MUTED);c.drawText(""+db,0,y(db)+dp(3),paint);}
            for(int hz:new int[]{20,100,1000,10000,20000}){paint.setColor(Color.rgb(231,235,230));c.drawLine(x(hz),dp(12),x(hz),getHeight()-dp(26),paint);paint.setColor(MUTED);String s=hz>=1000?(hz/1000)+"k":""+hz;c.drawText(s,x(hz)-dp(8),getHeight()-dp(5),paint);}
            int rate=MobileTargets.rate(MainActivity.this);
            for(String channel:new String[]{"right","left"}){
                ArrayList<org.openeq.dsp.Biquad> filters=new ArrayList<>();if(preset.enabled)for(Preset.Band b:preset.bands)if(b.enabled&&b.hz<rate/2.0&&(b.channel.equals("both")||b.channel.equals(channel)))filters.add(b.filter(rate));
                Path path=new Path();for(int n=0;n<=240;n++){double hz=20*Math.pow(1000,n/240.0),db=preset.enabled?preset.preamp:0;for(org.openeq.dsp.Biquad f:filters)db+=20*Math.log10(Math.max(1e-12,f.magnitude(hz,rate)));float yy=y(Math.max(-42,Math.min(18,db)));if(n==0)path.moveTo(x(hz),yy);else path.lineTo(x(hz),yy);}paint.setColor(channel.equals("left")?ACCENT:Color.rgb(148,163,154));paint.setStyle(Paint.Style.STROKE);paint.setStrokeWidth(dp(channel.equals("left")?2:3));c.drawPath(path,paint);paint.setStyle(Paint.Style.FILL);
            }
            paint.setStyle(Paint.Style.FILL);paint.setTextSize(dp(9));
            for(int i=0;i<preset.bands.size();i++){Preset.Band b=preset.bands.get(i);float px=x(b.hz),py=y(TargetFitter.clamp(preset.preamp+b.gain,-40,16));paint.setColor(b.enabled?ACCENT:MUTED);c.drawCircle(px,py,dp(i==dragging?8:6),paint);paint.setColor(Color.WHITE);c.drawText(Integer.toString(i+1),px-dp(3),py+dp(3),paint);}
        }
        @Override public boolean onTouchEvent(MotionEvent event){
            if(event.getActionMasked()==MotionEvent.ACTION_DOWN){double best=dp(24)*dp(24);dragging=-1;for(int i=0;i<preset.bands.size();i++){Preset.Band b=preset.bands.get(i);double dx=event.getX()-x(b.hz),dy=event.getY()-y(TargetFitter.clamp(preset.preamp+b.gain,-40,16)),distance=dx*dx+dy*dy;if(distance<best){best=distance;dragging=i;}}
                if(dragging<0)return false;owner=preset;moved=false;downX=event.getX();downY=event.getY();getParent().requestDisallowInterceptTouchEvent(true);return true;}
            if(dragging<0)return false;if(owner!=preset){dragging=-1;return false;}
            if(event.getActionMasked()==MotionEvent.ACTION_MOVE){
                if(!moved&&Math.hypot(event.getX()-downX,event.getY()-downY)<dp(4))return true;if(!moved){remember();moved=true;}
                Preset.Band b=preset.bands.get(dragging);double fraction=TargetFitter.clamp((event.getX()-dp(26))/Math.max(1,getWidth()-dp(40)),0,1);
                b.hz=Math.round(20*Math.pow(1000,fraction)*10)/10d;
                if(b.type.equals("peak")||b.type.endsWith("_shelf"))b.gain=Math.round(TargetFitter.clamp(18-(event.getY()-dp(12))*60/Math.max(1,getHeight()-dp(38))-preset.preamp,-24,24)*10)/10d;
                long now=SystemClock.uptimeMillis();if(now-lastChange>60){changed();lastChange=now;}invalidate();return true;
            }
            if(event.getActionMasked()==MotionEvent.ACTION_UP||event.getActionMasked()==MotionEvent.ACTION_CANCEL){int index=dragging;dragging=-1;getParent().requestDisallowInterceptTouchEvent(false);if(moved){changed();render();}else if(event.getActionMasked()==MotionEvent.ACTION_UP){performClick();edit(index);}return true;}
            return true;
        }
        @Override public boolean performClick(){super.performClick();return true;}
    }
}
