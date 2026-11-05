# OpenDSP control-pipe + MCP round-trip and edge-case acceptance (ASCII-only; run while OpenDSP is running).
$ErrorActionPreference = 'Stop'
$out = 'D:\AI workspace\OpenEQ\evidence\mcp-roundtrip.json'
$result = [ordered]@{}
$p = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'OpenDSP', [System.IO.Pipes.PipeDirection]::InOut)
$p.Connect(5000)
$w = New-Object System.IO.StreamWriter($p); $w.AutoFlush = $true
$r = New-Object System.IO.StreamReader($p)
function Send($cmd){ $w.WriteLine($cmd); return $r.ReadLine() }
# 1) band edit round-trip
$null = Send('{"cmd":"set_band","index":0,"gain_db":1.5,"frequency_hz":80}')
$state = Send('{"cmd":"get_state"}')
$result.pipe_set_band_ok = $state -like '*1.5*'
# 2) apply curve (ASCII-safe unicode escape)
$curve = Send('{"cmd":"apply_curve","name":"V \u578b"}')
$result.pipe_apply_curve_ok = $curve -like '*"ok":true*'
# 3) edge: invalid DSD mode rejected
$bad = Send('{"cmd":"set_dsd","mode":"nonsense"}')
$result.invalid_mode_rejected = $bad -like '*"ok":false*'
# 4) edge: out-of-range band rejected AND state not left half-mutated
$before = Send('{"cmd":"get_state"}')
$bad2 = Send('{"cmd":"set_band","index":0,"gain_db":999}')
$after = Send('{"cmd":"get_state"}')
$result.out_of_range_rejected = $bad2 -like '*"ok":false*'
$result.state_unchanged_after_error = $before -eq $after
# 5) edge: 32-band cap enforced
$capRejected = $false
for($i=0; $i -lt 40; $i++){
  $resp = Send('{"cmd":"add_band","frequency_hz":1000,"gain_db":0}')
  if($resp -like '*"ok":false*'){ $capRejected = $true; break }
}
$state2 = Send('{"cmd":"get_state"}')
$bandCount = ([regex]::Matches($state2,'"type":')).Count
$result.band_cap_ok = $capRejected -and $bandCount -le 32
# 6) MCP server protocol round-trip
$mcp = 'D:\AI workspace\OpenEQ\tools\OpenDspMcp\dist\OpenDspMcp.exe'
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $mcp; $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false
$proc = [System.Diagnostics.Process]::Start($psi)
$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'); $proc.StandardInput.Flush()
$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list"}'); $proc.StandardInput.Flush()
$proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"set_compressor","arguments":{"enabled":true,"threshold_db":-12,"ratio":3}}}'); $proc.StandardInput.Flush()
Start-Sleep -Seconds 2
$proc.StandardInput.Close()
$lines = @()
while(($line = $proc.StandardOutput.ReadLine()) -ne $null){ $lines += $line; if($lines.Count -ge 3){ break } }
try { $proc.Kill() } catch { }
$result.mcp_tool_count = if($lines.Count -ge 2){ ([regex]::Matches($lines[1],'"name":')).Count } else { 0 }
$result.mcp_compressor_ok = ($lines | Where-Object { $_ -like '*\"ok\":true*' }).Count -gt 0
$result.result = if($result.pipe_set_band_ok -and $result.pipe_apply_curve_ok -and $result.invalid_mode_rejected -and $result.out_of_range_rejected -and $result.state_unchanged_after_error -and $result.band_cap_ok -and $result.mcp_tool_count -ge 10 -and $result.mcp_compressor_ok){ 'PASS' } else { 'FAIL' }
$result | ConvertTo-Json -Depth 6 | Out-File -Encoding utf8 $out
$result.result
