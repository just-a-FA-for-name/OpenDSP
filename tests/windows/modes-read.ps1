$p=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{8c283559-d27c-40d8-9f30-ace6390d2979}\FxProperties'
($p.'{d3993a3f-99c2-4402-b5ec-a92a0367664b},6') -join ' || '
