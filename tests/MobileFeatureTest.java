package org.openeq.app;
import java.nio.file.*;
import org.json.*;
public final class MobileFeatureTest {
 static void check(boolean value,String message){if(!value)throw new AssertionError(message);}
 static JSONObject read(Path p)throws Exception{return new JSONObject(new String(Files.readAllBytes(p),"UTF-8"));}
 public static void main(String[] args)throws Exception{
  Path folder=Paths.get(args[0]);Preset preset=Preset.parse(new String(Files.readAllBytes(folder.resolve("desktop-complete.json")),"UTF-8"));
  JSONObject reference=read(folder.resolve("desktop-response.json")),mobile=ResponsePanel.analyze(preset,384000);
  String[] desktop={"LeftToLeft","RightToLeft","LeftToRight","RightToRight"},phone={"left_to_left","right_to_left","left_to_right","right_to_right"};
  double maxGain=0,maxDelay=0,maxPhase=0;for(int n=0;n<97;n++)for(int p=0;p<4;p++){
   JSONObject d=reference.getJSONArray("points").getJSONObject(n).getJSONObject(desktop[p]),m=mobile.getJSONArray("points").getJSONObject(n).getJSONObject(phone[p]);
   check(d.isNull("GainDb")==m.isNull("gain_db"),"Zero-path parity");if(d.isNull("GainDb"))continue;
   maxGain=Math.max(maxGain,Math.abs(d.getDouble("GainDb")-m.getDouble("gain_db")));maxDelay=Math.max(maxDelay,Math.abs(d.getDouble("GroupDelayMs")-m.getDouble("group_delay_ms")));
   double diff=Math.abs(d.getDouble("PhaseDegrees")-m.getDouble("phase_degrees"));maxPhase=Math.max(maxPhase,Math.min(diff,Math.abs(diff-360)));
  }
  check(maxGain<1e-7&&maxDelay<1e-3&&maxPhase<1e-6,"Linear response parity "+maxGain+"/"+maxDelay+"/"+maxPhase);
  JSONArray catalog=new JSONArray(new String(Files.readAllBytes(Paths.get(args[1])),"UTF-8"));check(catalog.length()==35,"Shared catalog size");TargetFitter.Curve target=null,acoustic=null;
  for(int i=0;i<catalog.length();i++){TargetFitter.Curve curve=new TargetFitter.Curve(catalog.getJSONObject(i));if(curve.id.equals("preference-7"))target=curve;if(curve.measured())acoustic=curve;}
  TargetFitter.Options options=new TargetFitter.Options();options.bands=12;options.max=20000;TargetFitter.Result fit=TargetFitter.fit(target,null,"none",384000,options,preset);
  JSONObject expected=read(folder.resolve("desktop-target-fit.json"));double maxFit=0;JSONArray responses=expected.getJSONArray("response");for(int i=0;i<fit.response.length;i++)maxFit=Math.max(maxFit,Math.abs(fit.response[i][3]-responses.getJSONArray(i).getDouble(3)));
  check(maxFit<1e-6,"Target fit parity "+maxFit);check(fit.preset.preamp==preset.preamp&&fit.preset.processing.toString().equals(preset.processing.toString()),"Fit preserves preamp and rack");
  try{TargetFitter.fit(acoustic,null,acoustic.rig,384000,options,preset);throw new AssertionError("Missing measurement accepted");}catch(AssertionError e){throw e;}catch(Exception expectedError){}
  check(PresetSync.direction("l","r","","").equals("first"),"First sync requires choice");
  check(PresetSync.direction("newL","oldR","oldL","oldR").equals("push"),"Local-only update");
  check(PresetSync.direction("oldL","newR","oldL","oldR").equals("pull"),"Remote-only update");
  check(PresetSync.direction("newL","newR","oldL","oldR").equals("conflict"),"Two-sided conflict");
  check(PresetSync.direction("same","same","oldL","oldR").equals("equal"),"Convergent edits");
  System.out.println("PASS: four-path response gain="+maxGain+"dB phase="+maxPhase+"deg delay="+maxDelay+"ms; target-fit="+maxFit+"dB; 35 targets; sync conflict decisions");
 }
}
