$ErrorActionPreference='Stop'
function Restore-Ep([string]$g){
  $fx="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$g\FxProperties"
  $props="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$g\Properties"
  foreach($n in @('{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6','{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7','{d3993a3f-99c2-4402-b5ec-a92a0367664b},6','{d3993a3f-99c2-4402-b5ec-a92a0367664b},7','{73467431-da1e-4eb4-af89-f615a18635dd},1')){
    Remove-ItemProperty -Path $fx -Name $n -ErrorAction SilentlyContinue
  }
  Remove-ItemProperty -Path $props -Name '{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5' -ErrorAction SilentlyContinue
  Write-Output "restored $g"
}
Restore-Ep '{ff3eb428-239e-43e5-9d0b-496440ec6d0b}'
Restore-Ep '{a74a00a1-c96d-4f20-b2be-070d9b286fd7}'
# 一次性刷新音频服务（与应用内“APO 安装/恢复”同一机制），让端点音效注册重新读取
$previous=@((Get-Service Audiosrv).DependentServices | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
try{
  Stop-Service Audiosrv -Force
  Start-Service Audiosrv
  foreach($name in $previous){ Start-Service -Name $name -ErrorAction SilentlyContinue }
}finally{
  if((Get-Service Audiosrv).Status -ne 'Running'){ Start-Service Audiosrv }
}
'DONE '+(Get-Date -Format o) | Out-File 'D:\AI workspace\OpenEQ\evidence\repair-done.txt' -Encoding UTF8
