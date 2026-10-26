[CmdletBinding()]
param([string]$Dotnet='dotnet',[string]$OutputPath='artifacts/security/nuget-audit.json')
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'DependencyAudit.psm1') -Force
$root=Split-Path $PSScriptRoot -Parent
$output=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$null=New-Item -ItemType Directory -Path (Split-Path $output -Parent) -Force
$projects=@(foreach($folder in @('src','tests')){Get-ChildItem -LiteralPath (Join-Path $root $folder) -Filter '*.csproj' -File -Recurse|Where-Object{$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}})
if(!$projects.Count){throw 'No SDK projects found to audit.'}
$result=[ordered]@{status='running';threshold='high';projects=@();findings=@();failure=$null}
try{
    foreach($project in $projects){
        Write-Output ('Auditing '+$project.FullName.Substring($root.Length+1))
        & $Dotnet restore $project.FullName --configfile (Join-Path $root 'NuGet.Config') --no-http-cache
        if($LASTEXITCODE -ne 0){throw ('NuGet restore/audit failed: '+$project.Name)}
        $json=& $Dotnet package list --project $project.FullName --vulnerable --include-transitive --format json --output-version 1 --no-restore
        if($LASTEXITCODE -ne 0){throw ('NuGet vulnerability query failed: '+$project.Name)}
        $report=($json -join "`n")|ConvertFrom-Json
        $audit=Get-NuGetAuditResult $report
        if(!@($report.projects|Where-Object{[IO.Path]::GetFullPath($_.path) -eq $project.FullName}).Count){throw 'Audit report does not cover the requested project.'}
        $result.projects+=@{project=$project.FullName.Substring($root.Length+1).Replace('\','/');report=$report}
        $result.findings+=@($audit.Findings)
    }
    $blocking=@($result.findings|Where-Object{$_.severity -in @('high','critical')})
    if($blocking.Count){throw ('Found '+$blocking.Count+' High/Critical vulnerability records.')}
    $result.status='passed'
    Write-Output ('PASS: '+$projects.Count+' SDK projects audited, including transitive packages; no High/Critical vulnerabilities.')
    if($result.findings.Count){Write-Warning ($result.findings.Count.ToString()+' Low/Moderate records require review; see audit report.')}
}catch{
    $result.status='failed';$result.failure=$_.Exception.Message
    throw
}finally{
    [IO.File]::WriteAllText($output,($result|ConvertTo-Json -Depth 25),[Text.UTF8Encoding]::new($false))
    if($env:GITHUB_STEP_SUMMARY){"NuGet audit: $($result.status); projects: $($result.projects.Count); records: $($result.findings.Count). High/Critical block publication."|Out-File -LiteralPath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8}
}
