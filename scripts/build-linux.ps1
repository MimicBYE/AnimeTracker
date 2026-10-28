[CmdletBinding()]
param([string]$Dotnet='dotnet',[string]$PackageVersion='',[ValidateSet('local','stable','dev')][string]$Channel='local',[string]$Commit='local',[switch]$SmokeTest)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'LinuxPackage.psm1') -Force
Test-RepositoryVersion $root
$version=Get-AppVersion $root;if(!$PackageVersion){$PackageVersion=$version}
$info=New-BuildInfo $version $PackageVersion $Channel $Commit
$run=Join-Path $root ('artifacts/linux/'+[Guid]::NewGuid().ToString('N'))
$name="AnimeTracker-$PackageVersion-linux-x64";$publish=Join-Path $run $name;$dist=Join-Path $root 'dist/linux'
$null=New-Item -ItemType Directory -Path $publish,$dist -Force
& $Dotnet publish (Join-Path $root 'src/AnimeTracker.Desktop/AnimeTracker.Desktop.csproj') -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false -p:PublishReadyToRun=false -o $publish --configfile (Join-Path $root 'NuGet.Config')
if($LASTEXITCODE -ne 0){throw 'Linux publish failed.'}
Get-ChildItem -LiteralPath $publish -Filter '*.pdb' | Remove-Item
foreach($file in @('LINUX.md','THIRD-PARTY-NOTICES.md','ArcCatalog-SOURCES.txt')){Copy-Item -LiteralPath (Join-Path $root ('docs/'+$file)) -Destination (Join-Path $publish $file)}
Copy-Item -LiteralPath (Join-Path $root 'docs/licenses') -Destination (Join-Path $publish 'licenses') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'src/AnimeTracker.Desktop/Assets/app.png') -Destination (Join-Path $publish 'app.png')
[IO.File]::WriteAllText((Join-Path $publish 'BUILD-INFO.json'),($info|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
if(!$IsLinux){throw 'Package permissions and native checks require Linux; run this script on Linux.'}
& chmod 755 (Join-Path $publish 'AnimeTracker');if($LASTEXITCODE -ne 0){throw 'chmod failed.'}
$archive=Join-Path $dist ($name+'.tar.gz')
& tar --create --gzip --format=ustar --owner=0 --group=0 --file $archive --directory $run $name
if($LASTEXITCODE -ne 0){throw 'Linux archive failed.'}
Test-LinuxPackage $archive $version $PackageVersion $Channel $Commit
if($SmokeTest) {
    $extract=Join-Path $run 'extracted package';$null=New-Item -ItemType Directory -Path $extract
    & tar --extract --gzip --file $archive --directory $extract;if($LASTEXITCODE -ne 0){throw 'Linux extraction failed.'}
    $exe=Join-Path $extract ($name+'/AnimeTracker')
    $testRoot=Join-Path $run 'anime-tracker-linux-smoke-data';$testHome=Join-Path $run 'smoke-home'
    $null=New-Item -ItemType Directory -Path $testRoot,$testHome
    foreach($attempt in 1..2) {
        $start=[Diagnostics.ProcessStartInfo]::new($exe);$start.UseShellExecute=$false
        foreach($argument in @('--startup','--no-tray','--data-dir',$testRoot,'--linux-smoke-test')){$start.ArgumentList.Add($argument)}
        $start.Environment['HOME']=$testHome
        foreach($pair in @(@('XDG_DATA_HOME','data'),@('XDG_CONFIG_HOME','config'),@('XDG_CACHE_HOME','cache'),@('XDG_STATE_HOME','state'))){$start.Environment[$pair[0]]=Join-Path $testHome $pair[1]}
        $process=[Diagnostics.Process]::Start($start)
        if(!$process.WaitForExit(60000)){$process.Kill($true);throw 'Linux smoke timeout (close must exit).'}
        $report=Get-Content -LiteralPath (Join-Path $testRoot 'linux-smoke.txt') -Raw
        if($process.ExitCode -ne 0 -or !$report.StartsWith('PASS:')){throw "Linux smoke failed: $report"}
        Write-Output "PASS native package smoke/restart $attempt"
    }
}
((Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive))|Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Linux package: $archive"
