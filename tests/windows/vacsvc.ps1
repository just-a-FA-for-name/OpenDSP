Get-Service | Where-Object {$_.Name -like '*irtual*' -or $_.DisplayName -like '*irtual Audio*'} | ForEach-Object { Write-Output ('SVC '+$_.Name+' | '+$_.DisplayName) }
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services' | Where-Object {$_.PSChildName -like '*irtualAudio*' -or $_.PSChildName -like '*vrtaucbl*'} | ForEach-Object { Write-Output ('KEY '+$_.PSChildName) }
Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services\vrtaucbl' -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ('VRTSUB '+$_.PSChildName) }
