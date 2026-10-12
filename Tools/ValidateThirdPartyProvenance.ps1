[CmdletBinding()]
param(
    [string] $Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ThirdPartyProvenance.psm1') -Force
$findings = @(Get-ThirdPartyProvenanceFindings -Root $Root)
if ($findings.Count -gt 0) {
    $findings | ForEach-Object { [Console]::Error.WriteLine("FAIL: $_") }
    exit 1
}

Write-Output 'PASS: no restricted reference source trees or missing retained MIT notices.'
exit 0
