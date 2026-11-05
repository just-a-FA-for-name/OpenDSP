$out='D:\AI workspace\OpenEQ\evidence\fixfmt.txt'
function Log($m){ $m | Out-File $out -Append -Encoding UTF8 }
"=== run "+(Get-Date -Format o) | Out-File $out -Encoding UTF8
Get-Process dotnet -ErrorAction SilentlyContinue | Where-Object {$_.Path -like '*tools\dotnet8*'} | Stop-Process -Force -ErrorAction SilentlyContinue
# 1) 删除渲染端点上被写坏的格式记录（384k 超出线缆驱动上限），让引擎重新向驱动取默认格式
$props='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{ff3eb428-239e-43e5-9d0b-496440ec6d0b}\Properties'
foreach($n in @('{f19f064d-082c-4e27-bc73-6882a1bb8e4c},0','{3d6e1656-2e50-4c4c-8d85-d0acae3c6c68},2','{3d6e1656-2e50-4c4c-8d85-d0acae3c6c68},3','{e4870e26-3cc5-4cd2-ba46-ca0a9a70ed04},0','{e4870e26-3cc5-4cd2-ba46-ca0a9a70ed04},1','{624f56de-fd24-473e-814a-de40aacaed16},3','{3d6e1656-2e50-4c4c-8d85-d0acae3c6c68},6')){
  try{Remove-ItemProperty -Path $props -Name $n -ErrorAction Stop;Log ("deleted "+$n)}catch{Log ("skip "+$n+" : "+$_.Exception.Message)}
}
# 2) 重启音频服务让引擎重建端点
$previous=@((Get-Service Audiosrv).DependentServices | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
try{Stop-Service Audiosrv -Force;Start-Service Audiosrv;foreach($n in $previous){Start-Service -Name $n -ErrorAction SilentlyContinue}}finally{if((Get-Service Audiosrv).Status -ne 'Running'){Start-Service Audiosrv}}
Log 'audio service restarted'
Start-Sleep 4
# 3) 校验
$dotnet='D:\AI workspace\tools\dotnet8\dotnet.exe'
$dll='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.dll'
& $dotnet $dll --device-probe '{0.0.0.00000000}.{ff3eb428-239e-43e5-9d0b-496440ec6d0b}' 'D:\AI workspace\OpenEQ\evidence\probe-r.json' | Out-Null
& $dotnet $dll --device-probe '{0.0.1.00000000}.{e5e45419-3510-4fc4-9868-b0c7e6b71934}' 'D:\AI workspace\OpenEQ\evidence\probe-c.json' | Out-Null
Log ((Get-Content 'D:\AI workspace\OpenEQ\evidence\probe-r.json' -Raw) + ' || ' + (Get-Content 'D:\AI workspace\OpenEQ\evidence\probe-c.json' -Raw))
Log 'DONE'
