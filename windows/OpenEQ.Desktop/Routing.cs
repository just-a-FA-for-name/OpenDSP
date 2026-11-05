using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace OpenEQ;
internal static class AudioEndpointControl {
 public static void Visibility(string id,bool visible){var policy=(IPolicyConfig)new PolicyConfigClient();try{Marshal.ThrowExceptionForHR(policy.SetEndpointVisibility(id,visible?1:0));}finally{Marshal.ReleaseComObject(policy);}}
}
[ComImport,Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]class PolicyConfigClient{}
[ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig {
 [PreserveSig]int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)]string id,out IntPtr format);
 [PreserveSig]int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)]string id,int @default,out IntPtr format);
 [PreserveSig]int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)]string id);
 [PreserveSig]int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr endpoint,IntPtr mix);
 [PreserveSig]int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)]string id,int @default,IntPtr period,IntPtr min);
 [PreserveSig]int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr period);
 [PreserveSig]int GetShareMode([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr mode);
 [PreserveSig]int SetShareMode([MarshalAs(UnmanagedType.LPWStr)]string id,IntPtr mode);
 [PreserveSig]int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.Bool)]bool fxStore,IntPtr key,IntPtr value);
 [PreserveSig]int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.Bool)]bool fxStore,IntPtr key,IntPtr value);
 [PreserveSig]int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)]string id,int role);
 [PreserveSig]int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)]string id,int visible);
}
public sealed class DefaultRoute:IDisposable {
 readonly Dictionary<Role,string> before=new();string? target;readonly string recovery;
 public DefaultRoute(){recovery=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","route-recovery.json");}
 public void Set(string id){if(File.Exists(recovery))throw new InvalidOperationException("发现上次未恢复的路由，请先恢复默认输出");using var e=new MMDeviceEnumerator();foreach(var role in new[]{Role.Console,Role.Multimedia,Role.Communications}){using var d=e.GetDefaultAudioEndpoint(DataFlow.Render,role);before[role]=d.ID;}
  target=id;Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);File.WriteAllText(recovery,System.Text.Json.JsonSerializer.Serialize(new RouteRecord{Target=id,Before=before.ToDictionary(x=>(int)x.Key,x=>x.Value)}));
  var policy=(IPolicyConfig)new PolicyConfigClient();try{foreach(var role in before.Keys)Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id,(int)role));}catch{Dispose();throw;}finally{Marshal.ReleaseComObject(policy);}
 }
 public void Dispose(){if(target==null)return;bool success=true;using var e=new MMDeviceEnumerator();var policy=(IPolicyConfig)new PolicyConfigClient();try{foreach(var item in before){try{using var current=e.GetDefaultAudioEndpoint(DataFlow.Render,item.Key);if(current.ID==target)Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(item.Value,(int)item.Key));}catch{success=false;}}if(success&&File.Exists(recovery))File.Delete(recovery);}finally{Marshal.ReleaseComObject(policy);target=null;}}
 public static string Recover(){var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","route-recovery.json");if(!File.Exists(path))return "没有待恢复的路由";var r=System.Text.Json.JsonSerializer.Deserialize<RouteRecord>(File.ReadAllText(path))??throw new Exception("恢复记录无效");using var e=new MMDeviceEnumerator();var policy=(IPolicyConfig)new PolicyConfigClient();try{foreach(var pair in r.Before){using var d=e.GetDefaultAudioEndpoint(DataFlow.Render,(Role)pair.Key);if(d.ID==r.Target)Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(pair.Value,pair.Key));}File.Delete(path);return "已恢复此前的默认输出";}finally{Marshal.ReleaseComObject(policy);}}
 public sealed class RouteRecord{public string Target{get;set;}="";public Dictionary<int,string> Before{get;set;}=new();}
}
