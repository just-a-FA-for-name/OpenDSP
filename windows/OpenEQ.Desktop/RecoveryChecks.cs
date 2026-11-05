using System.Runtime.InteropServices;
namespace OpenEQ;
internal static class RecoveryChecks {
 internal static object Run(){
  void Check(bool ok,string label){if(!ok)throw new Exception(label);}
  var devices=new[]{new DeviceOption("entry","Hi-Fi Cable Input","虚拟"),new DeviceOption("new-usb","USB DAC","USB")};
  var selection=new DeviceSelection{InputId="removed-entry",OutputId="removed-usb"};
  selection.Apply(devices,true,"new-usb");Check(selection.InputId=="entry"&&selection.OutputId=="new-usb","Recover obsolete endpoint IDs on launch");
  selection.Apply(new[]{devices[0]});Check(selection.OutputId=="new-usb","Keep disconnected output preference during playback");
  int attempts=0,releases=0,waits=0;
  ExclusiveOpenRetry.Run(()=>{if(++attempts<3)throw new COMException("Device busy",unchecked((int)0x8889000A));},()=>releases++,_=>waits++);
  Check(attempts==3&&releases==2&&waits==2,"Retry transient driver release only");
  attempts=0;try{ExclusiveOpenRetry.Run(()=>{attempts++;throw new COMException("Unsupported format",unchecked((int)0x88890008));},wait:_=>{});throw new Exception("Expected unsupported format");}catch(COMException ex){Check(attempts==1&&ex.HResult==unchecked((int)0x88890008),"Preserve unsupported format");}
  attempts=0;try{ExclusiveOpenRetry.Run(()=>{attempts++;throw new COMException("Busy",unchecked((int)0x8889000A));},wait:_=>{});throw new Exception("Expected occupied endpoint");}catch(COMException){Check(attempts==5,"Bound device busy retries");}
  string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".opendsp-abi-test");
  try{var data=new byte[4096];void Write(int offset,byte[] value)=>value.CopyTo(data,offset);
   Write(1448,BitConverter.GetBytes(123456789L));Write(1456,BitConverter.GetBytes(1.25));Write(1464,BitConverter.GetBytes(2.5));Write(1472,BitConverter.GetBytes(3.75));Write(1480,BitConverter.GetBytes(5d));Write(1488,BitConverter.GetBytes(384000u));Write(1504,BitConverter.GetBytes(7L));
   File.WriteAllBytes(file,data);using var reader=new NativeTelemetryReader(file);
   Check(reader.Frames==123456789&&reader.Rate==384000&&reader.Clipped==7&&reader.InputEnergy==3.75&&reader.OutputEnergy==8.75,"Reader agrees with native ABI v2 layout");
  }finally{File.Delete(file);}
  return new{endpoint_recovery="PASS",exclusive_release_retry="PASS",native_abi_v2_reader="PASS"};
 }
}
