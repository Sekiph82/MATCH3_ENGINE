[CmdletBinding()]
param(
    [string] $ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string] $EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe',
    [string] $ReleaseArtifactPath
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$runner = Join-Path $PSScriptRoot 'RunUnityEditModeTests.ps1'
$scannerTests = Join-Path (Split-Path -Parent $PSScriptRoot) 'Tests\Policy\ReleaseArtifactScanner.Tests.ps1'
$scanner = Join-Path $PSScriptRoot 'ScanUnityReleaseArtifact.ps1'
$powerShell = (Get-Command pwsh -ErrorAction Stop).Source

function Invoke-PowerShellChild([string] $scriptPath, [string[]] $arguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $powerShell
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $false
    foreach ($argument in (@('-NoProfile', '-File', $scriptPath) + $arguments)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    return $process.ExitCode
}

foreach ($platform in @('EditMode', 'PlayMode')) {
    Write-Output "RUN_UNITY_TESTS=$platform"
    $code = Invoke-PowerShellChild $runner @('-ProjectPath', $project, '-EditorPath', $EditorPath, '-TestPlatform', $platform)
    if ($code -ne 0) {
        Write-Output "UNITY_CI_FAILURE: Unity $platform checks failed with child exit code $code."
        exit $code
    }
}

Write-Output 'RUN_RELEASE_SCANNER_TESTS'
$code = Invoke-PowerShellChild $scannerTests @()
if ($code -ne 0) {
    Write-Output "UNITY_CI_FAILURE: Release artifact scanner tests failed with child exit code $code."
    exit $code
}

if (-not [string]::IsNullOrWhiteSpace($ReleaseArtifactPath)) {
    Write-Output "SCAN_RELEASE_ARTIFACT=$ReleaseArtifactPath"
    $code = Invoke-PowerShellChild $scanner @('-ArtifactPath', $ReleaseArtifactPath)
    if ($code -ne 0) {
        Write-Output "UNITY_CI_FAILURE: Release artifact scan failed with child exit code $code."
        exit $code
    }
}

Write-Output 'UNITY_CI=PASS'
exit 0
