& 'D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc18\OpenDSP.exe' --shutdown | Out-Null
$w=0; while((Get-Process OpenDSP -ErrorAction SilentlyContinue) -and $w -lt 20){Start-Sleep -Milliseconds 500;$w++}
Get-Process OpenDSP -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
Start-Process 'D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc18\OpenDSP.exe' -ArgumentList '--background','--start'
Start-Sleep -Seconds 6
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.','OpenDSP',[IO.Pipes.PipeDirection]::InOut)
try { $pipe.Connect(3000); $w2=[IO.StreamWriter]::new($pipe); $w2.AutoFlush=$true; $r=[IO.StreamReader]::new($pipe)
  $w2.WriteLine('{"cmd":"stop"}'); $r.ReadLine() | Out-Null
  $w2.WriteLine('{"cmd":"set_format","rate":384000,"bits":32}'); Write-Output ('format: '+$r.ReadLine())
  $w2.WriteLine('{"cmd":"start"}'); Write-Output ('start: '+$r.ReadLine())
  $w2.WriteLine('{"cmd":"set_preamp","db":-10}'); Write-Output ('preamp: '+$r.ReadLine())
} finally { $pipe.Dispose() }
