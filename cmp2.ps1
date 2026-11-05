$dir='D:\AI workspace\OpenEQ\evidence\parity'
$m=[IO.File]::ReadAllLines("$dir\managed_out.txt");$a=[IO.File]::ReadAllLines("$dir\apo_out.txt")
$max=0.0;$idx=-1
for($i=0;$i -lt $m.Count;$i++){$mv=$m[$i].Split(' ');$av=$a[$i].Split(' ');for($k=0;$k -lt 2;$k++){$d=[Math]::Abs([double]$mv[$k]-[double]$av[$k]);if($d -gt $max){$max=$d;$idx=$i}}}
"max_dev=$max first_bad_line=$idx"
if($idx -ge 0){"managed[$idx]=" + $m[$idx];"apo[$idx]=" + $a[$idx]}
