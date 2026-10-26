[CmdletBinding()]
param([string]$Branch=$env:GITHUB_REF_NAME,[string]$Event=$env:GITHUB_EVENT_NAME,[string]$Commit=$env:GITHUB_SHA,[long]$RunNumber=$env:GITHUB_RUN_NUMBER)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
Test-RepositoryVersion $root
$version=Get-AppVersion $root
if (!$Event) { $Event='local' }; if (!$Commit) { $Commit='local' }
$existing=''
if ($Event -eq 'push' -and $Branch -eq 'main') {
    $tag="refs/tags/v$version"
    $ref=git -C $root for-each-ref --format='%(refname)' $tag
    if ($LASTEXITCODE -ne 0) { throw 'Could not read Git tags.' }
    if ($ref -eq $tag) { $existing=(git -C $root rev-list -n 1 $tag).Trim(); if ($LASTEXITCODE -ne 0) { throw 'Could not resolve release tag.' } }
}
$context=Get-ReleaseContext -BaseVersion $version -Branch $Branch -Event $Event -Commit $Commit -RunNumber $RunNumber -ExistingTagCommit $existing
if ($env:GITHUB_OUTPUT) {
    @("base-version=$($context.BaseVersion)","package-version=$($context.PackageVersion)","tag=$($context.Tag)","channel=$($context.Channel)","publish=$($context.Publish.ToString().ToLowerInvariant())") | Out-File -LiteralPath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
if ($env:GITHUB_STEP_SUMMARY) { "Release plan: $($context.Tag) / $($context.Channel). $($context.Reason)" | Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8 }
$context | ConvertTo-Json
