[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$run=Join-Path $root ('artifacts/macos/'+[Guid]::NewGuid().ToString('N'))
$publish=Join-Path $run 'publish'
$bundle=Join-Path $run 'Anime Tracker.app'
$macos=Join-Path $bundle 'Contents/MacOS'
$resources=Join-Path $bundle 'Contents/Resources'
$dist=Join-Path $root 'dist/macos'
foreach($directory in @($macos,$resources,$dist)){New-Item -ItemType Directory -Path $directory -Force|Out-Null}
& $Dotnet publish (Join-Path $root 'src/AnimeTracker.Desktop/AnimeTracker.Desktop.csproj') -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o $publish --configfile (Join-Path $root 'NuGet.Config')
if($LASTEXITCODE -ne 0){throw 'macOS publish failed.'}
Get-ChildItem -LiteralPath $publish | Where-Object {$_.Extension -ne '.pdb'} | Copy-Item -Destination $macos -Recurse
Copy-Item -LiteralPath (Join-Path $root 'src/AnimeTracker.Desktop/Assets/app.icns') -Destination (Join-Path $resources 'app.icns')
Copy-Item -LiteralPath (Join-Path $root 'docs/licenses') -Destination (Join-Path $resources 'licenses') -Recurse
foreach($notice in @('MACOS.md','THIRD-PARTY-NOTICES.md','ArcCatalog-SOURCES.txt')){Copy-Item -LiteralPath (Join-Path $root ('docs/'+$notice)) -Destination (Join-Path $resources $notice)}
Copy-Item -LiteralPath (Join-Path $root 'src/AnimeTracker.Desktop/Assets/Info.plist') -Destination (Join-Path $bundle 'Contents/Info.plist')
$hostBytes=[IO.File]::ReadAllBytes((Join-Path $macos 'AnimeTracker'))
if([BitConverter]::ToUInt32($hostBytes,0) -ne [Convert]::ToUInt32('feedfacf',16) -or [BitConverter]::ToUInt32($hostBytes,4) -ne 0x100000c){throw 'Expected an ARM64 Mach-O app host.'}
# On macOS, apply a local ad-hoc signature; a public release requires Developer ID signing/notarization.
if($IsMacOS){& chmod +x (Join-Path $macos 'AnimeTracker');& codesign --force --deep --sign - $bundle;if($LASTEXITCODE -ne 0){throw 'Local code signing failed.'}}
Copy-Item -LiteralPath (Join-Path $root 'docs/MACOS.md') -Destination (Join-Path $run 'MACOS.md')
Copy-Item -LiteralPath (Join-Path $root 'docs/THIRD-PARTY-NOTICES.md') -Destination (Join-Path $run 'THIRD-PARTY-NOTICES.md')
Copy-Item -LiteralPath (Join-Path $root 'docs/ArcCatalog-SOURCES.txt') -Destination (Join-Path $run 'ArcCatalog-SOURCES.txt')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=Join-Path $dist ('AnimeTracker-'+$version+'-macos-arm64.zip')
$stream=[IO.File]::Create($archive);$zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
try{
    $files=@(Get-ChildItem -LiteralPath $bundle -File -Recurse)+@(Get-Item -LiteralPath (Join-Path $run 'MACOS.md'),(Join-Path $run 'THIRD-PARTY-NOTICES.md'),(Join-Path $run 'ArcCatalog-SOURCES.txt'))
    foreach($file in $files){
        $name=$file.FullName.Substring($run.Length+1).Replace('\','/')
        $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
        $mode=if($file.Name -eq 'AnimeTracker' -or $file.Extension -eq '.dylib'){0x81ed}else{0x81a4}
        $entry.ExternalAttributes=($mode -shl 16)
        $input=[IO.File]::OpenRead($file.FullName);$output=$entry.Open();try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
    }
}finally{$zip.Dispose();$stream.Dispose()}
# ZipArchive on Windows marks the creator as DOS. Mark each central-directory entry
# as Unix as well, so macOS extractors honor the executable modes above.
$archiveBytes=[IO.File]::ReadAllBytes($archive)
$end=$archiveBytes.Length-22
if([BitConverter]::ToUInt32($archiveBytes,$end) -ne 0x06054b50){throw 'Unexpected ZIP end record.'}
$position=[int][BitConverter]::ToUInt32($archiveBytes,$end+16)
$entryCount=[BitConverter]::ToUInt16($archiveBytes,$end+10)
for($index=0;$index -lt $entryCount;$index++){
    if([BitConverter]::ToUInt32($archiveBytes,$position) -ne 0x02014b50){throw 'Unexpected ZIP central-directory entry.'}
    $archiveBytes[$position+5]=3
    $position+=46+[BitConverter]::ToUInt16($archiveBytes,$position+28)+[BitConverter]::ToUInt16($archiveBytes,$position+30)+[BitConverter]::ToUInt16($archiveBytes,$position+32)
}
[IO.File]::WriteAllBytes($archive,$archiveBytes)
$verify=[IO.Compression.ZipFile]::OpenRead($archive)
try{
    $hostEntry=$verify.GetEntry('Anime Tracker.app/Contents/MacOS/AnimeTracker')
    if(!$hostEntry -or (($hostEntry.ExternalAttributes -shr 16) -band 0x1ff) -ne 493){throw 'ZIP lost executable permissions.'}
    if(@($verify.Entries|Where-Object {$_.FullName -match '(^|/)(data|covers|bin|obj|\.tools)(/|$)'}).Count){throw 'Unexpected private/build files in Mac archive.'}
}finally{$verify.Dispose()}
((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive))|Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Mac app: $archive"
Write-Output "Bundle: $bundle"
Write-Output 'PASS: self-contained osx-arm64, Info.plist, icon, ZIP executable permissions. Native macOS launch still requires a Mac.'
