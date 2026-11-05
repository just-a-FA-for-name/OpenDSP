# Install the OpenEQ virtual cable driver package (DEV / TEST-SIGNING only).
# This script NEVER enables testsigning and NEVER reboots. If test signing is
# off it prints the exact commands and exits; enabling it is a deliberate
# user decision (Secure Boot implications, visible Test Mode watermark).
# Requires: elevated PowerShell, package built via build-driver.ps1.
$ErrorActionPreference = 'Stop'
$pkg = Join-Path $PSScriptRoot 'x64\Win10\Scream'
$inf = Join-Path $pkg 'OpenEQ.VirtualCable.inf'
if (!(Test-Path $inf)) { throw "driver package not found: $inf (run build-driver.ps1 first)" }

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$admin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$admin) { throw 'run this script from an elevated PowerShell' }

# Test-signing state: check the BCD flag (no modification).
$bcd = bcdedit /enum '{current}' 2>$null | Out-String
if ($bcd -notmatch 'testsigning\s+Yes') {
  Write-Host @'
Test signing is not enabled. The dev driver is signed with a local test
certificate and Windows will refuse to load it in normal mode.

To enable (YOUR decision; requires reboot and shows a Test Mode watermark):

  1. Disable Secure Boot in BIOS/UEFI (required for testsigning).
  2. From an elevated prompt:  bcdedit /set testsigning on
  3. Reboot Windows.
  4. Re-run this script to install the driver.
  5. After testing, remove with:  bcdedit /set testsigning off  + reboot.

Production distribution must NOT use test signing: it needs an EV code
signing certificate and Microsoft attestation/HLK signing (see
spec/USB_EXCLUSIVE_DECISION.md).
'@
  exit 2
}

pnputil /add-driver $inf /install
if ($LASTEXITCODE -ne 0) { throw "pnputil failed: $LASTEXITCODE" }
Write-Host 'Driver package registered. The OpenEQ Virtual Cable endpoint should now appear in Windows playback devices.'
Write-Host 'In OpenEQ: switch OFF 原生系统 EQ (bridge mode), pick 扬声器 (OpenEQ Virtual Cable) as 系统音频入口 and the USB DAC as output.'
