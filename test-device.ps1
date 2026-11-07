param([string]$Serial='',[string]$Sdk='D:\adk\sdk',[string]$Jdk='D:\adk\jdk17',[string]$Apk='dist\OpenDSP-Android-0.4.0.apk',[switch]$VolumeCurve,[switch]$SystemVolumeGuard,[switch]$AudioTests,[switch]$SkipAppInstall,[switch]$UsbPrepare,[switch]$UsbSilence,[switch]$UsbServiceSilence,[switch]$WorkbenchLayout,[switch]$Headphones,[switch]$ReadLastReport,[switch]$NativeSrc,[switch]$PresetRoundtrip,[switch]$MobileFeatures,[switch]$ImportPcLibrary)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$Java=Join-Path $Jdk 'bin\java.exe';$Tools=Join-Path $Sdk 'build-tools\36.1.0';$Platform=Join-Path $Sdk 'platforms\android-36\android.jar';$Adb=Join-Path $Sdk 'platform-tools\adb.exe'
if(-not $Serial){$Candidates=@(& $Adb devices | Select-String '^\S+\s+device$' | ForEach-Object{($_.Line -split '\s+')[0]});if($Candidates.Count -ne 1){throw 'Specify -Serial when zero or multiple authorized devices are attached'};$Serial=$Candidates[0]}
function Run([string]$Exe,[string[]]$ArgsList){& $Exe @ArgsList;if($LASTEXITCODE -ne 0){throw "Failed: $Exe"}}
foreach($Dir in @('build\instrumentation','build\test-dex','evidence')){New-Item -ItemType Directory -Force -Path $Dir|Out-Null}
Run (Join-Path $Jdk 'bin\javac.exe') @('-encoding','UTF-8','-source','8','-target','8','-cp',($Platform+';build\classes'),'-d','build\instrumentation','tests\android\org\openeq\app\OpenEqInstrumentation.java')
$Classes=@(Get-ChildItem 'build\instrumentation' -Recurse -Filter '*.class'|ForEach-Object{$_.FullName})
Run $Java (@('-cp',(Join-Path $Tools 'lib\d8.jar'),'com.android.tools.r8.D8','--lib',$Platform,'--classpath','build\classes','--min-api','28','--output','build\test-dex')+$Classes)
Run (Join-Path $Tools 'aapt2.exe') @('link','-I',$Platform,'--manifest','tests\android\AndroidManifest.xml','-o','build\test-base.apk')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Zip=[IO.Compression.ZipFile]::Open((Join-Path $PSScriptRoot 'build\test-base.apk'),[IO.Compression.ZipArchiveMode]::Update)
try{[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Zip,(Join-Path $PSScriptRoot 'build\test-dex\classes.dex'),'classes.dex')|Out-Null}finally{$Zip.Dispose()}
Run (Join-Path $Tools 'zipalign.exe') @('-P','16','-f','4','build\test-base.apk','build\test-aligned.apk')
Run $Java @('-jar',(Join-Path $Tools 'lib\apksigner.jar'),'sign','--ks','signing\debug.jks','--ks-pass','pass:android','--out','build\OpenEQ-tests.apk','build\test-aligned.apk')
if(-not $SkipAppInstall){Run $Adb @('-s',$Serial,'install','-r',$Apk)}
Run $Adb @('-s',$Serial,'install','-r','build\OpenEQ-tests.apk')
$Options=@();if($SystemVolumeGuard){$Options=@('-e','system_volume_guard','true')}elseif($VolumeCurve){$Options=@('-e','volume_curve','true')}elseif($ImportPcLibrary){$Options=@('-e','import_pc_library','true')}elseif($MobileFeatures){$Options=@('-e','mobile_features','true')}elseif($PresetRoundtrip){$Options=@('-e','preset_roundtrip','true')}elseif($NativeSrc){$Options=@('-e','native_src','true')}elseif($ReadLastReport){$Options=@('-e','read_last_report','true')}elseif($Headphones){$Options=@('-e','headphones','true')}elseif($WorkbenchLayout){$Options=@('-e','workbench_layout','true')}elseif($UsbServiceSilence){$Options=@('-e','usb_service_silence','true')}elseif($UsbSilence){$Options=@('-e','usb_prepare','true','-e','usb_silence','true')}elseif($UsbPrepare){$Options=@('-e','usb_prepare','true')}elseif(-not $AudioTests){$Options=@('-e','no_audio','true')}
$Output=& $Adb -s $Serial shell am instrument -w @Options org.openeq.app.tests/org.openeq.app.OpenEqInstrumentation
$Output | Tee-Object -FilePath 'evidence\android-instrumentation.txt'
if(($Output -join "`n") -notmatch 'INSTRUMENTATION_CODE: -1'){throw 'Instrumentation failed'}
