[CmdletBinding()]
param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string]$Repository,[Parameter(Mandatory)][string]$PackageVersion,[ValidateSet('stable','dev')][string]$Channel,[Parameter(Mandatory)][string]$Commit)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or $Commit -notmatch '^[a-f0-9]{40}$'){throw 'Invalid repository or commit.'}
if(!$env:GH_TOKEN){throw 'GH_TOKEN is required only in the publishing job.'}
$root=Split-Path $PSScriptRoot -Parent
$baseVersion=Get-AppVersion $root
Test-ReleasePackages $Directory $baseVersion $PackageVersion $Channel $Commit
$assets=@(Get-ReleaseAssets $Directory $PackageVersion)
$checksums=Get-Item -LiteralPath (Join-Path $Directory 'SHA256SUMS.txt')
$expected=@($assets|ForEach-Object{$_.Sha256+'  '+$_.Name})
if(@(Compare-Object $expected @(Get-Content -LiteralPath $checksums.FullName)).Count){throw 'Release checksums do not match the archives.'}
$assets+=[pscustomobject]@{Name=$checksums.Name;Path=$checksums.FullName;Size=$checksums.Length;Sha256=(Get-FileHash -LiteralPath $checksums.FullName).Hash.ToLowerInvariant()}
$headers=@{Authorization=('Bearer '+$env:GH_TOKEN);'User-Agent'='AnimeTracker-Actions';Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
$api="https://api.github.com/repos/$Repository";$tag="v$PackageVersion"
function Invoke-GitHub([string]$Method,[string]$Url,$Body=$null){
    $parameters=@{Method=$Method;Uri=$Url;Headers=$headers;TimeoutSec=60}
    if($null -ne $Body){$parameters.ContentType='application/json; charset=utf-8';$parameters.Body=[Text.Encoding]::UTF8.GetBytes(($Body|ConvertTo-Json -Depth 6))}
    return Invoke-RestMethod @parameters
}
# Recheck the tag after builds finish; another push may have published this VERSION.
$tagCommit=''
try{$tagRef=Invoke-GitHub Get ($api+'/git/ref/tags/'+$tag);$object=$tagRef.object;while($object.type -eq 'tag'){$object=(Invoke-GitHub Get ($api+'/git/tags/'+$object.sha)).object};$tagCommit=$object.sha}catch{if(!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404){throw}}
$release=$null;$page=1
do{$items=Invoke-GitHub Get ($api+"/releases?per_page=100&page=$page");foreach($item in $items){if($item.tag_name -eq $tag){$release=$item;break}};$page++}while(!$release -and $items.Count -eq 100)
$action=Get-PublicationAction $Commit $tagCommit $release
if($action -eq 'SkipOtherCommit'){Write-Output "$tag already belongs to another commit; publication skipped without changes.";exit 0}
if($action -eq 'AlreadyPublished'){Write-Output "$tag is already published for this commit; no assets or metadata were changed.";exit 0}
if($action -eq 'CreateDraft'){
    $notes=[IO.File]::ReadAllText((Join-Path $Directory 'RELEASE-NOTES.md'))
    $release=Invoke-GitHub Post ($api+'/releases') @{tag_name=$tag;target_commitish=$Commit;name="Anime Tracker $PackageVersion";body=$notes;draft=$true;prerelease=($Channel -eq 'dev')}
}
$upload=($release.upload_url -split '\{',2)[0]
foreach($asset in $assets){
    $existing=$release.assets|Where-Object{$_.name -eq $asset.Name}|Select-Object -First 1
    if($existing){
        Test-ExistingAsset $existing $asset.Size $asset.Sha256
    }else{
        $type=if($asset.Name.EndsWith('.zip')){'application/zip'}elseif($asset.Name.EndsWith('.tar.gz')){'application/gzip'}else{'text/plain'}
        $existing=Invoke-RestMethod -Method Post -Uri ($upload+'?name='+[Uri]::EscapeDataString($asset.Name)) -Headers $headers -ContentType $type -InFile $asset.Path -TimeoutSec 180
        Test-ExistingAsset $existing $asset.Size $asset.Sha256
    }
    Write-Output ('Verified '+$asset.Name)
}
$verified=Invoke-GitHub Get ($api+'/releases/'+$release.id)
if($verified.assets.Count -ne $assets.Count){throw 'Draft contains unexpected assets; release was not published.'}
$latest=if($Channel -eq 'stable'){'true'}else{'false'}
$published=Invoke-GitHub Patch ($api+'/releases/'+$release.id) @{draft=$false;prerelease=($Channel -eq 'dev');make_latest=$latest}
if($published.draft -or $published.prerelease -ne ($Channel -eq 'dev')){throw 'Release publication did not complete.'}
Write-Output $published.html_url
