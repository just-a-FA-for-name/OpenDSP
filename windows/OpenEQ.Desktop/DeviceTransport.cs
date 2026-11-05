using System.Runtime.InteropServices;
using System.Text;
namespace OpenEQ;
public static class DeviceTransport {
 public static string Detect(string endpointId,string name){if(name.Contains("Streaming")||name.Contains("虚拟")||name.Contains("CABLE",StringComparison.OrdinalIgnoreCase)||name.Contains("Virtual",StringComparison.OrdinalIgnoreCase))return "虚拟";
  if(CM_Locate_DevNodeW(out var node,"SWD\\MMDEVAPI\\"+endpointId,0)!=0)return "系统";
  for(int n=0;n<8;n++){var text=new StringBuilder(1024);if(CM_Get_Device_IDW(node,text,text.Capacity,0)!=0)break;var id=text.ToString();if(id.StartsWith("BTH",StringComparison.OrdinalIgnoreCase))return "蓝牙";if(id.StartsWith("USB\\",StringComparison.OrdinalIgnoreCase))return "USB";if(CM_Get_Parent(out node,node,0)!=0)break;}return "系统";
 }
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern int CM_Locate_DevNodeW(out uint node,string id,uint flags);
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern int CM_Get_Device_IDW(uint node,StringBuilder buffer,int length,uint flags);
 [DllImport("cfgmgr32.dll")]static extern int CM_Get_Parent(out uint parent,uint node,uint flags);
}
