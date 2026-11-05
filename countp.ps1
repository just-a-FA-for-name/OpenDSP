$all=@(Get-CimInstance Win32_Process -Filter "Name='OpenDSP.exe'")
$players=@($all | Where-Object {$_.CommandLine -like '*play*file*'})
'OpenDSP total='+$all.Count+' players='+$players.Count
foreach($p in $all){'  '+(if($p.CommandLine -like '*play*file*'){'PLAYER'}else{'MAIN  '})+' pid='+$p.ProcessId}
