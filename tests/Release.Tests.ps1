$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
Import-Module (Join-Path $root 'scripts/ReleaseTools.psm1') -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$artifacts=[IO.Path]::GetFullPath((Join-Path $root 'artifacts/release-tests'))
$scratch=Join-Path $artifacts ([Guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $scratch -Force
$sha='a'*40;$other='b'*40;$base=Get-AppVersion $root
function Assert([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Reject([scriptblock]$Action,[string]$Message){$rejected=$false;try{& $Action}catch{$rejected=$true};Assert $rejected $Message}
function Write-Fixture([string]$Root,[string]$Name,[string]$Text='fixture'){
    $path=Join-Path $Root $Name;$null=New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force
    [IO.File]::WriteAllText($path,$Text,[Text.UTF8Encoding]::new($false))
}
function Add-Entry($Zip,[string]$Name,[byte[]]$Bytes,[bool]$Executable=$false){
    $entry=$Zip.CreateEntry($Name);$entry.ExternalAttributes=if($Executable){0x81ed -shl 16}else{0x81a4 -shl 16}
    $stream=$entry.Open();try{$stream.Write($Bytes,0,$Bytes.Length)}finally{$stream.Dispose()}
}
function New-Packages([string]$Directory,[string]$Version=$base,[string]$Channel='stable'){
    $null=New-Item -ItemType Directory -Path $Directory -Force
    $info=[Text.Encoding]::UTF8.GetBytes(((New-BuildInfo $base $Version $Channel $sha)|ConvertTo-Json))
    foreach($kind in @('windows-x64','macos-arm64','source')){
        $zip=[IO.Compression.ZipFile]::Open((Join-Path $Directory "AnimeTracker-$Version-$kind.zip"),[IO.Compression.ZipArchiveMode]::Create)
        try{
            $prefix=if($kind -eq 'macos-arm64'){''}else{"AnimeTracker-$Version-$kind/"}
            Add-Entry $zip ($prefix+'BUILD-INFO.json') $info
            $required=if($kind -eq 'windows-x64'){@('AnimeTracker.exe','AnimeTracker.exe.config','README.md')}elseif($kind -eq 'source'){@('README.md','VERSION','AGENTS.md','.github/workflows/ci.yml','scripts/ReleaseTools.psm1')}else{@('Anime Tracker.app/Contents/Info.plist','Anime Tracker.app/Contents/Resources/BUILD-INFO.json')}
            foreach($name in $required){Add-Entry $zip ($prefix+$name) ([Text.Encoding]::UTF8.GetBytes('fixture'))}
            if($kind -eq 'macos-arm64'){Add-Entry $zip 'Anime Tracker.app/Contents/MacOS/AnimeTracker' ([byte[]]@(0xcf,0xfa,0xed,0xfe,0x0c,0,0,1)) $true}
        }finally{$zip.Dispose()}
    }
}
try{
    Test-RepositoryVersion $root
    foreach($case in @(@('AT-1','push'),@('main','pull_request'),@('dev','pull_request'),@('main','workflow_dispatch'))){$plan=Get-ReleaseContext $base $case[0] $case[1] $sha 42;Assert (!$plan.Publish) 'Untrusted/manual event may publish'}
    $plan=Get-ReleaseContext $base 'main' 'push' $sha 42;Assert ($plan.Publish -and !$plan.Prerelease -and $plan.Tag -eq "v$base") 'Stable release context'
    $plan=Get-ReleaseContext $base 'main' 'push' $sha 42 $other;Assert (!$plan.Publish) 'Existing version was not skipped'
    $plan=Get-ReleaseContext $base 'main' 'push' $sha 42 $sha;Assert $plan.Publish 'Same commit retry should be permitted'
    $dev=Get-ReleaseContext $base 'dev' 'push' $sha 42;Assert ($dev.Publish -and $dev.Prerelease -and $dev.PackageVersion.EndsWith('-dev.42')) 'Development release context'
    Reject {Get-ReleaseContext $base 'dev' 'push' $sha 0} 'Development release accepted a missing run number'
    Reject {New-BuildInfo $base '../bad' 'stable' $sha} 'Unsafe package version accepted'
    Assert ((Get-PublicationAction $sha '' $null) -eq 'CreateDraft') 'Missing release action'
    Assert ((Get-PublicationAction $sha $other $null) -eq 'SkipOtherCommit') 'Existing tag could be moved'
    Assert ((Get-PublicationAction $sha $sha ([pscustomobject]@{target_commitish=$sha;draft=$false})) -eq 'AlreadyPublished') 'Published release could be changed'
    Assert ((Get-PublicationAction $sha $sha ([pscustomobject]@{target_commitish=$sha;draft=$true})) -eq 'ResumeDraft') 'Draft retry action'
    Reject {Get-PublicationAction $sha '' ([pscustomobject]@{target_commitish=$other;draft=$true})} 'Conflicting draft accepted'
    Test-ExistingAsset ([pscustomobject]@{state='uploaded';size=12;digest=('sha256:'+$sha)}) 12 $sha
    Reject {Test-ExistingAsset ([pscustomobject]@{state='uploaded';size=12;digest=('sha256:'+$other)}) 12 $sha} 'Conflicting asset accepted'
    $source=Join-Path $scratch 'source';Write-Fixture $source 'README.md';Write-Fixture $source 'AGENTS.md';Write-Fixture $source 'SECURITY.md';Write-Fixture $source 'Directory.Build.props';Write-Fixture $source '.github/workflows/ci.yml';Write-Fixture $source 'scripts/example.psm1';Write-Fixture $source 'tests/Example.ps1'
    foreach($name in @('data/library.xml','src/obj/leak.cs','src/bin/leak.cs','artifacts/secrets.txt','.tools/sdk.json','.github/.env')){Write-Fixture $source $name}
    $files=@(Get-SourceFiles $source);Assert ($files.Count -eq 7 -and $files -contains '.github/workflows/ci.yml' -and $files -contains 'AGENTS.md' -and $files -contains 'SECURITY.md' -and $files -contains 'Directory.Build.props' -and $files -contains 'tests/Example.ps1') 'Source inventory leaked private/generated files or lost infrastructure/security policy'
    $metadata=Join-Path $scratch 'metadata'
    foreach($name in @('VERSION','src/AnimeTracker.Windows/Version.cs','src/AnimeTracker.Windows/Assets/app.manifest','src/AnimeTracker.Core/AnimeTracker.Core.csproj','src/AnimeTracker.Desktop/AnimeTracker.Desktop.csproj','src/AnimeTracker.Desktop/Assets/Info.plist')){Write-Fixture $metadata $name ([IO.File]::ReadAllText((Join-Path $root $name)))}
    & (Join-Path $root 'scripts/set-version.ps1') -Version '1.2.3' -Root $metadata
    Assert ((Get-AppVersion $metadata) -eq '1.2.3') 'Version helper did not update VERSION';Test-RepositoryVersion $metadata
    $before=(Get-FileHash (Join-Path $metadata 'VERSION')).Hash
    Reject {& (Join-Path $root 'scripts/set-version.ps1') -Version '1.2-alpha' -Root $metadata} 'Version helper accepted a release channel'
    Assert ((Get-FileHash (Join-Path $metadata 'VERSION')).Hash -eq $before) 'Invalid version changed files'
    $packages=Join-Path $scratch 'packages';New-Packages $packages
    Test-ReleasePackages $packages $base $base 'stable' $sha
    Reject {Test-ReleasePackages $packages $base $base 'stable' $other} 'Artifacts from another commit accepted'
    Write-Fixture $packages 'stale.zip';Reject {Get-ReleaseAssets $packages $base} 'Stale archive accepted';Remove-Item -LiteralPath (Join-Path $packages 'stale.zip')
    $bad=Join-Path $scratch 'bad';New-Packages $bad
    $zip=[IO.Compression.ZipFile]::Open((Join-Path $bad "AnimeTracker-$base-source.zip"),[IO.Compression.ZipArchiveMode]::Update)
    try{Add-Entry $zip "AnimeTracker-$base-source/data/library.xml" ([byte[]]@(1))}finally{$zip.Dispose()}
    Reject {Test-ReleasePackages $bad $base $base 'stable' $sha} 'Private library accepted'
    $unsafe=Join-Path $scratch 'unsafe';New-Packages $unsafe
    $zip=[IO.Compression.ZipFile]::Open((Join-Path $unsafe "AnimeTracker-$base-source.zip"),[IO.Compression.ZipArchiveMode]::Update)
    try{Add-Entry $zip '../outside.txt' ([byte[]]@(1))}finally{$zip.Dispose()}
    Reject {Test-ReleasePackages $unsafe $base $base 'stable' $sha} 'ZIP path traversal accepted'
    $noexec=Join-Path $scratch 'noexec';New-Packages $noexec
    $zip=[IO.Compression.ZipFile]::Open((Join-Path $noexec "AnimeTracker-$base-macos-arm64.zip"),[IO.Compression.ZipArchiveMode]::Update)
    try{$zip.GetEntry('Anime Tracker.app/Contents/MacOS/AnimeTracker').ExternalAttributes=0x81a4 -shl 16}finally{$zip.Dispose()}
    Reject {Test-ReleasePackages $noexec $base $base 'stable' $sha} 'Non-executable Mac app accepted'
    $development=Join-Path $scratch 'development';New-Packages $development $dev.PackageVersion 'dev';Test-ReleasePackages $development $base $dev.PackageVersion 'dev' $sha
    Write-Output 'PASS: release channels, PR/manual guards, immutable tags/assets, source privacy, version synchronization, provenance, stale ZIPs, path traversal, Mac permissions and development packages.'
}finally{
    $cleanup=[IO.Path]::GetFullPath($scratch)
    if(!$cleanup.StartsWith($artifacts+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup path escaped test artifacts'}
    if(Test-Path -LiteralPath $cleanup){Remove-Item -LiteralPath $cleanup -Recurse -Force}
}
