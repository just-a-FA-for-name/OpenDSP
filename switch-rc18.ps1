$old=Get-Process OpenDSP -ErrorAction SilentlyContinue
if($old){ & 'D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc16\OpenDSP.exe' --shutdown | Out-Null
  $w=0; while((Get-Process OpenDSP -ErrorAction SilentlyContinue) -and $w -lt 20){Start-Sleep -Milliseconds 500;$w++}
  Get-Process OpenDSP -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 1 }
Start-Process 'D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc18\OpenDSP.exe' -ArgumentList '--background','--start'
Start-Sleep -Seconds 6
'running=' + [bool](Get-Process OpenDSP -ErrorAction SilentlyContinue)
