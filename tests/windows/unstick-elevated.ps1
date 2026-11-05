$out='D:\AI workspace\OpenEQ\evidence\unstick.txt'
function Log($m){ $m | Out-File $out -Append -Encoding UTF8 }
"=== run "+(Get-Date -Format o) | Out-File $out -Encoding UTF8
# 1) 重启音频服务，解开被非法格式卡住的端点状态（一次性，与应用内修复同一机制）
$previous=@((Get-Service Audiosrv).DependentServices | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
try{Stop-Service Audiosrv -Force;Start-Service Audiosrv;foreach($n in $previous){Start-Service -Name $n -ErrorAction SilentlyContinue}}finally{if((Get-Service Audiosrv).Status -ne 'Running'){Start-Service Audiosrv}}
Log 'audio service restarted'
Start-Sleep 3
# 2) 两端格式对齐到驱动原生 44100/32（线缆传送带率；384k 满血在 DAC 输出端）
$dotnet='D:\AI workspace\tools\dotnet8\dotnet.exe'
$dll='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.dll'
& $dotnet $dll --device-format-raw 'D:\AI workspace\OpenEQ\evidence\fmt-r.json' '{0.0.0.00000000}.{ff3eb428-239e-43e5-9d0b-496440ec6d0b}' 44100 32
Log ("render hr="+ (Get-Content 'D:\AI workspace\OpenEQ\evidence\fmt-r.json' -Raw -ErrorAction SilentlyContinue))
& $dotnet $dll --device-format-raw 'D:\AI workspace\OpenEQ\evidence\fmt-c.json' '{0.0.1.00000000}.{e5e45419-3510-4fc4-9868-b0c7e6b71934}' 44100 32
Log ("capture hr="+ (Get-Content 'D:\AI workspace\OpenEQ\evidence\fmt-c.json' -Raw -ErrorAction SilentlyContinue))
# 3) 校验
& $dotnet $dll --device-probe '{0.0.0.00000000}.{ff3eb428-239e-43e5-9d0b-496440ec6d0b}' 'D:\AI workspace\OpenEQ\evidence\probe-r.json'
& $dotnet $dll --device-probe '{0.0.1.00000000}.{e5e45419-3510-4fc4-9868-b0c7e6b71934}' 'D:\AI workspace\OpenEQ\evidence\probe-c.json'
Log 'DONE'
