$exe='D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc2\OpenDSP.exe'
& $exe --set-default '{0.0.0.00000000}.{8c283559-d27c-40d8-9f30-ace6390d2979}'
& $exe --default-route-report 'D:\AI workspace\OpenEQ\evidence\sd0.json' | Out-Null
Start-Sleep -Seconds 3
& $exe --default-route-report 'D:\AI workspace\OpenEQ\evidence\sd3.json' | Out-Null
$j0=Get-Content 'D:\AI workspace\OpenEQ\evidence\sd0.json' -Raw|ConvertFrom-Json
$j3=Get-Content 'D:\AI workspace\OpenEQ\evidence\sd3.json' -Raw|ConvertFrom-Json
'immediate: '+$j0.Multimedia.name
'after3s:   '+$j3.Multimedia.name
