$dll='D:\AI workspace\OpenEQ\windows\OpenEQ.Desktop\bin\Release\net8.0-windows\NAudio.Core.dll'
Add-Type -Path $dll
Write-Output '=== PropertyStore methods ==='
[NAudio.CoreAudioApi.PropertyStore].GetMethods() | ForEach-Object { $_.ToString() }
Write-Output '=== PropVariant ctors ==='
[NAudio.CoreAudioApi.PropVariant].GetConstructors() | ForEach-Object { $_.ToString() }
Write-Output '=== MMDevice.Properties ==='
[NAudio.CoreAudioApi.MMDevice].GetProperty('Properties') | ForEach-Object { $_.ToString() }
