Add-Type @'
using System.Runtime.InteropServices;
public class Mouse3 {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags,uint dx,uint dy,uint dwData,System.UIntPtr dwExtraInfo);
}
'@
[Mouse3]::SetCursorPos(633,326)
Start-Sleep -Milliseconds 400
[Mouse3]::mouse_event(0x02,0,0,0,[System.UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[Mouse3]::mouse_event(0x04,0,0,0,[System.UIntPtr]::Zero)
Start-Sleep -Seconds 3
'clicked'
