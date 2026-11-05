# OpenDSP virtual cable preparation: restore point + test signing.
# Run elevated. Reversal: bcdedit /set testsigning off + reboot.
$ErrorActionPreference = 'Continue'
$out = 'D:\AI workspace\OpenEQ\evidence\testsigning-state.txt'
try {
  Checkpoint-Computer -Description 'OpenDSP virtual cable pre-install' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
  'restore_point=created' | Out-File -Encoding utf8 $out
} catch {
  "restore_point=SKIPPED ($($_.Exception.Message))" | Out-File -Encoding utf8 $out
}
bcdedit /set testsigning on | Out-Null
"bcdedit_exit=$LASTEXITCODE" | Out-File -Append -Encoding utf8 $out
(bcdedit /enum '{current}' | Out-String) | Out-File -Append -Encoding utf8 $out
'driver_package_ready=windows\OpenEQ.VirtualCable\x64\Win10\Scream\{OpenEQ.VirtualCable.sys,OpenEQ.VirtualCable.inf,scream.cat}' | Out-File -Append -Encoding utf8 $out
'reboot_required=true  (test signing takes effect after reboot; then run install-testsigning.ps1)' | Out-File -Append -Encoding utf8 $out
