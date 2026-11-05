using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace OpenEQ;
/// <summary>Preserves Windows' own Listen-to-this-device pipeline while redirecting its render target.</summary>
public sealed class WindowsListen:IDisposable {
 static readonly Guid ListenKey=new("24dbb0fc-9311-4b3d-9cf0-18ff155639d4");
 readonly string input,output;readonly List<SavedTarget> saved=new();
 static string Recovery=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenEQ","listen-recovery.json");
 public int RedirectedCount=>saved.Count;
 public WindowsListen(string virtualInput,string physicalOutput){input=virtualInput;output=physicalOutput;}
 public static List<ListenInfo> ReadAll(){using var e=new MMDeviceEnumerator();var list=new List<ListenInfo>();foreach(var d in e.EnumerateAudioEndPoints(DataFlow.Capture,DeviceState.Active)){try{using var store=Store.Open(d.ID,false);var enabled=store.Get(1);var target=store.Get(0);list.Add(new ListenInfo(d.ID,d.FriendlyName,enabled.Number!=0,target.Text??"",enabled.Type,target.Type));}finally{d.Dispose();}}return list;}
 public void Refresh(IReadOnlyList<ListenInfo>? snapshot=null){foreach(var info in snapshot??ReadAll()){if(!info.Enabled||info.Target==input)continue;bool defaultTarget=string.IsNullOrEmpty(info.Target)||info.Target.Contains("Default",StringComparison.OrdinalIgnoreCase);if(info.Target!=output&&!defaultTarget)continue;
   using var store=Store.Open(info.Id,true);var prior=store.Get(0);
   // A background snapshot must not overwrite a subsequent user change.
   if(store.Get(1).Number==0||!string.Equals(prior.Text??"",info.Target,StringComparison.Ordinal))continue;
   saved.RemoveAll(s=>s.Id==info.Id);saved.Add(new SavedTarget{Id=info.Id,Target=prior});Persist();Rebind(store,new Value{Type=31,Text=input});
  }}
 void Persist(){Directory.CreateDirectory(Path.GetDirectoryName(Recovery)!);File.WriteAllText(Recovery,JsonSerializer.Serialize(new RecoveryData{Input=input,Targets=saved}));}
 public void Dispose(){if(saved.Count==0)return;Restore(new RecoveryData{Input=input,Targets=saved});saved.Clear();}
 public static void Recover(){if(!File.Exists(Recovery))return;var data=JsonSerializer.Deserialize<RecoveryData>(File.ReadAllText(Recovery))??throw new Exception("侦听恢复记录无效");Restore(data);}
 static void Restore(RecoveryData data){var failures=new List<SavedTarget>();foreach(var snapshot in data.Targets)try{using var store=Store.Open(snapshot.Id,true);var current=store.Get(0);if(current.Text==data.Input)Rebind(store,snapshot.Target);}catch{failures.Add(snapshot);}if(failures.Count==0){if(File.Exists(Recovery))File.Delete(Recovery);}else{data.Targets=failures;File.WriteAllText(Recovery,JsonSerializer.Serialize(data));}}
 // Windows may retain the old render stream after a target property update.
 // Recreate only the already-enabled native listening stream, preserving its final flag.
 static void Rebind(Store store,Value target){bool enabled=store.Get(1).Number!=0;if(enabled){store.Set(1,new Value{Type=11,Number=0});Thread.Sleep(200);}try{store.Set(0,target);}finally{if(enabled)store.Set(1,new Value{Type=11,Number=-1});}}
 public static void SetEnabled(string id,bool enabled){using var store=Store.Open(id,true);store.Set(1,new Value{Type=11,Number=enabled?-1:0});}
 public static void RestoreSettings(ListenInfo original){using var store=Store.Open(original.Id,true);store.Set(1,new Value{Type=original.EnableType,Number=original.Enabled?-1:0});store.Set(0,new Value{Type=original.TargetType,Text=original.Target});}
 public sealed record ListenInfo(string Id,string Name,bool Enabled,string Target,ushort EnableType,ushort TargetType);
 public sealed class Value{public ushort Type{get;set;}public string? Text{get;set;}public long Number{get;set;}}
 public sealed class SavedTarget{public string Id{get;set;}="";public Value Target{get;set;}=new();}
 public sealed class RecoveryData{public string Input{get;set;}="";public List<SavedTarget> Targets{get;set;}=new();}
 sealed class Store:IDisposable {
  readonly IListenStore store;readonly IListenDevice device;readonly IListenEnumerator enumerator;readonly string id;
  Store(IListenStore store,IListenDevice device,IListenEnumerator enumerator,string id){this.store=store;this.device=device;this.enumerator=enumerator;this.id=id;}
  public static Store Open(string id,bool write){var e=(IListenEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"),true)!)!;IListenDevice? d=null;try{Marshal.ThrowExceptionForHR(e.GetDevice(id,out d));Marshal.ThrowExceptionForHR(d.OpenPropertyStore(write?2:0,out var s));return new Store(s,d,e,id);}catch{if(d!=null)Marshal.ReleaseComObject(d);Marshal.ReleaseComObject(e);throw;}}
  public Value Get(int pid){var key=new ListenPropertyKey{Format=ListenKey,Id=pid};Marshal.ThrowExceptionForHR(store.GetValue(ref key,out var v));try{return new Value{Type=v.Type,Text=v.Type==31?Marshal.PtrToStringUni(v.Pointer):null,Number=v.Type==11?v.Bool:v.Type==19?v.UInt:0};}finally{PropVariantClear(ref v);}}
  public void Set(int pid,Value value){var key=new ListenPropertyKey{Format=ListenKey,Id=pid};var v=new ListenVariant{Type=value.Type};if(value.Type==31)v.Pointer=Marshal.StringToCoTaskMemUni(value.Text??"");else if(value.Type==11)v.Bool=(short)value.Number;else if(value.Type==19)v.UInt=(uint)value.Number;else if(value.Type!=0)throw new Exception("无法保留该 Windows 侦听属性类型 "+value.Type);IntPtr keyMemory=Marshal.AllocCoTaskMem(Marshal.SizeOf<ListenPropertyKey>()),valueMemory=Marshal.AllocCoTaskMem(24);IPolicyConfig? policy=null;try{Marshal.StructureToPtr(key,keyMemory,false);Marshal.StructureToPtr(v,valueMemory,false);policy=(IPolicyConfig)new PolicyConfigClient();Marshal.ThrowExceptionForHR(policy.SetPropertyValue(id,false,keyMemory,valueMemory));}finally{if(policy!=null)Marshal.ReleaseComObject(policy);Marshal.FreeCoTaskMem(keyMemory);Marshal.FreeCoTaskMem(valueMemory);PropVariantClear(ref v);}}
  public void Dispose(){Marshal.ReleaseComObject(store);Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(enumerator);}
 }
 [DllImport("ole32.dll")]static extern int PropVariantClear(ref ListenVariant value);
 [StructLayout(LayoutKind.Sequential)]struct ListenPropertyKey{public Guid Format;public int Id;}
 [StructLayout(LayoutKind.Explicit,Size=24)]struct ListenVariant{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;[FieldOffset(8)]public short Bool;[FieldOffset(8)]public uint UInt;}
 [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class ListenEnumerator{}
 [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IListenEnumerator{
  [PreserveSig]int EnumAudioEndpoints(int flow,int state,out IntPtr collection);[PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IListenDevice device);[PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IListenDevice device);
 }
 [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IListenDevice{
  [PreserveSig]int Activate(ref Guid iid,int context,IntPtr parameters,out IntPtr result);[PreserveSig]int OpenPropertyStore(int access,out IListenStore store);[PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetState(out int state);
 }
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IListenStore{
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out ListenPropertyKey key);[PreserveSig]int GetValue(ref ListenPropertyKey key,out ListenVariant value);[PreserveSig]int SetValue(ref ListenPropertyKey key,ref ListenVariant value);[PreserveSig]int Commit();
 }
}
