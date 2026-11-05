$path='C:\ProgramData\OpenEQ\apo\{8C283559-D27C-40D8-9F30-ACE6390D2979}.bin'
if(Test-Path $path){
  $b=[IO.File]::ReadAllBytes($path)
  $frames=[BitConverter]::ToInt64($b,1448)
  $pid2=[BitConverter]::ToUInt32($b,1500)
  "frames=$frames pid=$pid2 size=$($b.Length)"
}else{"bin missing"}
