$ws=New-Object -ComObject WScript.Shell
$lnk=$ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'OpenDSP.lnk'))
Write-Output ('before: target='+$lnk.TargetPath+' icon='+$lnk.IconLocation)
$lnk.TargetPath='D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc2\OpenDSP.exe'
$lnk.Arguments='--start'
$lnk.WorkingDirectory='D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc2'
$lnk.IconLocation='D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc2\OpenDSP.exe,0'
$lnk.Description='OpenDSP · 开源 HiFi DSP 套件'
$lnk.Save()
$check=$ws.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'OpenDSP.lnk'))
Write-Output ('after:  target='+$check.TargetPath+' icon='+$check.IconLocation)
