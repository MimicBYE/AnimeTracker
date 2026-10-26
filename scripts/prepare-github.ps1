[CmdletBinding()]
param([string]$OutputDirectory = '', [switch]$SkipWindowsBuild)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if (!$OutputDirectory) { $OutputDirectory = Join-Path (Split-Path $root -Parent) 'AnimeTracker-GitHub' }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output already exists; choose a new -OutputDirectory: $output" }
$separator = [IO.Path]::DirectorySeparatorChar
if ($output.TrimEnd('\','/') -eq $root -or $root.StartsWith($output.TrimEnd('\','/')+$separator,[StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($root+$separator,[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a new directory outside the source root.' }
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
[xml]$desktop = Get-Content -LiteralPath (Join-Path $root 'src/AnimeTracker.Desktop/AnimeTracker.Desktop.csproj') -Raw
$macVersion = [string]$desktop.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+(\.\d+)?$' -or $macVersion -ne $version) { throw 'Windows and macOS must use the same release version.' }
if (!$SkipWindowsBuild) { & (Join-Path $PSScriptRoot 'release-windows.ps1') }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$windows = Join-Path $root 'dist/windows'
$mac = Join-Path $root 'dist/macos'
$windowsNames = @("AnimeTracker-$version-windows-x64.zip", "AnimeTracker-$version-source.zip")
$macNames = @("AnimeTracker-$macVersion-macos-arm64.zip")
$forbidden = '(^|/)(data|covers|imports|artifacts|bin|obj|\.tools|\.git)(/|$)|(^|/)(library|watch-choices|widget-settings|view-settings|personal-order|arcs)\.xml$|(^|/)(startup-error|test-result|import-test)\.txt$|(^|/)\.env($|\.)|\.(pfx|p12|pem|key)$'

function Verify-Archive([string]$Directory,[string]$Name) {
    $path = Join-Path $Directory $Name
    if (!(Test-Path -LiteralPath $path)) { throw "Build the release first; missing $path" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = @(Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS.txt') | Where-Object { $_ -match ('^[0-9a-fA-F]{64}  ' + [regex]::Escape($Name) + '$') })
    if ($expected.Count -ne 1 -or $expected[0].Substring(0,64).ToLowerInvariant() -ne $hash) { throw "Checksum mismatch: $Name" }
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        foreach ($entry in $zip.Entries) {
            $entryName = $entry.FullName.Replace('\','/')
            if ($entryName -match $forbidden -or $entryName -match '(^|/)\.\.(/|$)' -or $entryName.StartsWith('/') -or $entryName.Contains(':')) { throw "Unexpected archive entry in ${Name}: $entryName" }
            $entryStream = $entry.Open(); try { $entryStream.CopyTo([IO.Stream]::Null) } finally { $entryStream.Dispose() }
        }
    } finally { $zip.Dispose() }
    return $hash
}
$hashes = @{}
foreach ($name in $windowsNames) { $hashes[$name] = Verify-Archive $windows $name }
foreach ($name in $macNames) { $hashes[$name] = Verify-Archive $mac $name }

$repository = Join-Path $output 'repository'
$releaseFolder = Join-Path $output ("releases/v" + $version)
foreach ($directory in @($repository,$releaseFolder)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
# Extract the verified source archive with an additional path boundary check.
$sourcePrefix = "AnimeTracker-$version-source/"
$sourceZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $windows $windowsNames[1]))
try {
    foreach ($entry in $sourceZip.Entries) {
        if (!$entry.Name) { continue }
        $entryName = $entry.FullName.Replace('\','/')
        if (!$entryName.StartsWith($sourcePrefix,[StringComparison]::Ordinal)) { throw 'Unexpected source archive root.' }
        $relative = $entryName.Substring($sourcePrefix.Length)
        if ($relative -eq 'BUILD-INFO.json') { continue } # Package provenance is not repository source.
        if ($relative -match '\.(exe|dll|pdb|zip|bak|tmp)$') { throw "Unexpected source binary: $relative" }
        $target = [IO.Path]::GetFullPath((Join-Path $repository $relative))
        if (!$target.StartsWith($repository+$separator,[StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escaped repository.' }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        $entryStream = $entry.Open(); $destination = [IO.File]::Create($target)
        try { $entryStream.CopyTo($destination) } finally { $entryStream.Dispose(); $destination.Dispose() }
    }
} finally { $sourceZip.Dispose() }

$utf8 = New-Object Text.UTF8Encoding($false)
function Write-Utf8([string]$Path,[string]$Value) { [IO.File]::WriteAllText($Path,$Value,$utf8) }
$allChecksums = @()
$releaseChecksums = @()
foreach ($pair in @(@{Names=$windowsNames;Source=$windows},@{Names=$macNames;Source=$mac})) {
    foreach ($name in $pair.Names) {
        Copy-Item -LiteralPath (Join-Path $pair.Source $name) -Destination (Join-Path $releaseFolder $name)
        $releaseChecksums += $hashes[$name] + '  ' + $name
        $allChecksums += $hashes[$name] + '  v' + $version + '/' + $name
    }
}
$releaseChecksums | Set-Content -LiteralPath (Join-Path $releaseFolder 'SHA256SUMS.txt') -Encoding ascii
$allChecksums | Set-Content -LiteralPath (Join-Path $output 'releases/SHA256SUMS.txt') -Encoding ascii
$changelog = Get-Content -LiteralPath (Join-Path $repository 'docs/RELEASE-NOTES.md') -Raw -Encoding UTF8
Write-Utf8 (Join-Path $releaseFolder 'RELEASE-NOTES.md') $changelog
$instructions = @"
# Anime Tracker — папка для GitHub

1. repository/ — исходники для репозитория. Загружайте содержимое этой папки, включая .gitignore и .gitattributes.
2. releases/v$version/ — вложения единого релиза v${version}: Windows ZIP, macOS ZIP и архив исходников. Текст описания — RELEASE-NOTES.md.
3. SHA256SUMS.txt содержит контрольные суммы всех трёх ZIP-файлов. Общий список с относительными путями — releases/SHA256SUMS.txt.

В исходный репозиторий входит только repository/; готовые ZIP предназначены для вложений Releases.

Лицензия исходного кода не назначена, как было решено ранее. Атрибуция сторонних материалов сохранена.

verification.json содержит результаты проверки. Публикация на GitHub не выполнялась. Ваше рабочее приложение и папка data не изменялись.
"@
Write-Utf8 (Join-Path $output 'START_HERE.md') $instructions

$files = @(Get-ChildItem -LiteralPath $repository -File -Recurse -Force)
if (@($files | Where-Object { $_.FullName.Substring($repository.Length+1).Replace('\','/') -match $forbidden }).Count) { throw 'Private or generated files found in prepared repository.' }
$secretPattern = 'github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|AKIA[A-Z0-9]{16}|sk-[A-Za-z0-9_-]{32,}|C:[\\/]+Users[\\/]+|C:[\\/]+Codex[\\/]+VibeCode'
foreach ($file in $files | Where-Object { $_.Extension -notin @('.ico','.icns','.png') }) {
    $text = [IO.File]::ReadAllText($file.FullName)
    # Form feeds are retained in upstream license notices.
    if ($text -match $secretPattern -or $text -match '[\x00-\x08\x0B\x0E-\x1F]') { throw ('Review a possible secret, local path or control character: ' + $file.FullName.Substring($repository.Length+1)) }
}
foreach ($file in $files | Where-Object { $_.Extension -eq '.md' }) {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName),'\]\(([^)\s]+)\)')) {
        $link = $match.Groups[1].Value
        if ($link -match '^[a-zA-Z]+:|^#') { continue }
        $target = Join-Path $file.DirectoryName (($link -split '#',2)[0])
        if (!(Test-Path -LiteralPath $target)) { throw ('Broken documentation link: ' + $link + ' in ' + $file.Name) }
    }
}
$report = [ordered]@{ version=$version; windowsVersion=$version; macVersion=$macVersion; sourceFileCount=$files.Count; archiveReadability='PASS'; sha256='PASS'; privateDataExcluded='PASS'; generatedFilesExcluded='PASS'; secretAndLocalPathPatternScan='PASS'; localMarkdownLinks='PASS'; ownSourceLicense='not assigned'; macNativeLaunch='not tested'; releaseKind='release'; published=$false }
Write-Utf8 (Join-Path $output 'verification.json') ($report | ConvertTo-Json -Depth 4)
Write-Output "GitHub directory ready: $output"
Write-Output ("PASS: {0} source files, 3 archives, checksums, documentation links, no private/build data." -f $files.Count)
