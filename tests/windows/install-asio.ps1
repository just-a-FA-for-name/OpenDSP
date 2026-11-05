Expand-Archive -Path 'D:\AI workspace\OpenEQ\build\research\acmee4x-official-asio-3.14.zip' -DestinationPath 'D:\AI workspace\OpenEQ\tools\acmee-asio-install' -Force
$msi='D:\AI workspace\OpenEQ\tools\acmee-asio-install\SaviAudioBravoHDSwPkg_HsDriverASIOiAP_20240607_v3p14p0p10\BravoHDSwPkgSetup.msi'
$p=Start-Process msiexec.exe -Verb RunAs -ArgumentList '/i',"`"$msi`"","/qn","/norestart" -Wait -PassThru
'msi exit='+$p.ExitCode
