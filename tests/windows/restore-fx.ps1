param([string]$EndpointGuid,[switch]$ProbeOnly)
$ErrorActionPreference='Stop'
$fx="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$EndpointGuid\FxProperties"
$props="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$EndpointGuid\Properties"
$keys=@(
  @{Store='fx';Name='{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6'},
  @{Store='fx';Name='{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7'},
  @{Store='fx';Name='{d3993a3f-99c2-4402-b5ec-a92a0367664b},6'},
  @{Store='fx';Name='{d3993a3f-99c2-4402-b5ec-a92a0367664b},7'},
  @{Store='fx';Name='{73467431-da1e-4eb4-af89-f615a18635dd},1'},
  @{Store='props';Name='{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5'}
)
foreach($k in $keys){
  $path=if($k.Store -eq 'fx'){$fx}else{$props}
  $cur=Get-ItemProperty -Path $path -Name $k.Name -ErrorAction SilentlyContinue
  if($null -ne $cur){
    Write-Output ("current "+$k.Store+" "+$k.Name+" = "+($cur.($k.Name)|Out-String).Trim())
    if(-not $ProbeOnly){Remove-ItemProperty -Path $path -Name $k.Name -ErrorAction Stop}
  }else{
    Write-Output ("absent  "+$k.Store+" "+$k.Name)
  }
}
if(-not $ProbeOnly){Write-Output 'RESTORED'}
