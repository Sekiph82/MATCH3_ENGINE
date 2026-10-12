[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot '../../Tools/MilestoneRepositoryPolicy.psm1'
Import-Module (Resolve-Path -LiteralPath $modulePath) -Force

$passed = 0
$failed = 0
function Assert-Policy([string] $Name, [bool] $Condition) {
    if (-not $Condition) { throw "FAIL: $Name" }
    Write-Output "PASS: $Name"
    $script:passed++
}

$valid = [pscustomobject]@{
    TrackedPaths = @('TASKS.md','Packages/com.match3.engine/Runtime/AssemblyMarker.cs','Documentation~/THIRD_PARTY_PROVENANCE.md')
    ChangedPaths = @('Tools/ValidateMilestoneRepositoryPolicy.ps1','Tests/Policy/MilestoneRepositoryPolicy.Tests.ps1')
    CommitPaths = @('Tools/ValidateMilestoneRepositoryPolicy.ps1','Documentation~/MILESTONE_REPOSITORY_POLICY.md')
    Branch = 'main'
    Ahead = 0
    Behind = 0
    ReusePolicyValid = $true
}
Assert-Policy 'source, tests, product documentation and the protected tracked TASKS.md are permitted' ((Get-MilestoneRepositoryPolicyFindings -Snapshot $valid).Count -eq 0)

$forbidden = Get-MilestoneRepositoryPathFindings -Paths @('02_CODEX_LOGS/ENG-M00-007_CODEX_LOG_V02.md','04_AUDIT_RESULTS/ENG-M00-007_AUDIT_RESULT_V02.md','05_EVIDENCE/result.xml','Prompts/ENG-M00-007.md','ENG-M00-007_Evidence_Notes.txt')
Assert-Policy 'Drive coordination paths and task artifacts are rejected' ($forbidden.Count -eq 5)

$mutatedTracker = $valid.PSObject.Copy()
$mutatedTracker.ChangedPaths = @('TASKS.md')
Assert-Policy 'a changed root TASKS.md is rejected' ((Get-MilestoneRepositoryPolicyFindings -Snapshot $mutatedTracker | Where-Object { $_ -like '*TASKS.md*' }).Count -gt 0)

$unsynced = $valid.PSObject.Copy()
$unsynced.Branch = 'feature/governance'
$unsynced.Ahead = 1
Assert-Policy 'non-main or divergent publication state is rejected' ((Get-MilestoneRepositoryPolicyFindings -Snapshot $unsynced).Count -ge 2)

$missingReuse = $valid.PSObject.Copy()
$missingReuse.ReusePolicyValid = $false
Assert-Policy 'missing or failed reuse-rights validation is rejected' ((Get-MilestoneRepositoryPolicyFindings -Snapshot $missingReuse | Where-Object { $_ -like '*reuse and license*' }).Count -eq 1)

Write-Output "GOVERNANCE_POLICY_TESTS=$passed PASSED=5 FAILED=$failed"
