using System.Runtime.InteropServices;
using System.Text.Json;
namespace OpenEQ;
/// <summary>
/// 修复端点 FxProperties 中的损坏值（例如畸形 GUID 字符串会导致引擎在 GetMixFormat/AudioClient
/// 激活时抛 CO_E_CLASSSTRING“无效的类字符串”）。写入走 IPolicyConfig.SetPropertyValue ——
/// 与 Windows“声音增强”设置同一条经音频服务的免提权通道，不需要改注册表 ACL。
/// </summary>
internal static class FxFix {
 static readonly Guid FxKey=new("d04e05a6-594b-4fb6-a80d-01af5eed7d1d");      // MFX/EFX CLSID
 static readonly Guid ModeKey=new("d3993a3f-99c2-4402-b5ec-a92a0367664b");   // 模式 CLSID 列表
 static readonly Guid ConfigKey=new("73467431-da1e-4eb4-af89-f615a18635dd"); // 端点配置
 static readonly string[] StandardModes={"{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}","{9E90EA20-B493-4FD1-A1A8-7E1361A956CF}","{4780004E-7133-41D8-8C74-660DADD2C0EE}"};
 static (string Name,PropKey Key)[] Targets=>new[]{("mfx",new PropKey(FxKey,6)),("efx",new PropKey(FxKey,7)),("modes",new PropKey(ModeKey,6)),("modes_legacy",new PropKey(ModeKey,7)),("config",new PropKey(ConfigKey,1))};
 public static int Run(string id,string reportPath){var report=new Dictionary<string,object>();IPolicyConfig? policy=null;
  try{
   policy=(IPolicyConfig)new PolicyConfigClient();
   var actions=new Dictionary<string,object>();
   foreach(var t in Targets){if(t.Name=="modes"){actions["modes_fx"]="0x"+((uint)WriteModes(policy,id)).ToString("X8");continue;}foreach(bool fx in new[]{true,false})actions[t.Name+(fx?"_fx":"_props")]="0x"+((uint)WriteEmpty(policy,id,t.Key,fx)).ToString("X8");}
   report["actions"]=actions;report["result"]="PASS";report["note"]="registry verify: tests\\windows\\fx-audit.ps1; device health: --device-probe";
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();}
  finally{if(policy!=null)Marshal.ReleaseComObject(policy);}
  File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  return report.ContainsKey("exception")?1:0;
 }
 static readonly Guid DevFmt=new("f19f064d-082c-4e27-bc73-6882a1bb8e4c"),EngFmt=new("3d6e1656-2e50-4c4c-8d85-d0acae3c6c68"),EngFmt2=new("e4870e26-3cc5-4cd2-ba46-ca0a9a70ed04"),EngFmt3=new("624f56de-fd24-473e-814a-de40aacaed16");
 /// <summary>仅重写模式 CLSID 列表为标准 3 项（清除老版本写入器留下的畸形 GUID），不动 MFX/EFX 绑定。</summary>
 public static int FixModes(string id,string reportPath){IPolicyConfig? policy=null;
  try{policy=(IPolicyConfig)new PolicyConfigClient();var hr=WriteModes(policy,id);File.WriteAllText(reportPath,"hr=0x"+((uint)hr).ToString("X8"));return hr==0?0:1;}
  catch(Exception ex){File.WriteAllText(reportPath,ex.ToString());return 1;}
  finally{if(policy!=null)Marshal.ReleaseComObject(policy);}}
 public static int SetUint(string id,bool fx,Guid format,int pid,uint value,string reportPath){IPolicyConfig? policy=null;
  try{policy=(IPolicyConfig)new PolicyConfigClient();var key=new PropKey(format,pid);var km=Marshal.AllocCoTaskMem(20);var vm=Marshal.AllocCoTaskMem(24);
   try{Marshal.StructureToPtr(key,km,false);var v=new PropVariant{Type=19,Count=value};Marshal.StructureToPtr(v,vm,false);var hr=policy.SetPropertyValue(id,fx,km,vm);File.WriteAllText(reportPath,"hr=0x"+((uint)hr).ToString("X8"));return hr==0?0:1;}
   finally{Marshal.FreeCoTaskMem(km);Marshal.FreeCoTaskMem(vm);}
  }catch(Exception ex){File.WriteAllText(reportPath,ex.ToString());return 1;}
  finally{if(policy!=null)Marshal.ReleaseComObject(policy);}}
 /// <summary>清掉端点上被写坏的格式记录（如超出驱动能力的 384k），引擎会向驱动重新取默认格式。</summary>
 public static int ClearFormat(string id,string reportPath){var report=new Dictionary<string,object>();IPolicyConfig? policy=null;
  try{policy=(IPolicyConfig)new PolicyConfigClient();var actions=new Dictionary<string,object>();
   foreach(var k in new[]{new PropKey(DevFmt,0),new PropKey(EngFmt,2),new PropKey(EngFmt,3),new PropKey(EngFmt2,0),new PropKey(EngFmt2,1),new PropKey(EngFmt3,3)})actions[k.Format+"#"+k.Id]="0x"+((uint)WriteEmpty(policy,id,k,false)).ToString("X8");
   report["actions"]=actions;report["result"]="PASS";
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();}
  finally{if(policy!=null)Marshal.ReleaseComObject(policy);}
  File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  return report.ContainsKey("exception")?1:0;}
 /// <summary>把目标格式（WAVEFORMATEXTENSIBLE）作为 VT_BLOB 写入端点的格式记录，引擎重启后按此格式建流。</summary>
 public static int SetFormatBlob(string id,int rate,int bits,bool isFloat,string reportPath){var report=new Dictionary<string,object>();IPolicyConfig? policy=null;
  try{policy=(IPolicyConfig)new PolicyConfigClient();var actions=new Dictionary<string,object>();
   var data=new MemoryStream();using(var w=new BinaryWriter(data,System.Text.Encoding.UTF8,true)){w.Write((ushort)0xfffe);w.Write((ushort)2);w.Write(rate);w.Write(rate*2*bits/8);w.Write((ushort)(2*bits/8));w.Write((ushort)bits);w.Write((ushort)22);w.Write((ushort)bits);w.Write(3);w.Write((isFloat?new Guid("00000003-0000-0010-8000-00aa00389b71"):new Guid("00000001-0000-0010-8000-00aa00389b71")).ToByteArray());}
   var bytes=data.ToArray();var payload=Marshal.AllocCoTaskMem(bytes.Length);Marshal.Copy(bytes,0,payload,bytes.Length);
   foreach(var k in new[]{new PropKey(DevFmt,0),new PropKey(EngFmt,2),new PropKey(EngFmt,3),new PropKey(EngFmt2,0),new PropKey(EngFmt2,1),new PropKey(EngFmt3,3)}){
    var km=Marshal.AllocCoTaskMem(20);var vm=Marshal.AllocCoTaskMem(24);
    try{Marshal.StructureToPtr(k,km,false);var v=new PropVariant{Type=65,Count=(uint)bytes.Length,Array=payload};Marshal.StructureToPtr(v,vm,false);actions[k.Format+"#"+k.Id]="0x"+((uint)policy.SetPropertyValue(id,false,km,vm)).ToString("X8");}
    finally{Marshal.FreeCoTaskMem(km);Marshal.FreeCoTaskMem(vm);}
   }
   report["actions"]=actions;report["result"]="PASS";
  }catch(Exception ex){report["result"]="FAIL";report["exception"]=ex.ToString();}
  finally{if(policy!=null)Marshal.ReleaseComObject(policy);}
  File.WriteAllText(reportPath,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
  return report.ContainsKey("exception")?1:0;}
 static object? Describe(IPolicyConfig policy,string id,PropKey key,bool fx){var hr=Get(policy,id,key,fx,out var v);if(hr!=0)return "hr=0x"+((uint)hr).ToString("X8");
  try{return v.Type switch{0=>null,31=>Marshal.PtrToStringUni(v.Pointer),0x101F=>ReadMulti(v),_=>"vt=0x"+v.Type.ToString("X")};}finally{PropVariantClear(ref v);}}
 static string[] ReadMulti(PropVariant v){var n=(int)v.Count;var list=new string[n];for(int i=0;i<n;i++)list[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(v.Array,i*8))??"";
  if(!IsCanonicalGuidList(list))return list.Select(s=>"<bad>"+s+"</bad>").ToArray();return list;}
 // 畸形 GUID（引擎 CLSIDFromString 会失败）判定：带花括号的必须能整体解析为 Guid
 static bool IsCanonicalGuidList(string[] list){foreach(var s in list){if(s.StartsWith("{")&&s.EndsWith("}")&&!Guid.TryParse(s,out _))return false;}return true;}
 static int Get(IPolicyConfig policy,string id,PropKey key,bool fx,out PropVariant value){var km=Marshal.AllocCoTaskMem(20);var vm=Marshal.AllocCoTaskMem(24);try{Marshal.StructureToPtr(key,km,false);Marshal.WriteInt32(vm,0);var hr=policy.GetPropertyValue(id,fx,km,vm);value=Marshal.PtrToStructure<PropVariant>(vm);return hr;}finally{Marshal.FreeCoTaskMem(km);Marshal.FreeCoTaskMem(vm);}}
 static int WriteEmpty(IPolicyConfig policy,string id,PropKey key,bool fx){var km=Marshal.AllocCoTaskMem(20);var vm=Marshal.AllocCoTaskMem(24);try{Marshal.StructureToPtr(key,km,false);var v=new PropVariant{Type=0};Marshal.StructureToPtr(v,vm,false);return policy.SetPropertyValue(id,fx,km,vm);}finally{Marshal.FreeCoTaskMem(km);Marshal.FreeCoTaskMem(vm);}}
 static int WriteModes(IPolicyConfig policy,string id){var key=new PropKey(ModeKey,6);var km=Marshal.AllocCoTaskMem(20);var vm=Marshal.AllocCoTaskMem(24);IntPtr array=IntPtr.Zero;try{
   Marshal.StructureToPtr(key,km,false);
   var strings=StandardModes.Select(Marshal.StringToCoTaskMemUni).ToArray();array=Marshal.AllocCoTaskMem(strings.Length*8);Marshal.Copy(strings,0,array,strings.Length);
   var v=new PropVariant{Type=0x101F,Count=(uint)strings.Length,Array=array};Marshal.StructureToPtr(v,vm,false);
   return policy.SetPropertyValue(id,true,km,vm);
  }finally{Marshal.FreeCoTaskMem(km);Marshal.FreeCoTaskMem(vm);/* 字符串与数组由 PropVariantClear/COM 释放 */}}
 [DllImport("ole32.dll")]static extern int PropVariantClear(ref PropVariant value);
 [StructLayout(LayoutKind.Explicit,Size=24)]struct PropVariant{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Pointer;[FieldOffset(8)]public uint Count;[FieldOffset(16)]public IntPtr Array;}
 [StructLayout(LayoutKind.Sequential)]struct PropKey{public Guid Format;public int Id;public PropKey(Guid format,int id){Format=format;Id=id;}}
}
