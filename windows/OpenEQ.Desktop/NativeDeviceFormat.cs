using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;
namespace OpenEQ;
internal static class NativeDeviceFormat {
 internal sealed class Backend(string id):INativeFormatBackend,IDisposable {
  readonly IPolicyConfig policy=(IPolicyConfig)new PolicyConfigClient();
  internal static byte[] CopyFormat(IntPtr pointer){int extra=(ushort)Marshal.ReadInt16(pointer,16);if(extra>4096)throw new InvalidDataException("音频格式数据过大");var data=new byte[18+extra];Marshal.Copy(pointer,data,0,data.Length);return data;}
  internal static byte[] Encode(WaveFormat format){IntPtr p=WaveFormat.MarshalToPtr(format);try{return CopyFormat(p);}finally{Marshal.FreeHGlobal(p);}}
  public NativeFormatState Read(){IntPtr endpoint=IntPtr.Zero,mix=IntPtr.Zero;try{Marshal.ThrowExceptionForHR(policy.GetDeviceFormat(id,0,out endpoint));Marshal.ThrowExceptionForHR(policy.GetMixFormat(id,out mix));return new(CopyFormat(endpoint),CopyFormat(mix));}finally{if(endpoint!=IntPtr.Zero)Marshal.FreeCoTaskMem(endpoint);if(mix!=IntPtr.Zero)Marshal.FreeCoTaskMem(mix);}}
  public void Write(NativeFormatState state){state.Validate();IntPtr endpoint=Marshal.AllocHGlobal(state.Endpoint.Length),mix=IntPtr.Zero;try{mix=Marshal.AllocHGlobal(state.Mix.Length);Marshal.Copy(state.Endpoint,0,endpoint,state.Endpoint.Length);Marshal.Copy(state.Mix,0,mix,state.Mix.Length);Marshal.ThrowExceptionForHR(policy.SetDeviceFormat(id,endpoint,mix));}finally{Marshal.FreeHGlobal(endpoint);if(mix!=IntPtr.Zero)Marshal.FreeHGlobal(mix);}}
  public void Dispose()=>Marshal.ReleaseComObject(policy);
 }
 static string BackupPath(string id)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","device-format-backups",NativeApo.EndpointGuid(id)+".json");
 public static NativeFormatState Read(string id){using var backend=new Backend(id);return backend.Read();}
 public static object Set(string id,int rate,int bits){AudioRestartGuard.EnsureReady(id);AudioActivityGuard.EnsureIdle(id);var supported=DeviceFormats.Require(id,rate,bits);using var backend=new Backend(id);var before=backend.Read();var desired=new NativeFormatState(Backend.Encode(supported),Backend.Encode(new WaveFormatExtensible(rate,32,2)));var actual=new NativeFormatTransaction(backend,BackupPath(id),id).Apply(desired);return new{before_device=NativeFormatState.Describe(before.Endpoint),before_mix=NativeFormatState.Describe(before.Mix),device=NativeFormatState.Describe(actual.Endpoint),mix=NativeFormatState.Describe(actual.Mix),backup=BackupPath(id),scope="Format readback only; acoustic output has not been validated"};}
 public static object Restore(string id){AudioRestartGuard.EnsureReady(id);AudioActivityGuard.EnsureIdle(id);using var backend=new Backend(id);var actual=new NativeFormatTransaction(backend,BackupPath(id),id).Restore();return new{device=NativeFormatState.Describe(actual.Endpoint),mix=NativeFormatState.Describe(actual.Mix)};}
 public static int Run(string path,string id,int rate,int bits){try{File.WriteAllText(path,JsonSerializer.Serialize(new{result="PASS",format=Set(id,rate,bits)},new JsonSerializerOptions{WriteIndented=true}));return 0;}catch(Exception e){File.WriteAllText(path,JsonSerializer.Serialize(new{result="FAIL",exception=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}}
 public static int RestoreCheck(string path,string id){try{File.WriteAllText(path,JsonSerializer.Serialize(new{result="PASS",restore=Restore(id),scope="Real device restore through the same NativeDeviceFormat.Restore the More menu calls; readback verified"},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));return 0;}catch(Exception e){File.WriteAllText(path,JsonSerializer.Serialize(new{result="FAIL",exception=e.ToString()},new JsonSerializerOptions{WriteIndented=true}));return 1;}}
}

