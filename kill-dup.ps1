$players=Get-CimInstance Win32_Process -Filter "Name='OpenDSP.exe'" | Where-Object {$_.CommandLine -like '*--play-file*'}
'players found: '+$players.Count
foreach($p in $players){Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue}
Start-Sleep -Seconds 1
$left=@(Get-CimInstance Win32_Process -Filter "Name='OpenDSP.exe'" | Where-Object {$_.CommandLine -like '*--play-file*'}).Count
'players after kill: '+$left
