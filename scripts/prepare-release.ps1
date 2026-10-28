[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$PackageVersion,[ValidateSet('local','stable','dev')][string]$Channel='local',[string]$Commit='local')
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
$root=Split-Path $PSScriptRoot -Parent
Test-RepositoryVersion $root
$baseVersion=Get-AppVersion $root
$info=New-BuildInfo $baseVersion $PackageVersion $Channel $Commit
$archives=@(Get-ChildItem -LiteralPath $InputDirectory -File -Recurse | Where-Object {$_.Name.EndsWith('.zip') -or $_.Name.EndsWith('.tar.gz')})
if($archives.Count -ne 4 -or @($archives.Name|Sort-Object -Unique).Count -ne 4){throw 'Expected exactly four distinct release archives.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new output directory to avoid stale release files.'}
$null=New-Item -ItemType Directory -Path $OutputDirectory
foreach($archive in $archives){Copy-Item -LiteralPath $archive.FullName -Destination (Join-Path $OutputDirectory $archive.Name)}
Test-ReleasePackages $OutputDirectory $baseVersion $PackageVersion $Channel $Commit
$assets=@(Get-ReleaseAssets $OutputDirectory $PackageVersion)
$assets|ForEach-Object{$_.Sha256+'  '+$_.Name}|Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'BUILD-INFO.json'),($info|ConvertTo-Json),$utf8)
$notesPath=Join-Path $root ("docs/releases/$baseVersion.md")
if($Channel -eq 'dev' -or !(Test-Path -LiteralPath $notesPath -PathType Leaf)){$notesPath=Join-Path $root 'docs/RELEASE-NOTES.md'}
$notes=[IO.File]::ReadAllText($notesPath)
if($Channel -eq 'dev'){$notes="# Anime Tracker $PackageVersion`n`nDevelopment build from dev. Commit: $Commit. Application version: $baseVersion.`n`n"+$notes}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'RELEASE-NOTES.md'),$notes,$utf8)
Write-Output "PASS: $PackageVersion, four archives, matching build provenance, private data excluded, ARM64/ELF x64 executable mode 0755, SHA-256."
