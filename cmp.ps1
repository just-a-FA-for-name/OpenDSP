$dir='D:\AI workspace\OpenEQ\evidence\parity'
$m=[IO.File]::ReadAllLines("$dir\managed_out.txt")
$a=[IO.File]::ReadAllLines("$dir\apo_out.txt")
if($m.Count -ne $a.Count){Write-Output ("LINE MISMATCH "+$m.Count+" vs "+$a.Count);exit 1}
$max=0.0;$idx=0
for($i=0;$i -lt $m.Count;$i++){
  $mv=$m[$i].Split(' '); $av=$a[$i].Split(' ')
  for($k=0;$k -lt 2;$k++){
    $d=[Math]::Abs([double]$mv[$k]-[double]$av[$k])
    if($d -gt $max){$max=$d;$idx=$i}
  }
}
"max_deviation=$max at_line=$idx samples=$($m.Count)"
