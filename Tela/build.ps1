param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\output\TelaSync'))
$ErrorActionPreference = 'Stop'
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Compilador C# do .NET Framework não encontrado.' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$product = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'product.json') -Raw | ConvertFrom-Json
if ($product.executable -notmatch '^[a-zA-Z0-9_-]+\.exe$' -or $product.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Nome de executável ou versão inválidos.' }
function Escape-CSharp([string]$value) { $value.Replace('\','\\').Replace('"','\"') }
$generated = @"
namespace Tela { public static class ProductInfo {
 public const string Name = "$(Escape-CSharp $product.name)";
 public const string Version = "$(Escape-CSharp $product.version)";
 public const string PatchFrom = "$(Escape-CSharp $product.patchFrom)";
 public const string AssemblyVersion = "$(Escape-CSharp $product.version).0";
 public const string Executable = "$(Escape-CSharp $product.executable)";
 public const string AppId = "$(Escape-CSharp $product.appId)";
 public const string Publisher = "$(Escape-CSharp $product.publisher)";
 public const string Signature = "By $(Escape-CSharp $product.publisher)";
} }
"@
$generated | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'src\ProductInfo.cs') -Encoding utf8
$compileArguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/warn:4',('/out:' + (Join-Path $outputPath $product.executable)),('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:' + (Join-Path $PSScriptRoot 'assets\app.ico')),('/resource:' + (Join-Path $PSScriptRoot 'assets\app-icon.png') + ',Tela.Icon.png'),('/resource:' + (Join-Path $PSScriptRoot 'assets\signature.png') + ',Tela.Signature.png'),('/resource:' + (Join-Path $PSScriptRoot 'src\MainWindow.xaml') + ',Tela.MainWindow.xaml'))
foreach ($assemblyName in @('System.dll','System.Core.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\UIAutomationTypes.dll')) { $compileArguments += '/reference:' + (Join-Path $frameworkDir $assemblyName) }
$compileArguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
$compileArguments += '/resource:' + (Join-Path $PSScriptRoot 'src\Theme.xaml') + ',Tela.Theme.xaml'
$compileArguments += '/resource:' + (Join-Path $PSScriptRoot 'assets\app.ico') + ',Tela.Icon.ico'
$compileArguments += '/reference:' + (Join-Path $frameworkDir 'System.Windows.Forms.dll')
$compileArguments += '/reference:' + (Join-Path $frameworkDir 'System.Drawing.dll')
& $compilerPath $compileArguments
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Tela.exe.config') -Destination (Join-Path $outputPath ($product.executable + '.config')) -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\app.ico') -Destination (Join-Path $outputPath 'app.ico') -Force
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LEIA-ME.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LEIA-ME.txt') -Destination $outputPath -Force }
Get-Item -LiteralPath (Join-Path $outputPath $product.executable) | Select-Object FullName,Length,LastWriteTime
