using System.Runtime.InteropServices;
using System.Text;
namespace OpenEQ;
internal static class AudioRestartGuard {
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Locate_DevNodeW")]static extern uint Locate(out uint node,string instance,uint flags);
 [DllImport("cfgmgr32.dll")]static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
 [DllImport("cfgmgr32.dll")]static extern uint CM_Get_Parent(out uint parent,uint node,uint flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode,EntryPoint="CM_Get_Device_IDW")]static extern uint DeviceId(uint node,StringBuilder buffer,uint length,uint flags);
 internal sealed record Node(string Instance,uint Status,uint Problem);
 internal static bool NeedsRestart(Node node)=>(node.Status&0x100)!=0||node.Problem==14;
 public static IReadOnlyList<Node> Read(string endpoint){NativeApo.EndpointGuid(endpoint);uint result=Locate(out uint node,@"SWD\MMDEVAPI\"+endpoint,0);if(result!=0)throw new InvalidOperationException($"无法核对音频设备状态（CM {result}），已阻止修改系统音频");var nodes=new List<Node>();for(int n=0;n<16;n++){if(CM_Get_DevNode_Status(out uint status,out uint problem,node,0)!=0)throw new IOException("无法核对音频驱动状态");var name=new StringBuilder(1024);DeviceId(node,name,1024,0);nodes.Add(new(name.ToString(),status,problem));if(CM_Get_Parent(out uint parent,node,0)!=0)break;node=parent;}return nodes;}
 public static void EnsureReady(string endpoint){if(Read(endpoint).Any(NeedsRestart))throw new InvalidOperationException("Windows 报告此音频设备或 USB 上级设备需要重启。已停止修改格式或安装音效；请先保存内容并重启电脑");}
}
