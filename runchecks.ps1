Start-Process -Wait -WindowStyle Hidden 'D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\OpenDSP.exe' -ArgumentList '--dsd-checks','D:\AI workspace\OpenEQ\evidence\dsd-golden2.json'
'exit='+$LASTEXITCODE
