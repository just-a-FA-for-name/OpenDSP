$ok=$true
foreach($f in @('D:\AI workspace\OpenEQ\build\research\acmee4x-official-asio-driver\BravoHDASIO.dll','D:\AI workspace\OpenEQ\build\research\acmee4x-official-asio-driver\BvoHdUAC.sys')){
  $b=[IO.File]::ReadAllBytes($f); $t=[Text.Encoding]::ASCII.GetString($b); $w=[Text.Encoding]::Unicode.GetString($b)
  $hits=([regex]::Matches($t,'DSD')).Count+([regex]::Matches($w,'DSD')).Count+([regex]::Matches($t,'DoP')).Count+([regex]::Matches($w,'DoP')).Count
  Write-Output ((Split-Path $f -Leaf)+' DSD/DoP hits='+$hits)
}
