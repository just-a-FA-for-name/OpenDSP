using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace OpenEQ;
/// <summary>Callbacks never enumerate or dispose COM objects on the audio notification thread.</summary>
public sealed class DeviceMonitor:IMMNotificationClient,IDisposable {
 readonly MMDeviceEnumerator enumerator=new();public event Action<string>? Changed;
 public DeviceMonitor(){enumerator.RegisterEndpointNotificationCallback(this);}
 public void OnDeviceStateChanged(string id,DeviceState state)=>Changed?.Invoke("state");
 public void OnDeviceAdded(string id)=>Changed?.Invoke("added");
 public void OnDeviceRemoved(string id)=>Changed?.Invoke("removed");
 public void OnDefaultDeviceChanged(DataFlow flow,Role role,string id)=>Changed?.Invoke("default");
 public void OnPropertyValueChanged(string id,PropertyKey key)=>Changed?.Invoke("property");
 public void Dispose(){enumerator.UnregisterEndpointNotificationCallback(this);enumerator.Dispose();}
}
public sealed class DeviceSelection {
 public string? InputId{get;set;}public string? OutputId{get;set;}
 public string Signature{get;private set;}="";
 public bool Apply(IReadOnlyList<DeviceOption> devices,bool initial=false,string? defaultOutput=null){
  string signature=string.Join("\n",devices.OrderBy(d=>d.Id,StringComparer.OrdinalIgnoreCase).Select(d=>d.Id+"|"+d.Name+"|"+d.Transport));bool changed=signature!=Signature;Signature=signature;
  if(initial){
   // USB re-enumeration can replace an endpoint ID while the saved preference survives.
   // Only recover missing choices on launch; an unplug during playback still waits for reconnection.
   if(!InputAvailable(devices))InputId=null;
   if(!OutputAvailable(devices))OutputId=null;
   // 入口优先正经虚拟线缆（Hi-Fi Cable 系），其次其它虚拟入口；Steam/网易等娱乐转接只作最后兜底
   InputId??=devices.FirstOrDefault(d=>d.Name.Contains("Hi-Fi Cable Input",StringComparison.OrdinalIgnoreCase))?.Id
    ??devices.FirstOrDefault(d=>(d.Name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)||d.Name.Contains("虚拟"))&&!d.Name.Contains("Steam",StringComparison.OrdinalIgnoreCase))?.Id
    ??devices.FirstOrDefault(d=>d.Name.Contains("Steam",StringComparison.OrdinalIgnoreCase)||d.Name.Contains("网易"))?.Id;
   // 输出优先物理 USB 声卡，其次系统默认输出，绝不与入口相同
   OutputId??=devices.FirstOrDefault(d=>d.Transport=="USB"&&d.Id!=InputId)?.Id
    ??devices.FirstOrDefault(d=>d.Id==defaultOutput&&d.Id!=InputId)?.Id
    ??devices.FirstOrDefault(d=>d.Transport!="虚拟"&&d.Id!=InputId)?.Id;
  }return changed;
 }
 public bool InputAvailable(IEnumerable<DeviceOption> devices)=>devices.Any(d=>string.Equals(d.Id,InputId,StringComparison.OrdinalIgnoreCase));
 public bool OutputAvailable(IEnumerable<DeviceOption> devices)=>devices.Any(d=>string.Equals(d.Id,OutputId,StringComparison.OrdinalIgnoreCase));
}
