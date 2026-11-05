# Build the OpenEQ virtual audio driver without a machine-wide WDK install.
# Uses the WDK NuGet package plus the local MSBuild toolset overlay under
# build/research/wdk-nuget (no admin rights required).
#
# Prerequisites (already present on the dev machine):
#   - VS 2022 Build Tools with MSVC x64
#   - build/research/wdk-nuget/pkg   (extracted Microsoft.Windows.WDK.x64 10.0.26100.1882)
#   - build/research/wdk-nuget/vctargets (VC targets overlay + WindowsKernelModeDriver10.0 shim)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # repo root = ..\..\ from windows/OpenEQ.VirtualCable
$wdk = Join-Path $root 'build\research\wdk-nuget'
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe'
if (!(Test-Path $msbuild)) { throw 'x64 MSBuild not found; install VS 2022 Build Tools' }
if (!(Test-Path (Join-Path $wdk 'pkg\c\build\10.0.26100.0\WindowsDriver.Common.targets'))) { throw 'WDK NuGet content missing; see build/research/wdk-nuget (download Microsoft.Windows.WDK.x64 10.0.26100.1882)' }

& $msbuild (Join-Path $PSScriptRoot 'Scream.vcxproj') `
  -t:Build -p:Configuration=Win10 -p:Platform=x64 `
  "-p:VCTargetsPath=$wdk\vctargets\" `
  "-p:WDK_NUGET_ROOT=$wdk" -v:m
if ($LASTEXITCODE -ne 0) { throw "driver build failed: $LASTEXITCODE" }

$pkg = Join-Path $PSScriptRoot 'x64\Win10\Scream'
Write-Host "Driver package: $pkg"
Get-ChildItem $pkg | Format-Table Name, Length
