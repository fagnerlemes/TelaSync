param(
 [string]$OutputDirectory,
 [string]$BaselineDirectory
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$product=Get-Content -LiteralPath (Join-Path $projectRoot 'product.json') -Raw|ConvertFrom-Json
if([string]::IsNullOrWhiteSpace($OutputDirectory)){$OutputDirectory=Join-Path $projectRoot ('..\output\TelaSync\'+$product.version)}
if([string]::IsNullOrWhiteSpace($BaselineDirectory)){$BaselineDirectory=Join-Path $projectRoot ('..\releases\'+$product.patchFrom)}
$outputPath=[IO.Path]::GetFullPath($OutputDirectory)
$baselinePath=[IO.Path]::GetFullPath($BaselineDirectory)
& (Join-Path $PSScriptRoot 'build-installer.ps1') -OutputDirectory $outputPath
$items=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-manifest.json') -Raw|ConvertFrom-Json
$changed=@($items|Where-Object{ $old=Join-Path $baselinePath $_.Name; -not(Test-Path -LiteralPath $old) -or (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash.ToLowerInvariant() -ne $_.Hash })
if($changed.Count -eq 0){throw 'Nenhum arquivo mudou em relação à versão de base.'}
$manifest=Join-Path $PSScriptRoot 'patch-manifest.json'
ConvertTo-Json -InputObject $changed -Depth 5|Set-Content -LiteralPath $manifest -Encoding utf8
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$patch=Join-Path $outputPath ($product.name+'-'+$product.version+'-Patch.exe')
$arguments=@('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/define:PATCH',('/out:'+$patch),('/win32manifest:'+(Join-Path $projectRoot 'app.manifest')),('/win32icon:'+(Join-Path $projectRoot 'assets\app.ico')),('/resource:'+$manifest+',Payload.Manifest'),('/resource:'+(Join-Path $projectRoot 'assets\app-icon.png')+',Brand.Icon'),('/resource:'+(Join-Path $projectRoot 'assets\signature.png')+',Brand.Signature'))
foreach($item in $changed){$arguments+='/resource:'+(Join-Path $outputPath $item.Name)+','+$item.Resource}
foreach($name in @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')){$arguments+='/reference:'+(Join-Path $framework $name)}
$arguments+=(Join-Path $projectRoot 'src\ProductInfo.cs')
$arguments+=(Join-Path $PSScriptRoot 'Setup.cs')
& (Join-Path $framework 'csc.exe') $arguments
if($LASTEXITCODE -ne 0){throw 'Falha ao compilar o patch.'}
Get-Item -LiteralPath $patch|Select-Object FullName,Length
