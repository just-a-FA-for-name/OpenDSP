package org.openeq.app;
import java.util.regex.*;
/** Same canonical JSON and strict Equalizer APO subset as desktop. */
final class PresetImport {
 static Preset parse(String text)throws Exception{
  if(text.length()>262144)throw new Exception("预设文件过大");
  String clean=text.startsWith("\ufeff")?text.substring(1):text;
  if(clean.trim().startsWith("{"))return Preset.parse(clean);
  Preset result=Preset.defaults();result.bands.clear();result.name="导入 EQ 参数";
  Pattern pre=Pattern.compile("^Preamp:\\s*([-+\\d.]+)\\s*dB\\s*$",Pattern.CASE_INSENSITIVE);
  Pattern filter=Pattern.compile("^Filter\\s*\\d*:\\s*(ON|OFF)\\s+(PK|LS|HS|LP|HP|NO|AP)\\s+Fc\\s+([-+\\d.]+)\\s+Hz(?:\\s+Gain\\s+([-+\\d.]+)\\s+dB)?\\s+Q\\s+([-+\\d.]+)\\s*$",Pattern.CASE_INSENSITIVE);
  for(String raw:clean.split("\\n")){
   String line=raw.split("#",2)[0].trim();if(line.isEmpty())continue;Matcher p=pre.matcher(line);if(p.matches()){result.preamp=Double.parseDouble(p.group(1));continue;}
   Matcher f=filter.matcher(line);if(!f.matches())throw new Exception("不支持的EQ APO指令，未近似转换："+line);
   Preset.Band band=new Preset.Band();String kind=f.group(2).toUpperCase(java.util.Locale.ROOT);
   band.type=kind.equals("PK")?"peak":kind.equals("LS")?"low_shelf":kind.equals("HS")?"high_shelf":kind.equals("LP")?"low_pass":kind.equals("HP")?"high_pass":kind.equals("NO")?"notch":"all_pass";
   band.hz=Double.parseDouble(f.group(3));band.gain=f.group(4)==null?0:Double.parseDouble(f.group(4));band.q=Double.parseDouble(f.group(5));band.enabled=f.group(1).equalsIgnoreCase("ON");result.bands.add(band);
  }
  if(result.bands.isEmpty())throw new Exception("没有可导入的滤波器");return Preset.parse(result.encode());
 }
}
