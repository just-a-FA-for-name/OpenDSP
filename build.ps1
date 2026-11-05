param([string]$Sdk='D:\adk\sdk',[string]$Jdk='D:\adk\jdk17',[switch]$TestOnly)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$Java=Join-Path $Jdk 'bin\java.exe'
$Javac=Join-Path $Jdk 'bin\javac.exe'
$Tools=Join-Path $Sdk 'build-tools\36.1.0'
$Platform=Join-Path $Sdk 'platforms\android-36\android.jar'
function Run([string]$Exe,[string[]]$ArgsList) { & $Exe @ArgsList; if($LASTEXITCODE -ne 0){throw "Failed ($LASTEXITCODE): $Exe"} }
foreach($Folder in @('build\classes','build\test','build\dex','dist','signing')) {New-Item -ItemType Directory -Force -Path $Folder | Out-Null}
Run $Javac (@('-encoding','UTF-8','-d','build\test')+@(Get-ChildItem 'core' -Recurse -Filter '*.java'|ForEach-Object {$_.FullName})+@('tests\DspTest.java','tests\AudioIoTest.java'))
Run $Java @('-cp','build\test','DspTest')
Run $Java @('-cp','build\test','AudioIoTest')
if($TestOnly){return}
& (Join-Path $PSScriptRoot 'build-native.ps1')
if($LASTEXITCODE -ne 0){throw 'Native build failed'}
Run (Join-Path $Tools 'aapt2.exe') @('compile','--dir','android\res','-o','build\resources.zip')
Run (Join-Path $Tools 'aapt2.exe') @('link','-I',$Platform,'--manifest','android\AndroidManifest.xml','--min-sdk-version','28','--target-sdk-version','36','-R','build\resources.zip','-o','build\base.apk')
$Sources=@(Get-ChildItem -LiteralPath 'core','android\src' -Recurse -Filter '*.java' | ForEach-Object {'"'+$_.FullName.Replace('\','/')+'"'})
$Utf8=New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'build\sources.txt'),$Sources,$Utf8)
Run $Javac @('-encoding','UTF-8','-source','8','-target','8','-cp',$Platform,'-d','build\classes','@build\sources.txt')
$Classes=@(Get-ChildItem -LiteralPath 'build\classes' -Recurse -Filter '*.class' | ForEach-Object {$_.FullName})
Run $Java (@('-cp',(Join-Path $Tools 'lib\d8.jar'),'com.android.tools.r8.D8','--lib',$Platform,'--min-api','28','--output','build\dex')+$Classes)
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Archive=[IO.Compression.ZipFile]::Open((Join-Path $PSScriptRoot 'build\base.apk'),[IO.Compression.ZipArchiveMode]::Update)
try{Get-ChildItem -LiteralPath 'build\dex' -Filter '*.dex' | ForEach-Object {[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive,$_.FullName,$_.Name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
 Get-ChildItem -LiteralPath 'build\native\arm64-v8a' -Filter '*.so' | ForEach-Object {[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive,$_.FullName,('lib/arm64-v8a/'+$_.Name),[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
 $AssetsRoot=Join-Path $PSScriptRoot 'android\assets'; Get-ChildItem -LiteralPath $AssetsRoot -Recurse -File | ForEach-Object { $Entry='assets/'+$_.FullName.Substring($AssetsRoot.Length+1).Replace('\','/'); [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive,$_.FullName,$Entry,[IO.Compression.CompressionLevel]::Optimal)|Out-Null }
}finally{$Archive.Dispose()}
Run (Join-Path $Tools 'zipalign.exe') @('-P','16','-f','4','build\base.apk','build\aligned.apk')
$Key=Join-Path $PSScriptRoot 'signing\debug.jks'
if(!(Test-Path -LiteralPath $Key)){Run (Join-Path $Jdk 'bin\keytool.exe') @('-genkeypair','-keystore',$Key,'-storepass','android','-keypass','android','-alias','androiddebugkey','-keyalg','RSA','-keysize','2048','-validity','10000','-dname','CN=OpenEQ Development')}
$Apk=Join-Path $PSScriptRoot 'dist\OpenDSP-Android-0.4.0.apk'
Run $Java @('-jar',(Join-Path $Tools 'lib\apksigner.jar'),'sign','--ks',$Key,'--ks-pass','pass:android','--v4-signing-enabled','false','--out',$Apk,'build\aligned.apk')
Run $Java @('-jar',(Join-Path $Tools 'lib\apksigner.jar'),'verify','--verbose',$Apk)
Run (Join-Path $Tools 'zipalign.exe') @('-c','-P','16','4',$Apk)
(Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash | Set-Content -LiteralPath 'dist\OpenDSP-Android-0.4.0.sha256.txt'
Write-Output "APK ready: $Apk"
