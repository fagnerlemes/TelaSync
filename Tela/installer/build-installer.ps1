param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\output\TelaSync'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $projectRoot 'build.ps1') -OutputDirectory $outputPath
$product = Get-Content -LiteralPath (Join-Path $projectRoot 'product.json') -Raw | ConvertFrom-Json
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$items = @(
 @{Name=$product.executable;Resource='Payload.App'},
 @{Name=($product.executable + '.config');Resource='Payload.Config'},
 @{Name='app.ico';Resource='Payload.Icon'},
 @{Name='LEIA-ME.txt';Resource='Payload.Readme'}
)
foreach($item in $items) { $item.Hash = (Get-FileHash -LiteralPath (Join-Path $outputPath $item.Name) -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifestPath = Join-Path $PSScriptRoot 'payload-manifest.json'
$items | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$setupName = $product.name + '-' + $product.version + '-Setup.exe'
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/warn:4',('/out:' + (Join-Path $outputPath $setupName)),('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),('/win32icon:' + (Join-Path $projectRoot 'assets\app.ico')),('/resource:' + $manifestPath + ',Payload.Manifest'),('/resource:' + (Join-Path $projectRoot 'assets\app-icon.png') + ',Brand.Icon'),('/resource:' + (Join-Path $projectRoot 'assets\signature.png') + ',Brand.Signature'))
foreach($item in $items) { $arguments += '/resource:' + (Join-Path $outputPath $item.Name) + ',' + $item.Resource }
foreach($assemblyName in @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')) { $arguments += '/reference:' + (Join-Path $frameworkDir $assemblyName) }
$arguments += (Join-Path $projectRoot 'src\ProductInfo.cs')
$arguments += (Join-Path $PSScriptRoot 'Setup.cs')
& (Join-Path $frameworkDir 'csc.exe') $arguments
if($LASTEXITCODE -ne 0) { throw 'A compilação do instalador falhou.' }
Get-Item -LiteralPath (Join-Path $outputPath $setupName) | Select-Object FullName,Length,LastWriteTime
