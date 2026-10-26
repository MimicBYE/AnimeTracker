[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version,[string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)(\.(0|[1-9]\d*))?$') { throw 'Use major.minor or major.minor.patch without a channel suffix.' }
foreach ($part in $Version.Split('.')) { if ([long]$part -gt 65534) { throw 'Version components must not exceed 65534.' } }
Test-RepositoryVersion $Root
$assembly=Get-AssemblyVersion $Version
$files=@{}
$path=Join-Path $Root 'src/AnimeTracker.Windows/Version.cs'
$text=[IO.File]::ReadAllText($path)
foreach ($attribute in @('AssemblyVersion','AssemblyFileVersion')) { $text=[regex]::Replace($text,($attribute+'\("[^"]+"\)'),($attribute+'("'+$assembly+'")')) }
$files[$path]=[regex]::Replace($text,'AssemblyInformationalVersion\("[^"]+"\)',('AssemblyInformationalVersion("'+$Version+'")'))
$path=Join-Path $Root 'src/AnimeTracker.Windows/Assets/app.manifest';$files[$path]=[regex]::Replace([IO.File]::ReadAllText($path),'(<assemblyIdentity\s+version=")[^"]+"',('${1}'+$assembly+'"'))
foreach ($project in @('AnimeTracker.Core','AnimeTracker.Desktop')) {
    $path=Join-Path $Root ("src/$project/$project.csproj");$text=[IO.File]::ReadAllText($path)
    $text=[regex]::Replace($text,'<Version>[^<]+</Version>',('<Version>'+$Version+'</Version>'))
    $files[$path]=[regex]::Replace($text,'<InformationalVersion>[^<]+</InformationalVersion>',('<InformationalVersion>'+$Version+'</InformationalVersion>'))
}
$path=Join-Path $Root 'src/AnimeTracker.Desktop/Assets/Info.plist'
$files[$path]=[regex]::Replace([IO.File]::ReadAllText($path),'(<key>CFBundleShortVersionString</key>\s*<string>)[^<]+</string>',('${1}'+$Version+'</string>'))
$files[(Join-Path $Root 'VERSION')]=$Version+"`n"
foreach ($path in $files.Keys) { [IO.File]::WriteAllText($path,$files[$path],[Text.UTF8Encoding]::new($false)) }
Test-RepositoryVersion $Root
Write-Output "Application version: $Version (both platforms). Update release notes before merging to main."
