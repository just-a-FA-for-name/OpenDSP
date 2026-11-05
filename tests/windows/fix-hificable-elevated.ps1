$ErrorActionPreference='Continue'
$out='D:\AI workspace\OpenEQ\evidence\hificable-fix.txt'
"start "+(Get-Date -Format o) | Out-File $out -Encoding UTF8
$devs=Get-PnpDevice -FriendlyName '*Hi-Fi Cable*' -ErrorAction SilentlyContinue
foreach($d in $devs){ "device: "+$d.FriendlyName+" | "+$d.InstanceId+" | status="+$d.Status | Out-File $out -Append }
foreach($d in $devs){
  "disable "+$d.InstanceId | Out-File $out -Append
  pnputil /disable-device $d.InstanceId 2>&1 | Out-File $out -Append
}
Start-Sleep 3
foreach($d in $devs){
  "enable "+$d.InstanceId | Out-File $out -Append
  pnputil /enable-device $d.InstanceId 2>&1 | Out-File $out -Append
}
Start-Sleep 3
"DONE "+(Get-Date -Format o) | Out-File $out -Append
