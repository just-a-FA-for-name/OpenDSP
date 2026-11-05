Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render' | ForEach-Object {
  $p=Get-ItemProperty (Join-Path $_.PSPath 'Properties') -ErrorAction SilentlyContinue
  $n=$p.'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
  if($n -like '*Virtual Audio Cable*'){ Write-Output ('R '+$_.PSChildName+' = '+$n) }
}
Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture' | ForEach-Object {
  $p=Get-ItemProperty (Join-Path $_.PSPath 'Properties') -ErrorAction SilentlyContinue
  $n=$p.'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
  if($n -like '*Line*'){ Write-Output ('C '+$_.PSChildName+' = '+$n) }
}
