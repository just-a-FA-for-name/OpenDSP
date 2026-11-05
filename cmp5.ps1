$dir='D:\AI workspace\OpenEQ\evidence\parity'
$m=[IO.File]::ReadAllLines("$dir\managed_out.txt");$a=[IO.File]::ReadAllLines("$dir\apo_out.txt")
$flips=0;$first=-1;$maxmagdiff=0
for($i=0;$i -lt $m.Count;$i++){$mv=$m[$i].Split(' ');$av=$a[$i].Split(' ')
 $ml=[double]$mv[0];$al=[double]$av[0]
 $mm=[Math]::Abs([Math]::Abs($ml)-[Math]::Abs($al));if($mm -gt $maxmagdiff){$maxmagdiff=$mm}
 if(($ml -lt 0) -ne ($al -lt 0)){$flips++;if($first -lt 0){$first=$i}}}
"sign_flips=$flips first_flip=$first max_magnitude_diff=$maxmagdiff"
