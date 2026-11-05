$ErrorActionPreference='SilentlyContinue'
$render='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
Get-ChildItem $render | ForEach-Object {
  $fx=Join-Path $_.PSPath 'FxProperties'
  if(Test-Path $fx){
    $p=Get-ItemProperty -Path $fx
    $clsid=$p.'{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6'
    $efx=$p.'{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7'
    $dis=$p.'{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5'
    if($clsid -or $efx){
      $props=Join-Path $_.PSPath 'Properties'
      $name=(Get-ItemProperty -Path $props).'{a45c254e-df1c-4efd-8020-67d146a850e0},2'
      Write-Output ('EP '+$_.PSChildName+' name='+$name)
      Write-Output ('   mfx(,6)='+$clsid)
      Write-Output ('   efx(,7)='+$efx)
      Write-Output ('   disable(,5)='+$dis)
    }
  }
}
Write-Output '--- backups in C:\ProgramData\OpenEQ\apo ---'
Get-ChildItem 'C:\ProgramData\OpenEQ\apo' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name
