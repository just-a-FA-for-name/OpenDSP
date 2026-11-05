using System.Text.Json;
namespace OpenEQ;
internal static class DesktopChecks {
 public static int Run(string path){void Check(bool value,string label){if(!value)throw new Exception(label);}var report=new Dictionary<string,object>();try{
  foreach(var p in DeviceProfiles.Factory()){p.Validate();Check(p.Bands.All(b=>Math.Abs(b.Gain)<=2),"Factory preset too aggressive");}report["factory_presets"]=4;
  var store=new DeviceProfiles(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"profile-test"));var warm=DeviceProfiles.Factory()[1];store.Save("usb-one",warm);store.Save("bluetooth-two",DeviceProfiles.Factory()[2]);var reopened=new DeviceProfiles(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"profile-test"));Check(reopened.Load("usb-one")!.Name==warm.Name,"USB profile persistence");Check(reopened.Load("bluetooth-two")!.Name!=warm.Name,"Bluetooth profile separation");Check(reopened.Load("new")==null,"Unknown device profile");reopened.Enabled=false;Check(reopened.Load("usb-one")==null,"Disabled autoload");report["device_profiles"]="PASS";
  var imported=PresetImport.EqualizerApo("Preamp: -6 dB\nFilter 1: ON PK Fc 1000 Hz Gain 6 dB Q 1.4\nFilter 2: OFF HS Fc 8000 Hz Gain -2 dB Q 0.707");Check(imported.Bands.Count==2&&!imported.Bands[1].Enabled,"APO import");Check(Math.Abs(imported.Response(1000,true))<1e-8,"Imported peak response");Check(PresetImport.Graphic31().Bands.Count==31,"31 graphic bands");Check(Math.Abs(PresetImport.Headroom(imported)+7)<.2,"Auto headroom");bool rejected=false;try{PresetImport.EqualizerApo("Include: secrets.txt");}catch(ArgumentException){rejected=true;}Check(rejected,"Unsupported directives not rejected");report["apo_import_graphic_eq_headroom"]="PASS";
  var legacy=Preset.Flat();legacy.Name="平直 · 原声起点";legacy.Preamp=-6;legacy.Output.Gain=-12;Check(DeviceProfiles.UpgradeLegacyStarter(legacy)&&legacy.Preamp==0&&legacy.Output.Gain==0,"Legacy starter must not hide 18 dB attenuation");var custom=Preset.Flat();custom.Name="平直 · 原声起点";custom.Preamp=-6;custom.Output.Gain=-12;custom.Bands[0].Gain=.1;Check(!DeviceProfiles.UpgradeLegacyStarter(custom)&&custom.Output.Gain==-12,"Custom device EQ must remain unchanged");report["legacy_default_migration"]="PASS";
  var flat=Preset.Flat();var linear=new StereoProcessor(48000);linear.Configure(flat);for(int n=0;n<48000;n++)linear.Process(.1,.1);Check(Math.Abs(linear.Left-.1)<1e-7,"Flat default unexpectedly attenuates output");var ext=DeviceFormats.Create(384000,32,true);Check(ext is NAudio.Wave.WaveFormatExtensible xf&&xf.SubFormat==new Guid("00000001-0000-0010-8000-00aa00389b71"),"32 bit PCM was mislabeled float");report["unity_gain_and_pcm_encoding"]="PASS";
  string before=flat.Encode(),context="device-a|48000|off";
  var proposed=Preset.Blank();proposed.Bands.Add(new Band{Frequency=1000,Gain=6,Q=1.4});
  var proposal=AiTuning.Preview(flat,proposed,48000,context);
  Check(flat.Encode()==before&&proposed.Preamp==0,"AI preview mutated live or proposed settings");
  Check(Math.Abs(proposal.Preset.Preamp+7)<.15&&proposal.EstimatedPeakGainDb<=-0.99,"AI preview did not reserve EQ headroom");
  Check(proposal.EqResponse.Length==97&&proposal.EqResponse.All(p=>p.All(double.IsFinite)),"AI curve response invalid");
  AiTuning.RequireRevision(proposal.BaseRevision,flat,context);
  bool staleRejected=false;try{AiTuning.RequireRevision(proposal.BaseRevision,flat,"device-b|48000|off");}catch(InvalidOperationException){staleRejected=true;}
  Check(staleRejected,"AI revision ignored an output-device change");
  var manuallyChanged=flat.Copy();manuallyChanged.Preamp=-3;staleRejected=false;
  try{AiTuning.RequireRevision(proposal.BaseRevision,manuallyChanged,context);}catch(InvalidOperationException){staleRejected=true;}
  Check(staleRejected,"AI revision overwrote a manual edit");
  proposed.Output.Matrix.Ll=2;proposed.Output.Matrix.Rr=2;proposed.Output.Compressor.Enabled=true;proposed.Output.Compressor.MakeupDb=6;
  proposal=AiTuning.Preview(flat,proposed,48000,context);
  Check(proposal.Preset.Preamp<=-19&&proposal.EstimatedPeakGainDb<=-.99,"AI headroom omitted matrix or compressor makeup");
  proposed.Enabled=false;proposal=AiTuning.Preview(flat,proposed,48000,context);
  Check(proposal.Preset.Output.Gain< -13&&proposal.EstimatedPeakGainDb<=-.99,"Bypassed EQ cannot attenuate through preamp");
  proposed=Preset.Blank();proposed.Bands.Add(new Band{Frequency=20000,Gain=12});
  proposal=AiTuning.Preview(flat,proposed,32000,"device-a|32000|off");
  Check(proposal.EqResponse.All(p=>Math.Abs(p[1]+1)<1e-6),"AI preview did not match above-Nyquist bypass");
  proposed.Bands[0].Gain=double.NaN;bool invalidAi=false;
  try{AiTuning.Preview(flat,proposed,48000,context);}catch(ArgumentException){invalidAi=true;}
  Check(invalidAi&&flat.Encode()==before,"Invalid AI proposal changed live state");
  proposed=Preset.Blank();for(int n=0;n<4;n++)proposed.Bands.Add(new Band{Frequency=1000,Gain=24});
  invalidAi=false;try{AiTuning.Preview(flat,proposed,48000,context);}catch(ArgumentException){invalidAi=true;}
  Check(invalidAi,"AI headroom must reject boosts beyond available attenuation");
  report["ai_preview_revision_headroom"]="PASS";
  var delayed=Preset.Blank();delayed.Output.LeftDelay=7;delayed.Output.RightDelay=11;delayed.Output.InvertRight=true;
  var responses=LinearResponse.Analyze(delayed,48000);
  Check(responses.All(p=>Math.Abs(p.LeftToLeft.GainDb!.Value)<1e-7&&Math.Abs(p.LeftToLeft.GroupDelayMs!.Value-7)<1e-5&&Math.Abs(p.RightToRight.GroupDelayMs!.Value-11)<1e-5),"Linear analysis lost channel delay or gain");
  Check(Math.Abs(responses[0].LeftToLeft.PhaseDegrees!.Value+50.4)<1e-6&&Math.Abs(responses[0].RightToRight.PhaseDegrees!.Value-100.8)<1e-6,"Linear phase did not include polarity and delay");
  Check(responses.All(p=>p.RightToLeft.GainDb==null&&p.LeftToRight.GroupDelayMs==null),"Zero crosstalk paths must have undefined phase/delay");
  delayed.Output.LeftDelay=delayed.Output.RightDelay=0;delayed.Output.InvertRight=false;delayed.Output.Matrix.Width=0;
  Check(LinearResponse.Analyze(delayed,48000).All(p=>Math.Abs(p.LeftToLeft.GainDb!.Value+6.020599913)<1e-6&&Math.Abs(p.RightToLeft.GainDb!.Value+6.020599913)<1e-6),"Mono width matrix analysis is incorrect");
  delayed=Preset.Blank();delayed.Bands.Add(new Band{Type="all_pass",Frequency=1000,Q=.707});
  Check(LinearResponse.Analyze(delayed,48000).All(p=>Math.Abs(p.LeftToLeft.GainDb!.Value)<1e-6&&p.LeftToLeft.GroupDelayMs>=0),"All-pass magnitude/group-delay response is incorrect");
  report["linear_phase_group_delay_matrix_analysis"]="PASS";
  var devices=AudioEngine.Devices();report["real_endpoints"]=devices;Check(devices.Where(d=>d.Name.Contains("Tears DSP")).All(d=>d.Transport=="USB"),"USB transport detection");report["result"]="PASS";return 0;
 }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}finally{File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}}
}
