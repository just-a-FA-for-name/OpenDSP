param([string]$Request='{"cmd":"get_state"}',[int]$TimeoutMs=4000)
$pipe=New-Object System.IO.Pipes.NamedPipeClientStream('.','OpenDSP',[System.IO.Pipes.PipeDirection]::InOut)
try{
  $pipe.Connect($TimeoutMs)
  $writer=New-Object System.IO.StreamWriter($pipe);$writer.AutoFlush=$true
  $reader=New-Object System.IO.StreamReader($pipe)
  $writer.WriteLine($Request)
  $line=$reader.ReadLine()
  if($null -eq $line){Write-Output '{"error":"no response"}';exit 1}
  Write-Output $line;exit 0
}catch{Write-Output ('{"error":"'+$_.Exception.Message+'"}');exit 1}
finally{if($pipe){$pipe.Dispose()}}
