[CmdletBinding()]
param([string] $Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$modulePath = Join-Path $PSScriptRoot 'MilestoneRepositoryPolicy.psm1'
Import-Module $modulePath -Force

function Invoke-GitReadOnly([string[]] $Arguments) {
    $output = @(& git -C $rootPath @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    return $output
}

$trackedPaths = @(Invoke-GitReadOnly @('ls-files'))
$statusLines = @(Invoke-GitReadOnly @('status','--porcelain=v1','--untracked-files=all'))
$changedPaths = @($statusLines | ForEach-Object { if ($_.Length -gt 3) { $_.Substring(3).Trim() } })
$commitPaths = @(Invoke-GitReadOnly @('diff-tree','--no-commit-id','--name-only','-r','HEAD'))
$branch = [string](Invoke-GitReadOnly @('branch','--show-current') | Select-Object -First 1)
$divergence = [string](Invoke-GitReadOnly @('rev-list','--left-right','--count','HEAD...origin/main') | Select-Object -First 1)
$counts = @($divergence -split '\s+' | Where-Object { $_ -ne '' })
if ($counts.Count -ne 2) { throw "Could not parse local/origin main divergence: '$divergence'." }

$provenancePath = Join-Path $rootPath 'Documentation~/THIRD_PARTY_PROVENANCE.md'
$noticesPath = Join-Path $rootPath 'Documentation~/THIRD_PARTY_NOTICES.md'
$provenanceValid = $false
$reusePolicyValid = $false
if ((Test-Path -LiteralPath $provenancePath -PathType Leaf) -and (Test-Path -LiteralPath $noticesPath -PathType Leaf)) {
    $provenanceText = [System.IO.File]::ReadAllText($provenancePath)
    $noticesText = [System.IO.File]::ReadAllText($noticesPath)
    $provenanceValid = $provenanceText.Contains('COPY_ALLOWED_WITH_CONDITIONS') -and $provenanceText.Contains('REFERENCE_ONLY') -and $provenanceText.Contains('BLOCKED')
    $noticesValid = $noticesText -match '(?i)LibraStack' -and $noticesText -match '(?i)MIT' -and $noticesText -match '(?i)UniTask'
    Import-Module (Join-Path $PSScriptRoot 'ThirdPartyProvenance.psm1') -Force
    $reuseFindings = @(Get-ThirdPartyProvenanceFindings -Root $rootPath)
    $reusePolicyValid = $provenanceValid -and $noticesValid -and $reuseFindings.Count -eq 0
}

$snapshot = [pscustomobject]@{
    TrackedPaths = $trackedPaths
    ChangedPaths = $changedPaths
    CommitPaths = $commitPaths
    Branch = $branch
    Ahead = [int]$counts[0]
    Behind = [int]$counts[1]
    ReusePolicyValid = $reusePolicyValid
}
$findings = @(Get-MilestoneRepositoryPolicyFindings -Snapshot $snapshot)
if ($findings.Count -gt 0) {
    $findings | ForEach-Object { [Console]::Error.WriteLine("FAIL: $_") }
    Write-Output "REPOSITORY_POLICY=FAIL TRACKED=$($trackedPaths.Count) CHANGED=$($changedPaths.Count) COMMIT_PATHS=$($commitPaths.Count)"
    exit 1
}

$tasksBlob = [string](Invoke-GitReadOnly @('rev-parse','HEAD:TASKS.md') | Select-Object -First 1)
Write-Output "REPOSITORY_POLICY=PASS TRACKED=$($trackedPaths.Count) CHANGED=$($changedPaths.Count) COMMIT_PATHS=$($commitPaths.Count) BRANCH=$branch MAIN_DIVERGENCE=$divergence"
Write-Output "TASKS_MD_BLOB=$tasksBlob"
Write-Output 'REUSE_POLICY=PASS retained provenance matrix, notices, pinned dependencies, and no restricted source trees.'
exit 0
