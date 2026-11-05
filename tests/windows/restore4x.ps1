$ui="$env:LOCALAPPDATA\OpenEQ\desktop-ui.json"
$j=Get-Content $ui -Raw
$j=$j -replace '"Output": *"\{[^"]*\}"', '"Output": "{0.0.0.00000000}.{dc195d67-bd98-42fa-8fad-8937f407ef97}"'
Set-Content $ui $j -Encoding UTF8
$pipe=[IO.Pipes.NamedPipeClientStream]::new('.','OpenDSP',[IO.Pipes.PipeDirection]::InOut)
try { $pipe.Connect(3000); $w=[IO.StreamWriter]::new($pipe); $w.AutoFlush=$true; $r=[IO.StreamReader]::new($pipe)
  $w.WriteLine('{"cmd":"set_dsd","mode":"off"}'); Write-Output ('dsd-off: '+$r.ReadLine())
  $w.WriteLine('{"cmd":"stop"}'); Write-Output ('stop: '+$r.ReadLine())
  $w.WriteLine('{"cmd":"start"}'); Write-Output ('start: '+$r.ReadLine())
  $w.WriteLine('{"cmd":"set_preamp","db":-10}'); Write-Output ('preamp: '+$r.ReadLine())
} finally { $pipe.Dispose() }
