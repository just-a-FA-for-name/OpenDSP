param([string]$Ndk='D:/adk/sdk/ndk/26.1.10909125')
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$Compiler=Join-Path $Ndk 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'
$Cpp=Join-Path $Ndk 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang++.exe'
function Run([string]$Exe,[string[]]$ArgsList){& $Exe @ArgsList;if($LASTEXITCODE -ne 0){throw "Native build failed: $Exe"}}
$Vendor='third_party/libusb'
$Common=@('--target=aarch64-linux-android28','-fPIC','-O2',"-I$Vendor/android","-I$Vendor/libusb",'-fvisibility=hidden')
$Out='build/native/arm64-v8a'
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Sources=@('core','descriptor','hotplug','io','sync','strerror','os/linux_usbfs','os/events_posix','os/threads_posix','os/linux_netlink')
$Objects=@()
foreach($Source in $Sources){$Object=Join-Path $Out ($Source.Replace('/','_')+'.o');Run $Compiler ($Common+@('-c',"$Vendor/libusb/$Source.c",'-o',$Object));$Objects+=$Object}
Run $Compiler (@('--target=aarch64-linux-android28','-shared','-Wl,-soname,libusb1.0.so','-Wl,-z,max-page-size=16384','-o',"$Out/libusb1.0.so")+$Objects+@('-llog'))
Run $Cpp ($Common+@('-std=c++17','-shared','-static-libstdc++','-Wl,-z,max-page-size=16384','android/native/usb_output.cpp',"-L$Out",'-lusb1.0','-llog','-o',"$Out/libopeneq_usb.so"))
Write-Output "Native libraries ready: $Out"
