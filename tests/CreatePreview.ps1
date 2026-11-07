$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Xml.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll')|ForEach-Object{'/reference:'+(Join-Path $fx $_)}
$refs+=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')|ForEach-Object{'/reference:'+(Join-Path (Join-Path $fx 'WPF') $_)}
$sources=Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName
$assets=Join-Path $root 'assets'
$resources=Get-ChildItem -LiteralPath (Join-Path $assets 'cat') -File | Where-Object {$_.Extension -in @('.png','.txt')} | ForEach-Object{
    '/resource:'+ $_.FullName + ',GlassBongoCat.Assets.' + $_.FullName.Substring($assets.Length+1).Replace('\','.')
}
$output=Join-Path $root 'artifacts\CreatePreview.exe'
& (Join-Path $fx 'csc.exe') /nologo /codepage:65001 /target:exe /optimize+ /main:CreatePreview ('/out:'+$output) @refs @resources @sources (Join-Path $PSScriptRoot 'CreatePreview.cs')
if($LASTEXITCODE -ne 0){throw 'Preview compilation failed.'}
Push-Location $root
try{& $output;if($LASTEXITCODE -ne 0){throw 'Preview failed.'}}finally{Pop-Location}
