Add-Type @'
using System.Runtime.InteropServices;
public class M5 {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags,uint dx,uint dy,uint dwData,System.UIntPtr dwExtraInfo);
 [DllImport("user32.dll")] public static extern void keybd_event(byte bVk,byte bScan,uint dwFlags,System.UIntPtr dwExtraInfo);
}
'@
function Click($x,$y){ [M5]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 250; [M5]::mouse_event(0x02,0,0,0,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [M5]::mouse_event(0x04,0,0,0,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 250 }
function SetField($x,$y,$text){ Click $x $y; [M5]::keybd_event(0x11,0,0,[System.UIntPtr]::Zero); [M5]::keybd_event(0x41,0,0,[System.UIntPtr]::Zero); [M5]::keybd_event(0x41,0,2,[System.UIntPtr]::Zero); [M5]::keybd_event(0x11,0,2,[System.UIntPtr]::Zero); Start-Sleep -Milliseconds 150; [System.Windows.Forms.SendKeys]::SendWait($text); Start-Sleep -Milliseconds 200 }
Add-Type -AssemblyName System.Windows.Forms
SetField 748 293 '384000'
SetField 846 293 '32'
SetField 697 293 '44100'
SetField 797 293 '16'
Click 1013 368
Start-Sleep -Seconds 3
'done'
