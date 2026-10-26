Set-StrictMode -Version Latest

function Get-AppVersion {
    param([Parameter(Mandatory)][string]$Root)
    $version = [IO.File]::ReadAllText((Join-Path $Root 'VERSION')).Trim()
    if ($version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)(\.(0|[1-9]\d*))?$') { throw 'VERSION must contain major.minor or major.minor.patch.' }
    foreach ($part in $version.Split('.')) { if ([long]$part -gt 65534) { throw 'Version components must not exceed 65534.' } }
    return $version
}

function Get-AssemblyVersion {
    param([Parameter(Mandatory)][string]$Version)
    if ($Version.Split('.').Count -eq 2) { return $Version + '.0.0' }
    return $Version + '.0'
}

function Test-RepositoryVersion {
    param([Parameter(Mandatory)][string]$Root)
    $version = Get-AppVersion $Root
    $assembly = Get-AssemblyVersion $version
    $windows = [IO.File]::ReadAllText((Join-Path $Root 'src/AnimeTracker.Windows/Version.cs'))
    foreach ($attribute in @('AssemblyVersion','AssemblyFileVersion')) {
        if ($windows -notmatch ($attribute + '\("' + [regex]::Escape($assembly) + '"\)')) { throw "$attribute does not match VERSION." }
    }
    if ($windows -notmatch ('AssemblyInformationalVersion\("' + [regex]::Escape($version) + '"\)')) { throw 'Windows informational version does not match VERSION.' }
    [xml]$manifest = [IO.File]::ReadAllText((Join-Path $Root 'src/AnimeTracker.Windows/Assets/app.manifest'))
    if ($manifest.assembly.assemblyIdentity.version -ne $assembly) { throw 'Windows manifest does not match VERSION.' }
    foreach ($project in @('AnimeTracker.Core','AnimeTracker.Desktop')) {
        [xml]$xml = [IO.File]::ReadAllText((Join-Path $Root ("src/$project/$project.csproj")))
        if ([string]$xml.Project.PropertyGroup.Version -ne $version) { throw "$project does not match VERSION." }
        if ($project -eq 'AnimeTracker.Desktop' -and [string]$xml.Project.PropertyGroup.InformationalVersion -ne $version) { throw 'Desktop informational version does not match VERSION.' }
    }
    [xml]$plist = [IO.File]::ReadAllText((Join-Path $Root 'src/AnimeTracker.Desktop/Assets/Info.plist'))
    $node = $plist.SelectSingleNode('//key[text()="CFBundleShortVersionString"]/following-sibling::string[1]')
    if (!$node -or $node.InnerText -ne $version) { throw 'macOS bundle does not match VERSION.' }
}

