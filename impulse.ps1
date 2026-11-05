$rate=384000.0; $f=1000.0; $Q=0.707; $gainDb=6.0
$w=2*[Math]::PI*$f/$rate; $c=[Math]::Cos($w); $s=[Math]::Sin($w)
$alpha=$s/(2*$Q); $A=[Math]::Pow(10,$gainDb/40); $t=2*[Math]::Sqrt($A)*$alpha
$b0=$A*(($A+1)+($A-1)*$c+$t); $b1=-2*$A*(($A-1)+($A+1)*$c); $b2=$A*(($A+1)+($A-1)*$c-$t)
$a0=($A+1)-($A-1)*$c+$t; $a1=2*(($A-1)-($A+1)*$c); $a2=($A+1)-($A-1)*$c-$t
$b0/=$a0; $b1/=$a0; $b2/=$a0; $a1/=$a0; $a2/=$a0
"norm: b0=$b0 b1=$b1 b2=$b2 a1=$a1 a2=$a2"
$z1=0.0; $z2=0.0; $max=0.0; $bad=0
for($n=0;$n -lt 384000;$n++){
  $x=if($n -eq 0){1.0}else{0.0}
  $y=$b0*$x+$z1; $z1=$b1*$x-$a1*$y+$z2; $z2=$b2*$x-$a2*$y
  $ab=[Math]::Abs($y)
  if($ab -gt $max){$max=$ab}
  if([double]::IsNaN($y) -or $ab -ge 10){$bad=$n; break}
}
"max=$max firstBad=$bad"
