Get-CimInstance Win32_Process -Filter "Name='OpenDSP.exe'" | ForEach-Object {
  $tag='?'
  if($_.CommandLine -like '*play-file*'){$tag='PLAYER'}
  elseif($_.CommandLine -like '*background*'){$tag='MAIN'}
  elseif($_.CommandLine -like '*--start*'){$tag='STARTER'}
  Write-Output ($tag+' pid='+$_.ProcessId+' :: '+$_.CommandLine)
}
