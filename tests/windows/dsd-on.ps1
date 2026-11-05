$pipe=[IO.Pipes.NamedPipeClientStream]::new('.','OpenDSP',[IO.Pipes.PipeDirection]::InOut)
try {
  $pipe.Connect(3000)
  $w=[IO.StreamWriter]::new($pipe); $w.AutoFlush=$true
  $r=[IO.StreamReader]::new($pipe)
  $w.WriteLine('{"cmd":"get_state"}'); $s=$r.ReadLine()
  Write-Output ('state: '+$s.Substring(0,[Math]::Min(220,$s.Length)))
  $w.WriteLine('{"cmd":"set_dsd","mode":"dop64"}'); Write-Output ('set_dsd: '+$r.ReadLine())
  Start-Sleep -Seconds 3
  $w.WriteLine('{"cmd":"get_state"}'); $s2=$r.ReadLine()
  Write-Output ('after: '+$s2.Substring(0,[Math]::Min(220,$s2.Length)))
} finally { $pipe.Dispose() }
