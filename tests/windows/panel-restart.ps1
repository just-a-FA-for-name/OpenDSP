Add-Type @'
using System.Runtime.InteropServices;
public class M4 {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags,uint dx,uint dy,uint dwData,System.UIntPtr dwExtraInfo);
}
'@
[M4]::SetCursorPos(821,454); Start-Sleep -Milliseconds 250
[M4]::mouse_event(0x02,0,0,0,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 80
[M4]::mouse_event(0x04,0,0,0,[System.UIntPtr]::Zero)
Start-Sleep -Seconds 2
Get-Process vcctlpan -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Start-Process 'C:\Program Files\Virtual Audio Cable\vcctlpan.exe'
Start-Sleep 5
'restarted'
