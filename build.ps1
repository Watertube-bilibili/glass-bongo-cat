[CmdletBinding()]
param([switch]$SkipTests)
$ErrorActionPreference='Stop'
$projectRoot=$PSScriptRoot
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'Build requires Windows .NET Framework 4.8.'}
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'dist'),(Join-Path $projectRoot 'artifacts') -Force | Out-Null
$references=@('System.dll','System.Core.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll') | ForEach-Object {'/reference:'+(Join-Path $framework $_)}
$references+=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object {'/reference:'+(Join-Path (Join-Path $framework 'WPF') $_)}
$sources=Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$assetRoot=Join-Path $projectRoot 'assets'
$resources=Get-ChildItem -LiteralPath (Join-Path $assetRoot 'cat') -File | Where-Object {$_.Extension -in @('.png','.txt')} | ForEach-Object {
    '/resource:'+ $_.FullName + ',GlassBongoCat.Assets.' + $_.FullName.Substring($assetRoot.Length+1).Replace('\','.')
}
$license=Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md'
if(Test-Path -LiteralPath $license){$resources+='/resource:'+$license+',GlassBongoCat.ThirdPartyNotices.txt'}
$output=Join-Path $projectRoot 'dist\GlassBongoCat.exe'
$flags=@('/nologo','/codepage:65001','/target:winexe','/platform:x64','/optimize+','/warn:4',('/out:'+$output),('/win32manifest:'+(Join-Path $projectRoot 'src\app.manifest')))
$icon=Join-Path $assetRoot 'GlassBongoCat.ico'
if(Test-Path -LiteralPath $icon){$flags+='/win32icon:'+$icon}
& $compiler @flags @references @resources @sources
if($LASTEXITCODE -ne 0){throw 'Application compilation failed.'}
if(-not $SkipTests){
    $optics=Join-Path $projectRoot 'artifacts\GlassMaterialTests.exe'
    & $compiler /nologo /optimize+ /target:exe ('/out:'+$optics) (Join-Path $projectRoot 'src\GlassMaterial.cs') (Join-Path $projectRoot 'tests\GlassMaterialTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Material tests compilation failed.'}
    & $optics
    if($LASTEXITCODE -ne 0){throw 'Material tests failed.'}
    $keyboardTests=Join-Path $projectRoot 'artifacts\KeyboardGlassTests.exe'
    & $compiler /nologo /optimize+ /target:exe ('/out:'+$keyboardTests) (Join-Path $projectRoot 'src\KeyboardGlass.cs') (Join-Path $projectRoot 'tests\KeyboardGlassTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Keyboard material test compilation failed.'}
    & $keyboardTests
    if($LASTEXITCODE -ne 0){throw 'Keyboard material tests failed.'}
    $session=Join-Path $projectRoot 'artifacts\DesktopSessionStateTests.exe'
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe ('/out:'+$session) @references (Join-Path $projectRoot 'src\DesktopSessionState.cs') (Join-Path $projectRoot 'src\AsyncDesktopBackdrop.cs') (Join-Path $projectRoot 'src\BackdropFrame.cs') (Join-Path $projectRoot 'src\NativeCaptureSupport.cs') (Join-Path $projectRoot 'tests\DesktopSessionStateTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Session/geometry tests compilation failed.'}
    & $session
    if($LASTEXITCODE -ne 0){throw 'Session/geometry tests failed.'}
    $integration=Join-Path $projectRoot 'artifacts\IntegrationTests.exe'
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /main:IntegrationTests ('/out:'+$integration) @references @resources @sources (Join-Path $projectRoot 'tests\IntegrationTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Integration tests compilation failed.'}
    & $integration
    if($LASTEXITCODE -ne 0){throw 'Integration tests failed.'}
}
$file=Get-Item -LiteralPath $output
if($file.Length -ge 5MB){throw 'The EXE exceeds the 5 MiB budget.'}
Write-Host ('GlassBongoCat.exe: {0:N0} bytes ({1:N3} MiB)' -f $file.Length,($file.Length/1MB))
Get-FileHash -LiteralPath $output -Algorithm SHA256 | Format-List
