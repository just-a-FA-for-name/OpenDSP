$dir='D:\AI workspace\OpenEQ\evidence\parity'
$m=[IO.File]::ReadAllLines("$dir\managed_out.txt");$a=[IO.File]::ReadAllLines("$dir\apo_out.txt")
$first=-1
for($i=0;$i -lt $m.Count;$i++){$mv=$m[$i].Split(' ');$av=$a[$i].Split(' ');for($k=0;$k -lt 2;$k++){$d=[Math]::Abs([double]$mv[$k]-[double]$av[$k]);if($d -gt 1e-12){$first=$i;break}}}
"first_divergence_line=$first"
if($first -ge 0){for($j=[Math]::Max(0,$first-1);$j -lt [Math]::Min($m.Count,$first+3);$j++){"$j  managed="+$m[$j]+"  apo="+$a[$j]}}
