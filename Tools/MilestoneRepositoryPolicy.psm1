Set-StrictMode -Version Latest

function Get-MilestoneRepositoryPathFindings {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    $findings = [System.Collections.Generic.List[string]]::new()
    $forbiddenDirectory = '(?i)(^|/)(02_CODEX_LOGS|04_AUDIT_RESULTS|05_EVIDENCE|prompts?|audit[-_ ]criteria)(/|$)'
    $forbiddenDocument = '(?i)(^|/)(ENG-M00-(\d{3}_)?(CODEX_LOG|AUDIT_RESULT|AUDIT_CRITERIA|IMPLEMENTATION_PROMPT)|ENG-M00_MASTER_CODEX_LOG|ENG-M00_MASTER_AUDIT_RESULT|AUDIT_HANDOFF_INDEX)(_[A-Z0-9]+)*\.(md|txt|json|xml|yaml|yml|docx|gdoc)$'

    foreach ($path in $Paths) {
        $normalized = $path.Replace([string][char]92, '/')
        if ($normalized -match $forbiddenDirectory -or $normalized -match $forbiddenDocument) {
            $findings.Add("Drive coordination artifact must not be committed: $normalized")
        }
    }

    return $findings.ToArray()
}

function Get-MilestoneRepositoryPolicyFindings {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][psobject] $Snapshot)

    $findings = [System.Collections.Generic.List[string]]::new()
    $allPaths = @($Snapshot.TrackedPaths) + @($Snapshot.ChangedPaths) + @($Snapshot.CommitPaths)
    foreach ($finding in (Get-MilestoneRepositoryPathFindings -Paths $allPaths)) {
        $findings.Add($finding)
    }

    foreach ($changedPath in @($Snapshot.ChangedPaths) + @($Snapshot.CommitPaths)) {
        $normalized = $changedPath.Replace([string][char]92, '/')
        if ($normalized -ieq 'TASKS.md') {
            $findings.Add('Protected root TASKS.md was modified or included in the latest commit.')
        }
    }

    if ($Snapshot.Branch -cne 'main') {
        $findings.Add("Expected branch main; found '$($Snapshot.Branch)'.")
    }
    if ([int]$Snapshot.Ahead -ne 0 -or [int]$Snapshot.Behind -ne 0) {
        $findings.Add("main is not synchronized with origin/main (ahead=$($Snapshot.Ahead), behind=$($Snapshot.Behind)).")
    }
    if (-not [bool]$Snapshot.ReusePolicyValid) {
        $findings.Add('Pinned reuse and license policy validation failed or its required records are missing.')
    }

    return $findings.ToArray()
}

Export-ModuleMember -Function Get-MilestoneRepositoryPathFindings, Get-MilestoneRepositoryPolicyFindings
