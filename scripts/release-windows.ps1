[CmdletBinding()]
param([string]$PackageVersion='', [ValidateSet('local','stable','dev')][string]$Channel='local', [string]$Commit='local')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
Test-RepositoryVersion $root
$version = Get-AppVersion $root
if (!$PackageVersion) { $PackageVersion=$version }
$buildInfo=New-BuildInfo $version $PackageVersion $Channel $Commit
$assemblyVersion = Get-AssemblyVersion $version
# Keep extracted self-test paths below the legacy .NET Framework path limit.
$run = Join-Path $root ('artifacts\release\' + [Guid]::NewGuid().ToString('N').Substring(0,12))
$dist = Join-Path $root 'dist/windows'
$binaryName = 'AnimeTracker-' + $PackageVersion + '-windows-x64'
$sourceName = 'AnimeTracker-' + $PackageVersion + '-source'
$binary = Join-Path $run $binaryName
$source = Join-Path $run $sourceName
foreach ($directory in @($binary,$source,$dist)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

function Invoke-Check([string]$Executable,[string]$Argument,[string]$Report) {
    $reportPath = Join-Path ([IO.Path]::GetDirectoryName($Executable)) $Report
    $process = Start-Process -FilePath $Executable -ArgumentList $Argument -WindowStyle Hidden -PassThru
    $handle = $process.Handle
    if (!$process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw "Timed out: $Argument" }
    if (!(Test-Path -LiteralPath $reportPath)) { throw "Missing test report: $Report" }
    $result = Get-Content -LiteralPath $reportPath -Raw
    if ($process.ExitCode -ne 0 -or !$result.StartsWith('PASS:') -or $result.Contains('FAIL')) { throw ("Check failed ({0}, exit {1}): {2}" -f $Argument,$process.ExitCode,$result) }
    Write-Output ("PASS {0}" -f $Argument)
}

function Assert-Zip([string]$Archive,[string]$Folder,[string[]]$Expected) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $actual = @($zip.Entries | Where-Object { $_.Name.Length -gt 0 } | ForEach-Object { $_.FullName.Replace('\','/') })
        $wanted = @($Expected | ForEach-Object { $Folder + '/' + $_.Replace('\','/') })
        $difference = @(Compare-Object -ReferenceObject ($wanted | Sort-Object) -DifferenceObject ($actual | Sort-Object))
        if ($difference.Count -gt 0) { throw ('Unexpected archive contents: ' + ($difference | Out-String)) }
        if (@($actual | Where-Object { $_ -match '(^|/)(data|test-[^/]*|artifacts|bin|obj|\.tools)(/|$)' }).Count -gt 0) { throw 'Private or build data found in an archive.' }
    } finally { $zip.Dispose() }
}

Write-Output "Building release $version"
& (Join-Path $PSScriptRoot 'build-windows.ps1') -OutputPath (Join-Path $binary 'AnimeTracker.exe') -Platform x64
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $binary 'AnimeTracker.exe'))
if ($fileVersion.FileVersion -ne $assemblyVersion -or $fileVersion.ProductVersion -ne $version) { throw 'Assembly version does not match VERSION.' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'src/AnimeTracker.Windows/Assets/app.manifest') -Raw
if ($manifest.assembly.assemblyIdentity.version -ne $assemblyVersion) { throw 'Manifest version does not match VERSION.' }

$documentation = @('README.md','VERSION','SECURITY.md','AGENTS.md','CONTRIBUTING.md') + @(Get-ChildItem -LiteralPath (Join-Path $root 'docs') -File -Recurse | Where-Object {$_.Extension -in @('.md','.txt')} | ForEach-Object {$_.FullName.Substring($root.Length+1).Replace('\','/')})
foreach ($file in $documentation) { $target=Join-Path $binary $file;New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force|Out-Null;Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target }
$binaryFiles = @('AnimeTracker.exe','AnimeTracker.exe.config') + $documentation
$sourceFiles = @(Get-SourceFiles $root)
foreach ($file in $sourceFiles) {
    $target = Join-Path $source $file
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
}
$buildJson=$buildInfo|ConvertTo-Json
foreach($folder in @($binary,$source)){[IO.File]::WriteAllText((Join-Path $folder 'BUILD-INFO.json'),$buildJson,[Text.UTF8Encoding]::new($false))}
$binaryFiles+='BUILD-INFO.json';$sourceFiles+='BUILD-INFO.json'

Add-Type -AssemblyName System.IO.Compression.FileSystem
$binaryZip = Join-Path $dist ($binaryName + '.zip')
$sourceZip = Join-Path $dist ($sourceName + '.zip')
Compress-Archive -LiteralPath $binary -DestinationPath $binaryZip -CompressionLevel Optimal -Force
Compress-Archive -LiteralPath $source -DestinationPath $sourceZip -CompressionLevel Optimal -Force
Assert-Zip $binaryZip $binaryName $binaryFiles
Assert-Zip $sourceZip $sourceName $sourceFiles

# Test the actual distributed files after extraction, keeping generated test data out of the ZIP.
$verifyBinary = Join-Path $run 'verify-binary'
Expand-Archive -LiteralPath $binaryZip -DestinationPath $verifyBinary
$distributedExe = Join-Path (Join-Path $verifyBinary $binaryName) 'AnimeTracker.exe'
Invoke-Check $distributedExe '--self-test' 'test-result.txt'
Invoke-Check $distributedExe '--import-test' 'import-test.txt'

# Rebuild only from the extracted source ZIP, with no files from the working checkout.
$verifySource = Join-Path $run 'verify-source'
Expand-Archive -LiteralPath $sourceZip -DestinationPath $verifySource
$extractedRoot = Join-Path $verifySource $sourceName
$rebuilt = Join-Path $run 'rebuilt\AnimeTracker.exe'
& (Join-Path $extractedRoot 'scripts/build-windows.ps1') -OutputPath $rebuilt -Platform x64
Invoke-Check $rebuilt '--self-test' 'test-result.txt'

$checksums = foreach ($file in @($binaryZip,$sourceZip)) { $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256; $hash.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
$checksums | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Copy-Item -LiteralPath (Join-Path $root 'docs/RELEASE-NOTES.md') -Destination (Join-Path $dist 'RELEASE-NOTES.md') -Force
$report = [ordered]@{ version=$version; packageVersion=$PackageVersion; channel=$Channel; commit=$Commit; platform='windows-x64'; cleanArchiveContents='PASS'; distributedSelfTest='PASS'; distributedImportTest='PASS'; sourceRebuild='PASS'; rebuiltSelfTest='PASS'; binaryFiles=$binaryFiles; sourceFiles=$sourceFiles }
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'verification.json') -Encoding utf8
Write-Output ("Release ready: {0}" -f $dist)
Write-Output ("Verification: {0}" -f (Join-Path $run 'verification.json'))
