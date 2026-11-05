param([string]$EndpointGuid)
$base="HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\$EndpointGuid"
foreach($sub in @('FxProperties','Properties')){
  $path="$base\$sub"
  Write-Output "=== $sub ==="
  if(Test-Path $path){
    $p=Get-ItemProperty -Path $path
    foreach($prop in $p.PSObject.Properties){
      if($prop.Name -in @('PSPath','PSParentPath','PSChildName','PSDrive','PSProvider')){continue}
      $val=$prop.Value
      if($val -is [Array]){ $txt=($val -join ' , ') }else{ $txt=[string]$val }
      if($txt.Length -gt 120){$txt=$txt.Substring(0,120)+'...'}
      Write-Output ($prop.Name+' = '+$txt)
    }
  }else{Write-Output '(missing)'}
}