function Get-ReleaseContext {
    param([Parameter(Mandatory)][string]$BaseVersion,[string]$Branch='',[string]$Event='local',[string]$Commit='local',[long]$RunNumber=0,[string]$ExistingTagCommit='')
    if ($BaseVersion -notmatch '^\d+\.\d+(\.\d+)?$') { throw 'Invalid base version.' }
    if ($Commit -ne 'local' -and $Commit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid commit SHA.' }
    $package = $BaseVersion; $channel = 'local'; $publish = $false; $reason = 'Checks only; this event does not publish releases.'
    if ($Event -eq 'push' -and $Branch -eq 'dev') {
        if ($RunNumber -le 0) { throw 'Development releases require a positive workflow run number.' }
        $core = if ($BaseVersion.Split('.').Count -eq 2) { $BaseVersion + '.0' } else { $BaseVersion }
        $package = "$core-dev.$RunNumber"; $channel = 'dev'; $publish = $true; $reason = 'Development release.'
    } elseif ($Event -eq 'push' -and $Branch -eq 'main') {
        $channel = 'stable'; $publish = !$ExistingTagCommit -or $ExistingTagCommit -eq $Commit
        $reason = if ($publish) { 'Stable release or retry for the same commit.' } else { 'This VERSION is already tagged at another commit; publication skipped.' }
    }
    return [pscustomobject]@{ BaseVersion=$BaseVersion; PackageVersion=$package; Tag="v$package"; Channel=$channel; Publish=$publish; Prerelease=($channel -eq 'dev'); Commit=$Commit; Reason=$reason }
}

function Test-PackageVersion {
    param([string]$BaseVersion,[string]$PackageVersion,[ValidateSet('local','stable','dev')][string]$Channel='local')
    if ($Channel -eq 'dev') {
        $core = if ($BaseVersion.Split('.').Count -eq 2) { $BaseVersion + '.0' } else { $BaseVersion }
        if ($PackageVersion -notmatch ('^' + [regex]::Escape($core) + '-dev\.[1-9]\d*$')) { throw 'Development package version does not match VERSION.' }
    } elseif ($PackageVersion -ne $BaseVersion) { throw 'Package version must match VERSION for stable/local builds.' }
}

function Get-SourceFiles {
    param([Parameter(Mandatory)][string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $names = @('README.md','VERSION','NuGet.Config','Directory.Build.props','AnimeTracker.exe.config','.gitignore','.gitattributes','AGENTS.md','CONTRIBUTING.md','SECURITY.md','global.json')
    $extensions = @('.cs','.csproj','.props','.xaml','.axaml','.xml','.ico','.icns','.manifest','.plist','.ps1','.psm1','.sh','.txt','.md','.yml','.yaml','.json')
    $blocked = '(^|/)(bin|obj|data|covers|imports|artifacts|\.git|\.tools)(/|$)'
    foreach ($folder in @('src','scripts','docs','tests','.github')) {
        $path = Join-Path $rootPath $folder
        if (!(Test-Path -LiteralPath $path)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $path -File -Recurse -Force) {
            $relative = $file.FullName.Substring($rootPath.Length+1).Replace('\','/')
            $releaseMedia = $relative.StartsWith('docs/media/',[StringComparison]::Ordinal) -and $file.Extension -in @('.png','.mp4')
            if ($relative -notmatch $blocked -and ($file.Extension -in $extensions -or $releaseMedia) -and ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) { $names += $relative }
        }
    }
    return @($names | Where-Object { Test-Path -LiteralPath (Join-Path $rootPath $_) -PathType Leaf } | Sort-Object -Unique)
}

function New-BuildInfo {
    param([string]$BaseVersion,[string]$PackageVersion,[string]$Channel,[string]$Commit='local')
    Test-PackageVersion $BaseVersion $PackageVersion $Channel
    if ($Commit -ne 'local' -and $Commit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid build commit SHA.' }
    return [ordered]@{ applicationVersion=$BaseVersion; packageVersion=$PackageVersion; channel=$Channel; commit=$Commit }
}

function Get-ReleaseAssets {
    param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string]$PackageVersion)
    if ($PackageVersion -notmatch '^\d+\.\d+(\.\d+)?(-dev\.[1-9]\d*)?$') { throw 'Invalid package version.' }
    $expected = @("AnimeTracker-$PackageVersion-windows-x64.zip","AnimeTracker-$PackageVersion-macos-arm64.zip","AnimeTracker-$PackageVersion-source.zip")
    $actual = @(Get-ChildItem -LiteralPath $Directory -Filter '*.zip' -File | Select-Object -ExpandProperty Name)
    if (@(Compare-Object ($expected|Sort-Object) ($actual|Sort-Object)).Count) { throw 'Release must contain exactly the Windows, macOS and source ZIPs for one package version.' }
    return @($expected | ForEach-Object { $file=Get-Item -LiteralPath (Join-Path $Directory $_); [pscustomobject]@{Name=$file.Name;Path=$file.FullName;Size=$file.Length;Sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
}

function Test-ReleasePackages {
    param([Parameter(Mandatory)][string]$Directory,[string]$BaseVersion,[string]$PackageVersion,[string]$Channel,[string]$Commit)
    $null=New-BuildInfo $BaseVersion $PackageVersion $Channel $Commit
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $blocked='(^|/)(data|covers|imports|bin|obj|artifacts|\.git|\.tools)(/|$)|(^|/)(library|watch-choices|widget-settings|view-settings|personal-order|arcs)\.xml$|(^|/)\.env($|\.)|\.(pfx|p12|pem|key|pdb|tmp|bak)$'
    foreach($asset in Get-ReleaseAssets $Directory $PackageVersion) {
        $zip=[IO.Compression.ZipFile]::OpenRead($asset.Path)
        try {
            $prefix=if($asset.Name.EndsWith('-windows-x64.zip')){"AnimeTracker-$PackageVersion-windows-x64/"}elseif($asset.Name.EndsWith('-source.zip')){"AnimeTracker-$PackageVersion-source/"}else{''}
            foreach($entry in $zip.Entries) {
                $name=$entry.FullName.Replace('\','/')
                if($name -match $blocked -or $name -match '(^|/)\.\.(/|$)' -or $name.StartsWith('/') -or $name.Contains(':') -or (($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000){throw "Unsafe release entry: $name"}
                if($prefix -and !$name.StartsWith($prefix,[StringComparison]::Ordinal)){throw 'Unexpected archive root.'}
                $stream=$entry.Open();try{$stream.CopyTo([IO.Stream]::Null)}finally{$stream.Dispose()}
            }
            $required=if($asset.Name.EndsWith('-windows-x64.zip')){@('AnimeTracker.exe','AnimeTracker.exe.config','README.md')}elseif($asset.Name.EndsWith('-source.zip')){@('README.md','VERSION','AGENTS.md','.github/workflows/ci.yml','scripts/ReleaseTools.psm1')}else{@('Anime Tracker.app/Contents/Info.plist','Anime Tracker.app/Contents/MacOS/AnimeTracker','Anime Tracker.app/Contents/Resources/BUILD-INFO.json')}
            foreach($name in $required){if(!$zip.GetEntry($prefix+$name)){throw "Missing release entry: $name"}}
            $metadata=$zip.GetEntry($prefix+'BUILD-INFO.json');if(!$metadata){throw 'Missing build provenance.'}
            $reader=[IO.StreamReader]::new($metadata.Open());try{$info=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
            if($info.applicationVersion -ne $BaseVersion -or $info.packageVersion -ne $PackageVersion -or $info.channel -ne $Channel -or $info.commit -ne $Commit){throw 'Artifacts were built from different release contexts.'}
            if(!$prefix){
                $hostEntry=$zip.GetEntry('Anime Tracker.app/Contents/MacOS/AnimeTracker')
                if((($hostEntry.ExternalAttributes -shr 16) -band 0x1ff) -ne 493){throw 'Mac ZIP lost executable permissions.'}
                $stream=$hostEntry.Open();$header=New-Object byte[] 8;try{$read=0;while($read -lt 8){$chunk=$stream.Read($header,$read,8-$read);if(!$chunk){throw 'Invalid Mac executable header.'};$read+=$chunk}}finally{$stream.Dispose()}
                if([BitConverter]::ToUInt32($header,0) -ne [Convert]::ToUInt32('feedfacf',16) -or [BitConverter]::ToUInt32($header,4) -ne 0x100000c){throw 'Mac executable is not ARM64 Mach-O.'}
            }
        } finally {$zip.Dispose()}
    }
}

function Get-PublicationAction {
    param([string]$Commit,[string]$TagCommit='',$Release=$null)
    if($TagCommit -and $TagCommit -ne $Commit){return 'SkipOtherCommit'}
    if($null -eq $Release){return 'CreateDraft'}
    if($Release.target_commitish -ne $Commit){throw 'Existing release target does not match this build.'}
    if(!$Release.draft){return 'AlreadyPublished'}
    return 'ResumeDraft'
}

function Test-ExistingAsset {
    param($Asset,[long]$Size,[string]$Sha256)
    if($Asset.state -ne 'uploaded' -or $Asset.size -ne $Size -or $Asset.digest -ne ('sha256:'+$Sha256)){throw 'Asset digest or size mismatch; nothing was replaced.'}
}

Export-ModuleMember -Function Get-AppVersion,Get-AssemblyVersion,Test-RepositoryVersion,Get-ReleaseContext,Test-PackageVersion,Get-SourceFiles,New-BuildInfo,Get-ReleaseAssets,Test-ReleasePackages,Get-PublicationAction,Test-ExistingAsset
