$out='D:\AI workspace\OpenEQ\evidence\redev.txt'
function Log($m){$m|Out-File $out -Append -Encoding UTF8}
'=== run '+(Get-Date -Format o)|Out-File $out -Encoding UTF8
pnputil /remove-device 'ROOT\MEDIA\0002' 2>&1 | Out-File $out -Append -Encoding UTF8
Start-Sleep 2
pnputil /scan-devices 2>&1 | Out-File $out -Append -Encoding UTF8
Start-Sleep 6
$previous=@((Get-Service Audiosrv).DependentServices | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
try{Stop-Service Audiosrv -Force;Start-Service Audiosrv;foreach($s in $previous){Start-Service -Name $s -ErrorAction SilentlyContinue}}finally{if((Get-Service Audiosrv).Status -ne 'Running'){Start-Service Audiosrv}}
Log 'audio service restarted'
Start-Sleep 4
& 'D:\AI workspace\tools\dotnet8\dotnet.exe' 'D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.dll' --device-probe '{0.0.0.00000000}.{ff3eb428-239e-43e5-9d0b-496440ec6d0b}' 'D:\AI workspace\OpenEQ\evidence\probe-r.json' | Out-Null
& 'D:\AI workspace\tools\dotnet8\dotnet.exe' 'D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.dll' --device-probe '{0.0.1.00000000}.{e5e45419-3510-4fc4-9868-b0c7e6b71934}' 'D:\AI workspace\OpenEQ\evidence\probe-c.json' | Out-Null
Log ((Get-Content 'D:\AI workspace\OpenEQ\evidence\probe-r.json' -Raw)+(Get-Content 'D:\AI workspace\OpenEQ\evidence\probe-c.json' -Raw))
Log 'DONE'
