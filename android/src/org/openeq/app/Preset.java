package org.openeq.app;
import org.json.*;
import java.util.*;
import org.openeq.dsp.Biquad;

final class Preset {
    static final String[] TYPES={"peak","low_shelf","high_shelf","low_pass","high_pass","notch","all_pass"};
    static final String[] LABELS={"峰值","低架","高架","低通 · 12 dB/oct","高通 · 12 dB/oct","陷波","全通"};
    String name="默认", device="";
    double preamp=0;
    boolean enabled=true;
    double output=0,balance=0,leftDelay=0,rightDelay=0,ceiling=-1;
    boolean invertLeft=false,invertRight=false,protection=true;
    JSONObject processing=new JSONObject();
    final ArrayList<Band> bands=new ArrayList<>();
    static class Band {
        String type="peak",channel="both";
        double hz=1000,gain=0,q=0.707;
        boolean enabled=true;
        Biquad filter(double rate){return new Biquad(type,hz,gain,q,rate);}
    }
    static Preset defaults(){Preset p=new Preset();for(double f:new double[]{60,150,400,1000,2500,6000,12000}){Band b=new Band();b.hz=f;p.bands.add(b);}return p;}
    boolean upgradeLegacyStarter(){double[] hz={60,150,400,1000,2500,6000,12000};if(!(name.equals("默认")||name.equals("平直 · 原声起点"))||preamp!=-6||output!=-12||!enabled||bands.size()!=hz.length||balance!=0||leftDelay!=0||rightDelay!=0||invertLeft||invertRight||!protection||ceiling!=-1)return false;for(int n=0;n<hz.length;n++){Band b=bands.get(n);if(b.hz!=hz[n]||!b.type.equals("peak")||b.gain!=0||b.q!=.707||!b.channel.equals("both")||!b.enabled)return false;}preamp=0;output=0;return true;}
    JSONObject json() throws JSONException {
        JSONObject j=new JSONObject().put("format","org.openeq.preset").put("version",1).put("name",name)
            .put("preamp_db",preamp).put("enabled",enabled).put("device_hint",device);
        JSONArray a=new JSONArray();for(Band b:bands)a.put(new JSONObject().put("type",b.type).put("frequency_hz",b.hz).put("gain_db",b.gain).put("q",b.q).put("channel",b.channel).put("enabled",b.enabled));
        j.put("output",new JSONObject(processing.toString()).put("gain_db",output).put("balance",balance).put("left_delay_ms",leftDelay).put("right_delay_ms",rightDelay).put("invert_left",invertLeft).put("invert_right",invertRight).put("peak_protection",protection).put("ceiling_db",ceiling));
        return j.put("filters",a);
    }
    static Preset parse(String text)throws Exception {
        if(text.length()>262144)throw new Exception("预设文件过大");
        JSONObject j=new JSONObject(text);
        if(!"org.openeq.preset".equals(j.getString("format"))||j.getInt("version")!=1)throw new Exception("不支持的配置格式或版本");
        Preset p=new Preset();p.name=j.getString("name");if(p.name.length()>100)throw new Exception("名称过长");
        p.preamp=j.getDouble("preamp_db");if(!Double.isFinite(p.preamp)||p.preamp < -60||p.preamp>0)throw new Exception("前级范围 -60 至 0 dB");
        p.enabled=j.getBoolean("enabled");p.device=j.optString("device_hint","");
        JSONObject o=j.optJSONObject("output");if(o!=null){
            p.processing=new JSONObject(o.toString());
            p.output=bounded(o.getDouble("gain_db"),-60,0,"输出增益");p.balance=bounded(o.getDouble("balance"),-1,1,"声道平衡");
            p.leftDelay=bounded(o.getDouble("left_delay_ms"),0,100,"左延时");p.rightDelay=bounded(o.getDouble("right_delay_ms"),0,100,"右延时");
            p.invertLeft=o.getBoolean("invert_left");p.invertRight=o.getBoolean("invert_right");p.protection=o.getBoolean("peak_protection");p.ceiling=bounded(o.getDouble("ceiling_db"),-12,0,"峰值上限");
        }
        JSONArray a=j.getJSONArray("filters");if(a.length()>32)throw new Exception("最多 32 段");
        for(int i=0;i<a.length();i++){
            JSONObject x=a.getJSONObject(i);Band b=new Band();b.type=x.getString("type");b.hz=x.getDouble("frequency_hz");b.gain=x.getDouble("gain_db");b.q=x.getDouble("q");b.channel=x.getString("channel");b.enabled=x.getBoolean("enabled");
            if(b.hz<20||b.hz>20000||!Arrays.asList("both","left","right").contains(b.channel))throw new Exception("频率或声道无效");
            b.filter(48000);p.bands.add(b);
        }DspRack.validate(p);return p;
    }
    static double bounded(double v,double low,double high,String name)throws Exception{if(!Double.isFinite(v)||v<low||v>high)throw new Exception(name+"范围 "+low+"–"+high);return v;}
    String encode(){try{return json().toString(2);}catch(Exception e){throw new IllegalStateException(e);}}
    Preset copy(){try{return parse(encode());}catch(Exception e){throw new IllegalStateException(e);}}
    double response(double hz,String channel){
        if(!enabled)return 0;
        double db=preamp;
        for(Band b:bands)if(b.enabled&&(b.channel.equals("both")||b.channel.equals(channel)))db+=20*Math.log10(Math.max(1e-12,b.filter(48000).magnitude(hz,48000)));
        return db;
    }
}
