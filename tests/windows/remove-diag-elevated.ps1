$out='D:\AI workspace\OpenEQ\evidence\remove-diag.txt'
function Log($m){ $m | Out-File $out -Append -Encoding UTF8 }
"=== run "+(Get-Date -Format o) | Out-File $out -Encoding UTF8
Log ("user="+(whoami)+" 64bit="+[Environment]::Is64BitProcess+" elevated="+([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
$keys=@('{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6','{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7','{d3993a3f-99c2-4402-b5ec-a92a0367664b},6','{d3993a3f-99c2-4402-b5ec-a92a0367664b},7','{73467431-da1e-4eb4-af89-f615a18635dd},1')
function Dump($g,$tag){
  $fx="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$g\FxProperties"
  $found=@()
  foreach($n in $keys){
    $v=(Get-ItemProperty -Path $fx -Name $n -ErrorAction SilentlyContinue).$n
    if($null -ne $v){$found+=($n+' => '+(($v|ForEach-Object{$_}) -join ' | '))}
  }
  Log ("read[$tag] "+$g+" remaining="+$found.Count)
  foreach($x in $found){Log ("   "+$x)}
}
function RemoveAll($g){
  $fx="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$g\FxProperties"
  $props="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$g\Properties"
  foreach($n in $keys){
    try{Remove-ItemProperty -Path $fx -Name $n -ErrorAction Stop;Log ("removed fx "+$n)}catch{Log ("FAILED fx "+$n+" : "+$_.Exception.Message)}
  }
  try{Remove-ItemProperty -Path $props -Name '{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5' -ErrorAction Stop;Log "removed props {1da5d803},5"}catch{Log ("FAILED props : "+$_.Exception.Message)}
}
$cable='{ff3eb428-239e-43e5-9d0b-496440ec6d0b}'
$steam='{a74a00a1-c96d-4f20-b2be-070d9b286fd7}'
Dump $cable 'before'
RemoveAll $cable
RemoveAll $steam
Dump $cable 'after-remove'
Start-Sleep 3
Dump $cable 'after-3s'
$previous=@((Get-Service Audiosrv).DependentServices | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
try{Stop-Service Audiosrv -Force;Start-Service Audiosrv;foreach($n in $previous){Start-Service -Name $n -ErrorAction SilentlyContinue}}finally{if((Get-Service Audiosrv).Status -ne 'Running'){Start-Service Audiosrv}}
Start-Sleep 3
Dump $cable 'after-service-restart'
Log 'DONE'
