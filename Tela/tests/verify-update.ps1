$ErrorActionPreference='Stop'
$workspace=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$folder=Join-Path $workspace 'output\TelaSync\0.1.1'
$patch=Join-Path $folder 'TelaSync-0.1.1-Patch.exe'
$full=Join-Path $folder 'TelaSync-0.1.1-Setup.exe'
$old=Join-Path $workspace 'releases\0.1.0\TelaSync-0.1.0-Setup.exe'
function Run-Package([string]$Exe,[string]$Mode,[string]$Target,[bool]$ShouldPass=$true){
 $p=Start-Process -FilePath $Exe -ArgumentList @($Mode,('"'+$Target+'"')) -WindowStyle Hidden -Wait -PassThru
 if(($p.ExitCode -eq 0) -ne $ShouldPass){if(Test-Path -LiteralPath ($Target+'.error.txt')){Get-Content -LiteralPath ($Target+'.error.txt')};throw "Unexpected result: $Mode, exit $($p.ExitCode)"}
}
Run-Package $patch '--verify' (Join-Path $PSScriptRoot 'patch-integrity.json')
Run-Package $full '--verify' (Join-Path $PSScriptRoot 'full-integrity.json')
$prefix='tela-installer-test-011-'+[Guid]::NewGuid().ToString('N')
$missing=Join-Path $PSScriptRoot ($prefix+'-missing')
Run-Package $patch '--test-install' $missing $false
if(Test-Path -LiteralPath $missing){throw 'Patch created a new installation'}
$stage=Join-Path $PSScriptRoot ($prefix+'-upgrade')
Run-Package $old '--test-install' $stage
$exe=Join-Path $stage 'TelaSync.exe'
$oldHash=(Get-FileHash -LiteralPath $exe).Hash
$config=Join-Path $stage 'TelaSync.exe.config'
Set-Content -LiteralPath $config -Value 'config-personalizada-preservar'
$configHash=(Get-FileHash -LiteralPath $config).Hash
$iconHash=(Get-FileHash -LiteralPath (Join-Path $stage 'app.ico')).Hash
$extra=Join-Path $stage 'preferencia-do-usuario.json'
Set-Content -LiteralPath $extra -Value '{"preservar":true}'
$extraHash=(Get-FileHash -LiteralPath $extra).Hash
$locked=[IO.File]::Open((Join-Path $stage 'LEIA-ME.txt'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
try{Run-Package $patch '--test-install' $stage $false}finally{$locked.Dispose()}
if((Get-FileHash -LiteralPath $exe).Hash -ne $oldHash){throw 'Failed patch did not restore original executable'}
if((Get-Content -LiteralPath (Join-Path $stage '.telasync-install.json') -Raw|ConvertFrom-Json).Version -ne '0.1.0'){throw 'Failed patch changed version marker'}
Run-Package $patch '--test-install' $stage
if((Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath (Join-Path $folder 'TelaSync.exe')).Hash){throw 'Updated executable differs'}
if((Get-FileHash -LiteralPath $config).Hash -ne $configHash){throw 'Patch overwrote unchanged personalized config'}
if((Get-FileHash -LiteralPath (Join-Path $stage 'app.ico')).Hash -ne $iconHash){throw 'Patch overwrote unchanged icon'}
if((Get-FileHash -LiteralPath $extra).Hash -ne $extraHash){throw 'Patch changed unknown user data'}
$manifest=Get-Content -LiteralPath (Join-Path $stage '.telasync-install.json') -Raw|ConvertFrom-Json
if($manifest.Version -ne '0.1.1' -or $manifest.Files.Count -ne 6){throw 'Installation manifest is incomplete'}
Run-Package $patch '--test-install' $stage
# Simulate an installed newer release: the updater must refuse a downgrade.
$manifest.Version='0.1.2'
$manifest|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $stage '.telasync-install.json') -Encoding utf8
Run-Package $patch '--test-install' $stage $false
$manifest.Version='0.1.1'
$manifest|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $stage '.telasync-install.json') -Encoding utf8
Run-Package $patch '--test-remove' $stage
if(-not(Test-Path -LiteralPath $extra)){throw 'User data was removed'}
foreach($name in @('TelaSync.exe','TelaSync.exe.config','app.ico','LEIA-ME.txt','Uninstall.exe','.telasync-install.json')){if(Test-Path -LiteralPath (Join-Path $stage $name)){throw "Uninstaller left $name"}}
$fresh=Join-Path $PSScriptRoot ($prefix+'-full')
Run-Package $full '--test-install' $fresh
Run-Package $full '--test-remove' $fresh
if(Test-Path -LiteralPath $fresh){throw 'Full installation removal left an empty directory'}
$result=[ordered]@{PatchUpdatesFrom='0.1.0';PatchUpdatesTo='0.1.1';PatchRefusesFreshInstall=$true;FailedPatchRestoresPreviousExecutable=$true;FailedPatchPreservesVersion=$true;UnchangedCustomConfigPreserved=$true;UnchangedIconPreserved=$true;UnknownUserDataPreserved=$true;RepeatedPatchIsSupported=$true;NewerVersionIsProtected=$true;PatchUninstallerRemovesAllOwnedFiles=$true;FullInstallAndRemovalPass=$true;RealUserInstallationModified=$false}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'update-test-results.json') -Encoding utf8
$result|ConvertTo-Json
