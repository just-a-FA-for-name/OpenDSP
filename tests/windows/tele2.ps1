$pipe=[IO.Pipes.NamedPipeClientStream]::new('.','OpenDSP',[IO.Pipes.PipeDirection]::InOut)
try { $pipe.Connect(3000); $w=[IO.StreamWriter]::new($pipe); $w.AutoFlush=$true; $r=[IO.StreamReader]::new($pipe)
  $w.WriteLine('{"cmd":"get_state"}'); $s=$r.ReadLine(); Write-Output $s
} finally { $pipe.Dispose() }
