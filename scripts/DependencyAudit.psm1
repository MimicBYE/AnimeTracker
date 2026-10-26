Set-StrictMode -Version Latest

function Get-AuditProperty($Object,[string]$Name){
    if($null -ne $Object -and $Object.PSObject.Properties[$Name]){
        $value=$Object.PSObject.Properties[$Name].Value
        if($null -ne $value){return $value}
    }
}

function Get-NuGetAuditResult {
    param([Parameter(Mandatory)]$Report)
    if((Get-AuditProperty $Report 'version') -ne 1){throw 'Unsupported NuGet audit report version.'}
    $parameters=[string](Get-AuditProperty $Report 'parameters')
    if($parameters -notmatch '--vulnerable' -or $parameters -notmatch '--include-transitive'){throw 'Audit did not include vulnerable and transitive packages.'}
    if(!@(Get-AuditProperty $Report 'sources').Count){throw 'Audit report has no vulnerability sources.'}
    $projects=@(Get-AuditProperty $Report 'projects')
    if(!$projects.Count){throw 'Audit report has no projects.'}
    $findings=@()
    foreach($object in @($Report)+$projects){
        if(@(Get-AuditProperty $object 'errors').Count){throw 'NuGet reported audit errors.'}
        foreach($log in @(Get-AuditProperty $object 'logs')){
            if((Get-AuditProperty $log 'level') -eq 'Error' -or (Get-AuditProperty $log 'code') -in @('NU1900','NU1905')){throw 'NuGet could not complete vulnerability evaluation.'}
        }
    }
    foreach($project in $projects){
        $path=[string](Get-AuditProperty $project 'path');if(!$path){throw 'Audit project path is missing.'}
        foreach($framework in @(Get-AuditProperty $project 'frameworks')){
            foreach($scope in @('topLevelPackages','transitivePackages')){
                foreach($package in @(Get-AuditProperty $framework $scope)){
                    foreach($vulnerability in @(Get-AuditProperty $package 'vulnerabilities')){
                        $severity=([string](Get-AuditProperty $vulnerability 'severity')).ToLowerInvariant()
                        if($severity -match '^[0-3]$'){$severity=@('low','moderate','high','critical')[[int]$severity]}
                        if($severity -notin @('low','moderate','high','critical')){throw 'Unknown vulnerability severity.'}
                        $id=[string](Get-AuditProperty $package 'id');$version=[string](Get-AuditProperty $package 'resolvedVersion');$advisory=[string](Get-AuditProperty $vulnerability 'advisoryurl')
                        $uri=$null
                        if(!$id -or !$version -or ![Uri]::TryCreate($advisory,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -ne 'https'){throw 'Incomplete vulnerability record.'}
                        $findings+=[pscustomobject]@{project=$path;package=$id;version=$version;scope=$scope;severity=$severity;advisory=$advisory}
                    }
                }
            }
        }
    }
    return [pscustomobject]@{Findings=@($findings);Blocked=@($findings|Where-Object{$_.severity -in @('high','critical')});ProjectCount=$projects.Count}
}

Export-ModuleMember -Function Get-NuGetAuditResult
