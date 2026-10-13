[CmdletBinding()]
param(
    [string] $ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string] $EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe',
    [ValidateSet('EditMode','PlayMode')][string] $TestPlatform = 'EditMode',
    [string] $EvidenceDirectory,
    [string] $TestFilter,
    [switch] $FailureProbe
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw "Unity editor not found: $EditorPath" }
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
if ($FailureProbe) {
    if ($TestPlatform -ne 'EditMode') { throw 'The runner-integrity failure probe is EditMode-only.' }
    $expectedFilter = '^Match3\.Engine\.Tests\.Editor\.UnityRunnerIntegrityProbeTests\.IntentionalAssertionFailureProbe$'
    if (-not [string]::IsNullOrWhiteSpace($TestFilter) -and $TestFilter -ne $expectedFilter) {
        throw 'FailureProbe cannot be combined with a different TestFilter.'
    }
    $TestFilter = $expectedFilter
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $work = Join-Path $tempBase ('eng-m00-' + $TestPlatform.ToLowerInvariant() + '-' + [guid]::NewGuid().ToString('N'))
}
else {
    $work = [System.IO.Path]::GetFullPath($EvidenceDirectory)
}
New-Item -ItemType Directory -Path $work -Force | Out-Null
$resultsPath = Join-Path $work ($TestPlatform.ToLowerInvariant() + '-results.xml')
$logPath = Join-Path $work ('unity-' + $TestPlatform.ToLowerInvariant() + '.log')

$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $EditorPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in @('-batchmode','-nographics','-runTests','-testPlatform',$TestPlatform,'-testResults',$resultsPath,'-projectPath',$project,'-logFile',$logPath)) {
    [void]$start.ArgumentList.Add($argument)
}
if (-not [string]::IsNullOrWhiteSpace($TestFilter)) {
    [void]$start.ArgumentList.Add('-testFilter')
    [void]$start.ArgumentList.Add($TestFilter)
}
if ($FailureProbe) {
    $start.Environment['M00_FORCE_TEST_FAILURE'] = '1'
}
$process = [System.Diagnostics.Process]::Start($start)
$process.WaitForExit()
$exitCode = $process.ExitCode
if (-not (Test-Path -LiteralPath $resultsPath -PathType Leaf)) {
    Write-Output "UNITY_FAILURE: Unity exited $exitCode without producing the $TestPlatform results XML. See $logPath"
    if ($exitCode -ne 0) { exit $exitCode }
    exit 1
}
$xml = [xml][System.IO.File]::ReadAllText($resultsPath)
$run = $xml.SelectSingleNode('/test-run')
if ($null -eq $run) { throw "EditMode results did not contain a test-run node. See $resultsPath" }
$total = [int]$run.GetAttribute('total')
$passed = [int]$run.GetAttribute('passed')
$failed = [int]$run.GetAttribute('failed')
$skipped = [int]$run.GetAttribute('skipped')
Write-Output "UNITY_EXIT=$exitCode TOTAL=$total PASSED=$passed FAILED=$failed SKIPPED=$skipped RESULT=$($run.GetAttribute('result'))"
$xml.SelectNodes('//test-case') | ForEach-Object { Write-Output "TEST=$($_.GetAttribute('fullname')) RESULT=$($_.GetAttribute('result'))" }
Write-Output "RESULTS=$resultsPath"
Write-Output "LOG=$logPath"
Write-Output "RESULTS_SHA256=$((Get-FileHash -LiteralPath $resultsPath -Algorithm SHA256).Hash)"
Write-Output "LOG_SHA256=$((Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash)"
if ($FailureProbe) {
    $cases = @($xml.SelectNodes('//test-case'))
    $expectedName = 'Match3.Engine.Tests.Editor.UnityRunnerIntegrityProbeTests.IntentionalAssertionFailureProbe'
    $expectedCase = @($cases | Where-Object { $_.GetAttribute('fullname') -eq $expectedName -and $_.GetAttribute('result') -eq 'Failed' })
    if ($exitCode -eq 0 -or $total -ne 1 -or $passed -ne 0 -or $failed -ne 1 -or $skipped -ne 0 -or $expectedCase.Count -ne 1) {
        Write-Output 'UNITY_FAILURE: the intentional assertion failure did not produce the exact expected failing test and nonzero Unity exit.'
        exit 1
    }
    Write-Output 'PASS: the isolated intentional assertion produced one failed test, zero skipped tests, and a nonzero Unity exit.'
    exit 0
}
if ($exitCode -ne 0) { exit $exitCode }
if ($failed -ne 0 -or $skipped -ne 0 -or $passed -lt 1) { exit 1 }
Write-Output "PASS: Unity $TestPlatform smoke test completed."
