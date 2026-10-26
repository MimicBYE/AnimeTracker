$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts/DependencyAudit.psm1') -Force
function Assert([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Reject([scriptblock]$Action,[string]$Message){$rejected=$false;try{& $Action}catch{$rejected=$true};Assert $rejected $Message}
function Report([string]$Severity='High',[string]$Scope='topLevelPackages'){
    $package=[pscustomobject]@{id='Example.Package';resolvedVersion='1.0.0';vulnerabilities=@([pscustomobject]@{severity=$Severity;advisoryurl='https://github.com/advisories/example'})}
    $framework=[pscustomobject]@{framework='net10.0';topLevelPackages=@();transitivePackages=@()};$framework.$Scope=@($package)
    return [pscustomobject]@{version=1;parameters='--vulnerable --include-transitive';sources=@('https://api.nuget.org/v3/index.json');projects=@([pscustomobject]@{path='example.csproj';frameworks=@($framework)})}
}
$clean=[pscustomobject]@{version=1;parameters='--vulnerable --include-transitive';sources=@('https://api.nuget.org/v3/index.json');projects=@([pscustomobject]@{path='example.csproj'})}
$result=Get-NuGetAuditResult $clean;Assert ($result.Findings.Count -eq 0 -and $result.Blocked.Count -eq 0 -and $result.ProjectCount -eq 1) 'Clean CLI report format rejected'
$result=Get-NuGetAuditResult (Report 'High');Assert ($result.Blocked.Count -eq 1 -and $result.Blocked[0].scope -eq 'topLevelPackages') 'Direct High vulnerability did not block'
$result=Get-NuGetAuditResult (Report 'Critical' 'transitivePackages');Assert ($result.Blocked.Count -eq 1 -and $result.Blocked[0].severity -eq 'critical') 'Transitive Critical vulnerability did not block'
$result=Get-NuGetAuditResult (Report '2' 'transitivePackages');Assert ($result.Blocked.Count -eq 1) 'Numeric NuGet severity did not block'
$result=Get-NuGetAuditResult (Report 'Moderate');Assert ($result.Blocked.Count -eq 0 -and $result.Findings.Count -eq 1) 'Moderate advisory should remain visible'
Reject {Get-NuGetAuditResult (Report 'unknown')} 'Unknown severity accepted as safe'
$invalid=Report;$invalid.version=2;Reject {Get-NuGetAuditResult $invalid} 'Unknown report schema accepted'
$invalid=Report;$invalid.sources=@();Reject {Get-NuGetAuditResult $invalid} 'Missing vulnerability source accepted'
$invalid=Report;$invalid.projects=@();Reject {Get-NuGetAuditResult $invalid} 'Missing project coverage accepted'
$invalid=Report;$invalid.parameters='--vulnerable';Reject {Get-NuGetAuditResult $invalid} 'Missing transitive audit accepted'
$invalid=Report;$invalid|Add-Member -NotePropertyName errors -NotePropertyValue @('source unavailable');Reject {Get-NuGetAuditResult $invalid} 'Audit errors accepted as a clean result'
$invalid=Report;$invalid|Add-Member -NotePropertyName logs -NotePropertyValue @([pscustomobject]@{level='Warning';code='NU1900'});Reject {Get-NuGetAuditResult $invalid} 'Unavailable database accepted'
$invalid=Report;$invalid.projects[0].frameworks[0].topLevelPackages[0].vulnerabilities[0].advisoryurl='';Reject {Get-NuGetAuditResult $invalid} 'Incomplete advisory accepted'
Write-Output 'PASS: clean reports, direct/transitive findings, numeric severities, High/Critical gating, Moderate visibility, unknown schema/severity, audit failures and missing coverage.'
