[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
if(-not $SkipBuild){& (Join-Path $PSScriptRoot 'build.ps1')}
$exe=Join-Path $PSScriptRoot 'dist\GlassBongoCat.exe'
$checksum=Join-Path $PSScriptRoot 'dist\SHA256SUMS.txt'
$hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
[IO.File]::WriteAllText($checksum,$hash+'  GlassBongoCat.exe'+[Environment]::NewLine,(New-Object Text.UTF8Encoding($false)))
$portable=Join-Path $PSScriptRoot 'dist\GlassBongoCat-0.1.0-win-x64.zip'
$files=@($exe,$checksum)+(@('README.md','LICENSE','THIRD_PARTY_NOTICES.md','RELEASE_NOTES.md','VERIFICATION.md')|ForEach-Object{Join-Path $PSScriptRoot $_})
Compress-Archive -LiteralPath $files -DestinationPath $portable -CompressionLevel Optimal -Force
if((Get-Item -LiteralPath $portable).Length -ge 5MB){throw 'Portable archive exceeds 5 MiB.'}
$source=Join-Path $PSScriptRoot 'dist\GlassBongoCat-0.1.0-source.zip'
$sourceFiles=@('src','assets','tests','verification','build.ps1','package.ps1','README.md','LICENSE','THIRD_PARTY_NOTICES.md','RELEASE_NOTES.md','VERIFICATION.md')|ForEach-Object{Join-Path $PSScriptRoot $_}
Compress-Archive -LiteralPath $sourceFiles -DestinationPath $source -CompressionLevel Optimal -Force
$lines=@($hash+'  GlassBongoCat.exe')
foreach($file in @($portable,$source)){$lines+=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($file)}
[IO.File]::WriteAllLines($checksum,$lines,(New-Object Text.UTF8Encoding($false)))
Get-Item -LiteralPath $exe,$portable,$source | Select-Object Name,Length
