Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System.Runtime.InteropServices;
public class Mouse {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags,uint dx,uint dy,uint dwData,System.UIntPtr dwExtraInfo);
}
'@
[Mouse]::SetCursorPos(633,326)
Start-Sleep -Milliseconds 300
[Mouse]::mouse_event(0x02,0,0,0,[System.UIntPtr]::Zero)
Start-Sleep -Milliseconds 80
[Mouse]::mouse_event(0x04,0,0,0,[System.UIntPtr]::Zero)
Start-Sleep -Seconds 2
reg query 'HKLM\SOFTWARE\EuMus Design\Virtual Audio Cable\4\Cable 1' 2>&1
reg query 'HKLM\SYSTEM\CurrentControlSet\Services\VirtualAudioCable_83ed7f0e\Parameters\Cable 1' 2>&1
