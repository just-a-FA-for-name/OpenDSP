$dir='C:\ProgramData\OpenEQ\apo'
Get-ChildItem "$dir\*.bin" | ForEach-Object {
  $b=[IO.File]::ReadAllBytes($_.FullName)
  $ver=[BitConverter]::ToUInt32($b,4)
  if($ver -ne 2){Remove-Item $_.FullName -Force; Write-Output ("deleted stale: "+$_.Name+" ver="+$ver)}
}
'cleaned'
