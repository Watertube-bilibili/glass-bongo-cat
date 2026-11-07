[CmdletBinding()]
param(
    [ValidateSet('Baseline','Current','Compare')][string]$Stage='Compare',
    [string]$BaselineTag='v0.1.1',
    [switch]$CompileOnly
)
# Manual, opt-in comparative test. Never invoked by build.ps1/package.ps1.
# Does not start the app entry point, load user settings, send input or save pixels.
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot=Join-Path $projectRoot 'artifacts'
$baselineRoot=Join-Path $artifactRoot ('baseline-'+$BaselineTag)
$benchmarkRoot=Join-Path $artifactRoot ('frame-performance-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
if($BaselineTag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$'){throw 'BaselineTag must be a version tag.'}
[IO.Directory]::CreateDirectory($benchmarkRoot)|Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$runner=Join-Path $PSScriptRoot 'FramePerformanceTests.cs'
$references=@('System.dll','System.Core.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$references+=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')|ForEach-Object{'/reference:'+(Join-Path (Join-Path $framework 'WPF') $_)}
$baselineCommit=(& git -C $projectRoot rev-parse ($BaselineTag+'^{commit}')).Trim()
if($LASTEXITCODE -ne 0){throw 'Cannot resolve baseline tag.'}
$archive=Join-Path $artifactRoot ('baseline-'+$BaselineTag+'.zip')
& git -C $projectRoot archive --format=zip ('--output='+$archive) $baselineCommit
if($LASTEXITCODE -ne 0){throw 'Baseline git archive failed.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(-not(Test-Path -LiteralPath $baselineRoot)){[IO.Compression.ZipFile]::ExtractToDirectory($archive,$baselineRoot)}
# Reused baseline directories must match every byte in the immutable tag.
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach($entry in $zip.Entries){
        if($entry.FullName.EndsWith('/')){continue}
        $path=Join-Path $baselineRoot $entry.FullName
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Baseline missing '+$entry.FullName)}
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$expected=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
        if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expected){throw ('Baseline modified: '+$entry.FullName)}
    }
}finally{$zip.Dispose()}
$stages=if($Stage -eq 'Compare'){@('Baseline','Current')}else{@($Stage)}
$metadata=@{utc=(Get-Date).ToUniversalTime().ToString('o');baselineCommit=$baselineCommit;runnerSha256=(Get-FileHash -LiteralPath $runner -Algorithm SHA256).Hash;opacity=.001;warmupSeconds=2;movingSeconds=6;sizes=@(200,300);inputInjected=$false;screenshotsSaved=$false;stages=@()}
foreach($item in $stages){
    $root=if($item -eq 'Baseline'){$baselineRoot}else{$projectRoot}
    $label=if($item -eq 'Baseline'){$BaselineTag}else{'current'}
    $sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File|Sort-Object Name|ForEach-Object FullName)
    $assetRoot=Join-Path $root 'assets'
    $resourceFiles=@(Get-ChildItem -LiteralPath (Join-Path $assetRoot 'cat') -File|Where-Object{$_.Extension -in @('.png','.txt')}|Sort-Object Name)
    $resources=@($resourceFiles|ForEach-Object{'/resource:'+ $_.FullName+',GlassBongoCat.Assets.'+$_.FullName.Substring($assetRoot.Length+1).Replace('\','.')})
    $notice=Join-Path $root 'THIRD_PARTY_NOTICES.md'
    $resources+='/resource:'+$notice+',GlassBongoCat.ThirdPartyNotices.txt'
    $output=Join-Path $benchmarkRoot ($label+'-FramePerformanceTests.exe')
    $csv=Join-Path $benchmarkRoot ($label+'.csv')
    $manifest=Join-Path $root 'src\app.manifest'
    & $compiler /nologo /codepage:65001 /optimize+ /target:exe /platform:x64 /main:FramePerformanceTests ('/out:'+$output) ('/win32manifest:'+$manifest) @references @resources @sources $runner
    if($LASTEXITCODE -ne 0){throw ($item+' benchmark compilation failed.')}
    $hashes=@(@($sources)+@($resourceFiles|ForEach-Object FullName)+@($notice,$manifest)|ForEach-Object{[pscustomobject]@{file=$_.Substring($root.Length+1);sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
    $record=[ordered]@{stage=$item;label=$label;sourceRoot=$root;exeSha256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash;sourceHashes=$hashes;exitCode=$null}
    Write-Host ('Prepared '+$item+' benchmark: '+$output)
    if(-not $CompileOnly){
        & $output $label $csv | Tee-Object -LiteralPath (Join-Path $benchmarkRoot ($label+'.log'))
        $record.exitCode=$LASTEXITCODE
    }
    $metadata.stages+=,$record
    $metadata|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $benchmarkRoot 'metadata.json') -Encoding UTF8
    if(-not $CompileOnly -and $record.exitCode -ne 0){throw ($item+' benchmark skipped or invalid; exit '+$record.exitCode+'. See '+$benchmarkRoot)}
}
Write-Host ('Results: '+$benchmarkRoot)
