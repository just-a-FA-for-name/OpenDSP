try{Stop-Service Audiosrv -Force;Start-Service Audiosrv}catch{}
Start-Sleep 3
'restart done' | Out-File 'D:\AI workspace\OpenEQ\evidence\restart.txt' -Encoding UTF8
