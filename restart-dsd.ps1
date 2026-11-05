$pipe=[IO.Pipes.NamedPipeClientStream]::new('.','OpenDSP',[IO.Pipes.PipeDirection]::InOut)
try { $pipe.Connect(3000); $w=[IO.StreamWriter]::new($pipe); $w.AutoFlush=$true; $r=[IO.StreamReader]::new($pipe)
  $w.WriteLine('{"cmd":"stop"}'); Write-Output ('stop: '+$r.ReadLine())
  $w.WriteLine('{"cmd":"start"}'); Write-Output ('start: '+$r.ReadLine())
  Start-Sleep -Seconds 2
  $w.WriteLine('{"cmd":"set_dsd","mode":"dop64"}'); Write-Output ('dsd: '+$r.ReadLine())
  $w.WriteLine('{"cmd":"set_preamp","db":-10}'); Write-Output ('preamp: '+$r.ReadLine())
} finally { $pipe.Dispose() }
