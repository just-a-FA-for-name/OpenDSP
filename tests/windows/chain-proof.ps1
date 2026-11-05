# Stage-by-stage digital chain proof: file -> entry capture -> DSP -> output.
# Uses only in-process telemetry numbers (no acoustics): captured bytes advance,
# frames advance, and EnergyOut/EnergyIn matches the active EQ setting.
$ErrorActionPreference = 'Stop'
$out = 'D:\AI workspace\OpenEQ\evidence\chain-proof.json'
$result = [ordered]@{}
function State {
  $p = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'OpenDSP', [System.IO.Pipes.PipeDirection]::InOut)
  $p.Connect(4000)
  $w = New-Object System.IO.StreamWriter($p); $w.AutoFlush = $true
  $r = New-Object System.IO.StreamReader($p)
  $w.WriteLine('{"cmd":"get_state"}')
  $line = $r.ReadLine(); $p.Dispose()
  $j = $line | ConvertFrom-Json
  return $j
}
function SetPreamp($db) {
  $p = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'OpenDSP', [System.IO.Pipes.PipeDirection]::InOut)
  $p.Connect(4000)
  $w = New-Object System.IO.StreamWriter($p); $w.AutoFlush = $true
  $r = New-Object System.IO.StreamReader($p)
  $w.WriteLine("{`"cmd`":`"set_preamp`",`"db`":$db}")
  $null = $r.ReadLine(); $p.Dispose()
}
# start music file into the chain (default route = entry)
$proc = Start-Process -FilePath 'D:\AI workspace\OpenEQ\dist\OpenDSP-Windows-0.4-rc2\OpenDSP.exe' -ArgumentList '--play-file','F:\ai\AI测试\frog-island-bgm.m4a' -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 3
$s1 = State
Start-Sleep -Seconds 3
$s2 = State
$result.captured_growing = $s2.engine.captured -gt $s1.engine.captured
$result.captured_delta = $s2.engine.captured - $s1.engine.captured
$result.frames_growing = $s2.engine.frames -gt $s1.engine.frames
$result.frames_delta = $s2.engine.frames - $s1.engine.frames
# EQ -20 dB transfer ratio
SetPreamp -20
Start-Sleep -Seconds 3
$a = State
Start-Sleep -Seconds 5
$b = State
$dIn = $b.engine.energy_in - $a.engine.energy_in
$dOut = $b.engine.energy_out - $a.engine.energy_out
$result.ratio_minus20_db = if($dIn -gt 0){ 10 * [Math]::Log10($dOut / $dIn) } else { $null }
# EQ 0 dB transfer ratio
SetPreamp 0
Start-Sleep -Seconds 3
$a = State
Start-Sleep -Seconds 5
$b = State
$dIn = $b.engine.energy_in - $a.engine.energy_in
$dOut = $b.engine.energy_out - $a.engine.energy_out
$result.ratio_0_db = if($dIn -gt 0){ 10 * [Math]::Log10($dOut / $dIn) } else { $null }
$result.peak_out = $b.engine.peak_out
$result.overflow = $b.engine.overflow
$result.starved = $b.engine.starved
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
# verdict: chain receives + processes + EQ matches settings
$okRatio = ($result.ratio_minus20_db -ne $null -and $result.ratio_0_db -ne $null -and [Math]::Abs($result.ratio_minus20_db + 20) -lt 1.5 -and [Math]::Abs($result.ratio_0_db) -lt 1.5)
$result.result = if($result.captured_growing -and $result.frames_growing -and $okRatio){ 'CHAIN_PASS' } else { 'CHAIN_FAIL' }
$result | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 $out
$result.result
