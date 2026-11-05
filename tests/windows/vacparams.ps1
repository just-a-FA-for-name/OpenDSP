$p=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}\0026'
$p.PSObject.Properties | Where-Object {$_.Name -notlike 'PS*'} | ForEach-Object {
  $v=$_.Value
  if($v -is [Array]){$v=($v -join ',')}
  Write-Output ($_.Name+' = '+$v)
}
