param([string]$Output='../../build/apo')
$ErrorActionPreference='Stop'
$taskHere=$PSScriptRoot
$taskOut=[IO.Path]::GetFullPath((Join-Path $taskHere $Output))
New-Item -ItemType Directory -Force -Path $taskOut | Out-Null
$taskVc='C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/VC/Tools/MSVC/14.42.34433'
$taskSdk='C:/Program Files (x86)/Windows Kits/10'
$env:INCLUDE="$taskVc/include;$taskSdk/Include/10.0.22621.0/ucrt;$taskSdk/Include/10.0.22621.0/shared;$taskSdk/Include/10.0.22621.0/um"
$env:LIB="$taskVc/lib/x64;$taskSdk/Lib/10.0.22621.0/ucrt/x64;$taskSdk/Lib/10.0.22621.0/um/x64"
& "$taskVc/bin/Hostx64/x64/cl.exe" /nologo /utf-8 /std:c++17 /O2 /EHsc /MT /LD /DUNICODE /D_UNICODE (Join-Path $taskHere 'apo.cpp') "/Fo$taskOut/apo.obj" /link "/OUT:$taskOut/OpenEQ.Apo.dll" /MANIFEST:NO /EXPORT:DllGetClassObject /EXPORT:DllCanUnloadNow ole32.lib uuid.lib propsys.lib shell32.lib
if($LASTEXITCODE -ne 0){throw 'APO native build failed'}
& "$taskVc/bin/Hostx64/x64/cl.exe" /nologo /utf-8 /std:c++17 /O2 /EHsc /MT /DUNICODE /D_UNICODE (Join-Path $taskHere 'checks.cpp') "/Fo$taskOut/checks.obj" /link "/OUT:$taskOut/OpenEQ.Apo.Checks.exe" ole32.lib uuid.lib propsys.lib shell32.lib
if($LASTEXITCODE -ne 0){throw 'APO checks build failed'}
Write-Output "Native APO built: $taskOut/OpenEQ.Apo.dll"
