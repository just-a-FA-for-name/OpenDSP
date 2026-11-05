using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
namespace OpenEQ;
internal static class ExclusiveOwnership {
 public const int DeviceInUse=unchecked((int)0x8889000A);
 public static int? SharedInitializationError(string id){using var en=new MMDeviceEnumerator();using var device=en.GetDevice(id);using var client=device.AudioClient;try{client.Initialize(AudioClientShareMode.Shared,AudioClientStreamFlags.None,1000000,0,client.MixFormat,Guid.Empty);return null;}catch(COMException e){return e.HResult;}}
 public static void Confirm(string id){int? error=SharedInitializationError(id);if(error!=DeviceInUse)throw new InvalidOperationException(error==null?"共享客户端仍能打开设备，未确认独占所有权":$"共享竞争返回 0x{error:X8}，不能作为独占成功证据");}
}
