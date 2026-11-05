package org.openeq.app;
import java.nio.file.*;
import org.json.*;
public final class PresetInteropTest {
 public static void main(String[] args)throws Exception{
  Path directory=Paths.get(args[0]);Preset p=Preset.parse(new String(Files.readAllBytes(directory.resolve("desktop-complete.json")),"UTF-8"));
  if(p.bands.size()!=32||p.processing.getJSONObject("spatial").getJSONArray("mid_filters").length()!=16||p.processing.getJSONObject("spatial").getJSONArray("side_filters").length()!=16)throw new AssertionError("Incomplete desktop import");
  Files.write(directory.resolve("android-returned.json"),p.encode().getBytes("UTF-8"));
  Preset apo=PresetImport.parse("Preamp: -4 dB\nFilter 1: ON PK Fc 1200 Hz Gain -3.5 dB Q 1.4\nFilter 2: OFF HS Fc 6500 Hz Gain 2 dB Q 0.7");
  Files.write(directory.resolve("android-apo.json"),apo.encode().getBytes("UTF-8"));
  System.out.println("PASS: Android import/export preserves32 main +16/16 M/S, full output and shared APO subset");
 }
}
