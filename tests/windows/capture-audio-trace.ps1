param([ValidateRange(5,30)][int]$Seconds=15,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'WPR Audio tracing requires an elevated process'}
$outputPath=[IO.Path]::GetFullPath($Output)
if([IO.Path]::GetExtension($outputPath) -ne '.etl'){throw 'Output must be an .etl file'}
if(Test-Path -LiteralPath $outputPath){throw 'Trace output already exists; choose a new name'}
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$wprPath=Join-Path $env:WINDIR 'System32\wpr.exe'
$status=& $wprPath -status 2>&1
if(($status -join "`n") -notmatch 'WPR is not recording'){throw 'An existing or unknown WPR session was found; it will not be modified'}
$started=$false
$report=[ordered]@{started_at=(Get-Date -Format o);seconds=$Seconds;profile='Audio';scope='Windows audio timing events only; no audio device formats, bindings or policies are modified';output=$outputPath}
try {
 $startOutput=& $wprPath -start Audio -filemode 2>&1
 if($LASTEXITCODE -ne 0){throw ($startOutput -join "`n")}
 $started=$true
 Start-Sleep -Seconds $Seconds
} catch {$report.error=$_.Exception.ToString();throw}
finally {
 if($started){$stopOutput=& $wprPath -stop $outputPath 2>&1;$report.stop_exit_code=$LASTEXITCODE;$report.stop_output=$stopOutput -join "`n";$report.dropped_events=0;if($report.stop_output -match 'dropped\s+(\d+)\s+events'){$report.dropped_events=[long]$Matches[1]}}
 $report.collected=Test-Path -LiteralPath $outputPath
 $report | ConvertTo-Json | Set-Content -LiteralPath ($outputPath+'.json')
}
if(-not $report.collected -or $report.stop_exit_code -ne 0){throw 'Audio trace collection did not finish successfully; inspect the trace report'}
if($report.dropped_events -gt 0){throw 'The trace dropped events and cannot prove absence of glitches; inspect the trace report'}
