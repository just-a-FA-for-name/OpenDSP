$out='D:\AI workspace\OpenEQ\evidence\copyfmt.txt'
function Log($m){$m|Out-File $out -Append -Encoding UTF8}
'=== run '+(Get-Date -Format o)|Out-File $out -Encoding UTF8
$src='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\{e5e45419-3510-4fc4-9868-b0c7e6b71934}\Properties'
$dst='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{ff3eb428-239e-43e5-9d0b-496440ec6d0b}\Properties'
foreach($n in @('{f19f064d-082c-4e27-bc73-6882a1bb8e4c},0','{3d6e1656-2e50-4c4c-8d85-d0acae3c6c68},2','{3d6e1656-2e50-4c4c-8d85-d0acae3c6c68},3','{e4870e26-3cc5-4cd2-ba46-ca0a9a70ed04},0','{e4870e26-3cc5-4cd2-ba46-ca0a9a70ed04},1','{624f56de-fd24-473e-814a-de40aacaed16},3')){
  try{
    $v=(Get-ItemProperty -LiteralPath $src -Name $n -ErrorAction Stop).$n
    Set-ItemProperty -LiteralPath $dst -Name $n -Value $v -Type Binary -ErrorAction Stop
    Log ('copied '+$n+' bytes='+$v.Length)
  }catch{Log ('fail '+$n+' : '+$_.Exception.Message)}
}
$previous=@((Get-Service Audiosrv).DependentServices | Where-Object Status -eq Running | Select-Object -ExpandProperty Name)
try{Stop-Service Audiosrv -Force;Start-Service Audiosrv;foreach($s in $previous){Start-Service -Name $s -ErrorAction SilentlyContinue}}finally{if((Get-Service Audiosrv).Status -ne 'Running'){Start-Service Audiosrv}}
Log 'audio service restarted'
Start-Sleep 4
& 'D:\AI workspace\tools\dotnet8\dotnet.exe' 'D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.dll' --device-probe '{0.0.0.00000000}.{ff3eb428-239e-43e5-9d0b-496440ec6d0b}' 'D:\AI workspace\OpenEQ\evidence\probe-r.json' | Out-Null
Log (Get-Content 'D:\AI workspace\OpenEQ\evidence\probe-r.json' -Raw)
Log 'DONE'
