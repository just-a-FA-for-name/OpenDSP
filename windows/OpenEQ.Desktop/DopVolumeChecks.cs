using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
namespace OpenEQ;
internal static class DopVolumeChecks {
 sealed class PartialFailure(string path):IDopVolumeBackend {
  readonly DopFileVolume inner=new(path);public bool SupportsUnity=>true;
  public float Db{get=>inner.Db;set{if(value==0){inner.Db=-1;throw new IOException("Injected partial volume write failure");}inner.Db=value;}}
  public void Dispose()=>inner.Dispose();
 }
 sealed class Constant:ISampleProvider {
  public WaveFormat WaveFormat=>WaveFormat.CreateIeeeFloatWaveFormat(176400,2);
  public int Read(float[] buffer,int offset,int count){Array.Fill(buffer,.2f,offset,count);return count;}
 }
 sealed class PartialAndRestoreFailure(string path):IDopVolumeBackend {
  readonly DopFileVolume inner=new(path);bool failed;
  public bool SupportsUnity=>true;public float Db{get=>inner.Db;set{
   if(value==0){inner.Db=-1;throw new IOException("Injected unity failure");}
   if(!failed){failed=true;throw new IOException("Injected parent restoration failure");}inner.Db=value;
  }}
  public void Dispose()=>inner.Dispose();
 }
 internal static int Owner(string volume,string transaction,string ready){using var lease=new DopVolumeLease(new DopFileVolume(volume),transaction,volume,true);lease.Activate();File.WriteAllText(ready,Environment.ProcessId.ToString());Thread.Sleep(30000);return 0;}
 internal static int Device(string path,string id){
  var report=new Dictionary<string,object>();float? original=null;
  try{
   using var en=new NAudio.CoreAudioApi.MMDeviceEnumerator();using var device=en.GetDevice(id);original=device.AudioEndpointVolume.MasterVolumeLevel;
   report["device"]=device.FriendlyName;report["before_db"]=original;
   // Acquire and start a physically silent exclusive stream before normalizing
   // the endpoint. Other applications cannot play through this device at unity.
   using var renderer=new StrictExclusiveOut(device,false);
   renderer.Init(new SilenceProvider(DeviceFormats.Require(id,384000,32)));renderer.Play();Thread.Sleep(150);ExclusiveOwnership.Confirm(id);
   using(var lease=new DopVolumeLease(id)){lease.Activate();report["carrier_db"]=lease.CarrierDb;report["pre_packet_gain"]=lease.Gain;Thread.Sleep(1000);}
   report["mmcss"]=renderer.MmcssRegistered;report["output_underruns"]=renderer.Underruns;report["buffer_ms"]=renderer.BufferMilliseconds;
   float restored=device.AudioEndpointVolume.MasterVolumeLevel;report["restored_db"]=restored;
   if(Math.Abs(restored-original.Value)>.1)throw new IOException("Live endpoint volume did not restore");
   renderer.Stop();report["result"]="PASS";return 0;
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}
  finally{report["scope"]="Live endpoint volume round trip while an exclusive PCM stream emits only zero samples; no DoP packets or audible test signal";File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));}
 }
 internal static int Run(string reportPath){
  var cases=new List<string>();var report=new Dictionary<string,object>();string directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"dop-volume-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add(label);}
  string volume=Path.Combine(directory,"volume.txt"),transaction=Path.Combine(directory,"lease.json");var file=new DopFileVolume(volume);Process? owner=null;
  try{
   file.Db=-18;using(var lease=new DopVolumeLease(file,transaction,volume,true)){
    Check(file.Db==-18,"guard_ready_before_any_volume_change");Check(Math.Abs(lease.Gain-Math.Pow(10,-18d/20))<1e-12,"original_attenuation_moved_before_packing");
    lease.Activate();Check(file.Db==0,"carrier_unity_verified");double expected=.2*Math.Pow(10,-18d/20);var source=new DopGainProvider(new Constant(),()=>lease.Gain);var samples=new float[128];source.Read(samples,0,samples.Length);Check(samples.All(v=>Math.Abs(v-expected)<1e-8),"pre_packet_samples_preserve_listening_level");
   }
   Check(file.Db==-18&&!File.Exists(transaction),"normal_stop_restores_volume_and_removes_lease");
   file.Db=-21;using(var lease=new DopVolumeLease(new PartialFailure(volume),transaction,volume,true)){
    bool failed=false;try{lease.Activate();}catch(IOException){failed=true;}Check(failed&&file.Db==-21,"partial_unity_write_rolls_back_immediately");
   }
   file.Db=-12;using(var lease=new DopVolumeLease(file,transaction,volume,true)){lease.Activate();file.Db=-23;}
   Check(file.Db==-23,"explicit_external_volume_change_preserved_on_release");
   file.Db=-22;using(var lease=new DopVolumeLease(new PartialAndRestoreFailure(volume),transaction,volume,true)){
    try{lease.Activate();}catch(IOException){}
    var retry=Stopwatch.StartNew();while(File.Exists(transaction)&&retry.ElapsedMilliseconds<5000)Thread.Sleep(25);
    Check(!File.Exists(transaction)&&file.Db==-22,"independent_guard_recovers_failed_parent_restoration_while_parent_is_alive");
   }
   file.Db=-1;var partial=new DopVolumeState(volume,0,0,"partial",-19,true,true);Check(DopVolumeLease.Restore(file,partial)&&file.Db==-19,"crash_during_partial_driver_write_restores_original_attenuation");
   using var self=Process.GetCurrentProcess();var existing=new DopVolumeState(volume,self.Id,self.StartTime.ToUniversalTime().Ticks,"existing",-11,true);DopVolumeLease.Save(transaction,existing);
   bool refused=false;try{using var collision=new DopVolumeLease(file,transaction,volume,true);}catch(IOException){refused=true;}
   Check(refused&&DopVolumeLease.Load(transaction).Token=="existing","existing_recovery_record_never_overwritten_or_deleted");File.Delete(transaction);
   file.Db=-27;string ready=Path.Combine(directory,"owner.ready");owner=Process.Start(SelfProcess.StartInfo("--dop-volume-fake-owner",volume,transaction,ready))??throw new IOException("Could not start isolated test owner");
   var watch=Stopwatch.StartNew();while(!File.Exists(ready)&&!owner.HasExited&&watch.ElapsedMilliseconds<7000)Thread.Sleep(20);
   Check(File.Exists(ready)&&file.Db==0,"independent_guard_armed_for_owned_test_process");
   owner.Kill();owner.WaitForExit();watch.Restart();while(File.Exists(transaction)&&watch.ElapsedMilliseconds<5000)Thread.Sleep(25);
   Check(!File.Exists(transaction)&&file.Db==-27,"process_crash_restores_volume_without_parent_cleanup");
   Check(!File.Exists(transaction+".guard-error.txt"),"recovery_guard_completed_without_error");
   report["result"]="PASS";return 0;
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();return 1;}
  finally{if(owner!=null){if(!owner.HasExited){owner.Kill();owner.WaitForExit();}owner.Dispose();}report["cases"]=cases;report["scope"]="Disk-backed fake endpoint, including deliberate termination of an owned test child; no live audio/device volume changes";File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
 }
}
