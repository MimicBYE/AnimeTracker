[CmdletBinding()]
param([Parameter(Mandatory)][string]$InputDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$PackageVersion,[ValidateSet('local','stable','dev')][string]$Channel='local',[string]$Commit='local')
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
$root=Split-Path $PSScriptRoot -Parent
Test-RepositoryVersion $root
$baseVersion=Get-AppVersion $root
$info=New-BuildInfo $baseVersion $PackageVersion $Channel $Commit
$archives=@(Get-ChildItem -LiteralPath $InputDirectory -Filter '*.zip' -File -Recurse)
if($archives.Count -ne 3 -or @($archives.Name|Sort-Object -Unique).Count -ne 3){throw 'Expected exactly three distinct release archives.'}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new output directory to avoid stale release files.'}
$null=New-Item -ItemType Directory -Path $OutputDirectory
foreach($archive in $archives){Copy-Item -LiteralPath $archive.FullName -Destination (Join-Path $OutputDirectory $archive.Name)}
Test-ReleasePackages $OutputDirectory $baseVersion $PackageVersion $Channel $Commit
$assets=@(Get-ReleaseAssets $OutputDirectory $PackageVersion)
$assets|ForEach-Object{$_.Sha256+'  '+$_.Name}|Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'BUILD-INFO.json'),($info|ConvertTo-Json),$utf8)
$notes=[IO.File]::ReadAllText((Join-Path $root 'docs/RELEASE-NOTES.md'))
if($Channel -eq 'dev'){$notes="# Anime Tracker $PackageVersion`n`nDevelopment build from dev. Commit: $Commit. Application version: $baseVersion.`n`n"+$notes}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'RELEASE-NOTES.md'),$notes,$utf8)
Write-Output "PASS: $PackageVersion, three archives, matching build provenance, private data excluded, ARM64 executable mode 0755, SHA-256."
