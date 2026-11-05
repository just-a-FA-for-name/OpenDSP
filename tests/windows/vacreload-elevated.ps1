$out='D:\AI workspace\OpenEQ\evidence\vacreload.txt'
function Log($m){$m|Out-File $out -Append -Encoding UTF8}
'=== run '+(Get-Date -Format o)|Out-File $out -Encoding UTF8
Get-Process vcctlpan -ErrorAction SilentlyContinue | Stop-Process -Force
$devs=Get-PnpDevice -FriendlyName '*Virtual Audio Cable*' -ErrorAction SilentlyContinue
foreach($d in $devs){ Log ('device: '+$d.FriendlyName+' | '+$d.InstanceId) }
foreach($d in $devs){ pnputil /disable-device $d.InstanceId 2>&1 | Out-Null }
Start-Sleep 3
foreach($d in $devs){ pnputil /enable-device $d.InstanceId 2>&1 | Out-Null }
Start-Sleep 5
$previous=@((Get-Service Audiosrv).DependentServices | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
try{Stop-Service Audiosrv -Force;Start-Service Audiosrv;foreach($s in $previous){Start-Service -Name $s -ErrorAction SilentlyContinue}}finally{if((Get-Service Audiosrv).Status -ne 'Running'){Start-Service Audiosrv}}
Log 'audio service restarted'
Start-Sleep 4
& 'D:\AI workspace\tools\dotnet8\dotnet.exe' 'D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.dll' --device-probe '{0.0.0.00000000}.{670f32e1-2444-46a2-a67a-f814ea969207}' 'D:\AI workspace\OpenEQ\evidence\vac-probe-r.json' | Out-Null
& 'D:\AI workspace\tools\dotnet8\dotnet.exe' 'D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.dll' --device-probe '{0.0.1.00000000}.{8b5cd7bd-3085-4218-9905-76bd6e06a121}' 'D:\AI workspace\OpenEQ\evidence\vac-probe-c.json' | Out-Null
Log ((Get-Content 'D:\AI workspace\OpenEQ\evidence\vac-probe-r.json' -Raw)+(Get-Content 'D:\AI workspace\OpenEQ\evidence\vac-probe-c.json' -Raw))
Log 'DONE'
