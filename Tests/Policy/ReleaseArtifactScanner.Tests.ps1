[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scanner = Join-Path $repositoryRoot 'Tools/ScanUnityReleaseArtifact.ps1'
$powerShell = (Get-Command pwsh -ErrorAction Stop).Source
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$work = [System.IO.Path]::GetFullPath((Join-Path $tempRoot ('eng-m00-006-scanner-' + [guid]::NewGuid().ToString('N'))))
if (-not $work.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create test fixtures outside the temporary directory: $work"
}
New-Item -ItemType Directory -Path $work | Out-Null
$script:passed = 0

function Assert-Pass([string] $message) {
    Write-Output "PASS: $message"
    $script:passed++
}

function New-FixtureZip([string] $name, [hashtable] $entries) {
    $archive = Join-Path $work ($name + '.zip')
    $file = [System.IO.File]::Open($archive, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($file, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($entry in $entries.GetEnumerator()) {
                $zipEntry = $zip.CreateEntry([string]$entry.Key)
                $stream = $zipEntry.Open()
                try {
                    if ($entry.Value -is [byte[]]) {
                        $stream.Write($entry.Value, 0, $entry.Value.Length)
                    }
                    else {
                        $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]$entry.Value)
                        $stream.Write($bytes, 0, $bytes.Length)
                    }
                }
                finally { $stream.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $file.Dispose() }
    return $archive
}

function Invoke-Scanner([string[]] $arguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $powerShell
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in (@('-NoProfile', '-File', $scanner) + $arguments)) {
        [void]$start.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $stdoutTask.Result + $stderrTask.Result
        }
    }
    finally { $process.Dispose() }
}

try {
    $positive = New-FixtureZip 'positive-themed-content' @{
        'Demo_Data/Resources/config.json' = '{"theme":"neutral"}'
        'Demo_Data/StreamingAssets/readme.txt' = 'demo payload'
        'Demo_Data/StreamingAssets/Factory/FactoryGems.png' = 'themed gem sprites'
        'Demo_Data/StreamingAssets/Factory/FactoryBackground.jpg' = 'themed background'
        'Demo_Data/StreamingAssets/Factory/FactoryIcon.webp' = 'themed icon'
        'Demo_Data/StreamingAssets/Factory/FactoryDebugGems.png' = 'legitimate theme artwork'
    }
    $result = Invoke-Scanner @('-ArtifactPath', $positive)
    if ($result.ExitCode -ne 0 -or $result.Output -notmatch 'SCAN=PASS') {
        throw "Allowed content fixture, including themed Factory artwork, should pass; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Assert-Pass 'positive ZIP with multiple Factory art formats passes'

    $developerResourceFixtures = @(
        # All payloads are harmless and contain no Unity/NUnit/test-runner markers.
        @{ Name = 'r01-diagnostics-evidence'; Path = 'Demo_Data/StreamingAssets/Diagnostics/evidence.json'; Content = '{"note":"innocuous payload"}' },
        @{ Name = 'r01-factory-database'; Path = 'Demo_Data/StreamingAssets/Factory/FactoryDatabase.bytes'; Content = [byte[]](0, 1, 2, 255) },
        @{ Name = 'r01-addressables-debug-overlay'; Path = 'Demo_Data/Addressables/Development/DebugOverlay.prefab'; Content = [byte[]](17, 0, 9, 255) },
        @{ Name = 'r02-diagnostics-new-debug-logs'; Path = 'Demo_Data/StreamingAssets/Diagnostics/new_debug_logs.json'; Content = '{"entries":[]}' },
        @{ Name = 'r02-factory-pipeline-database'; Path = 'Demo_Data/StreamingAssets/Factory/FactoryPipelineDatabase.bytes'; Content = [byte[]](3, 4, 5, 0, 254) },
        @{ Name = 'r02-addressables-debug-overlay-v2'; Path = 'Demo_Data/Addressables/Development/DebugOverlayV2.prefab'; Content = [byte[]](8, 0, 6, 255) },
        @{ Name = 'r02-resources-unity-capture'; Path = 'Demo_Data/Resources/DeveloperLogs/UnityCapture.txt'; Content = 'capture complete' },
        @{ Name = 'variant-diagnostics-suffixed'; Path = 'Demo_Data/StreamingAssets/Diagnostics/DebugLogs_2026_10.json'; Content = '{"ok":true}' },
        @{ Name = 'variant-factory-pipeline-prefix-suffix'; Path = 'Demo_Data/StreamingAssets/Factory/CustomPipelineOutput_v9.BYTES'; Content = [byte[]](10, 0, 11, 255) },
        @{ Name = 'variant-factory-database-mutated'; Path = 'Demo_Data/StreamingAssets/Factory/FactoryDatabaseBackup2.bin'; Content = [byte[]](12, 0, 13, 255) },
        @{ Name = 'variant-factory-database-extension'; Path = 'Demo_Data/StreamingAssets/Factory/FactoryToolingDatabaseV3.db'; Content = [byte[]](18, 0, 19, 255) },
        @{ Name = 'variant-factory-pipeline-directory'; Path = 'Demo_Data/StreamingAssets/Factory/Pipeline/BuildCache_42.bin'; Content = [byte[]](20, 0, 21, 255) },
        @{ Name = 'variant-addressables-mixed-case'; Path = 'dEmO_dAtA\aDdReSsAbLeS\dEvElOpMeNt\DebugOverlayReleaseV3.PREFAB'; Content = [byte[]](14, 0, 15, 255) },
        @{ Name = 'variant-resource-mixed-case'; Path = 'Demo_Data\rEsOuRcEs\dEvElOpErLoGs\CaptureAgent_42.LOG'; Content = [byte[]](16, 0, 17, 255) },
        @{ Name = 'variant-diagnostics-mixed-case'; Path = 'Demo_Data\sTrEaMiNgAsSeTs\dIaGnOsTiCs\new_debug_logs_r3.JSON'; Content = '{"safe":1}' }
    )

    foreach ($fixture in $developerResourceFixtures) {
        $archive = New-FixtureZip $fixture.Name @{ $fixture.Path = $fixture.Content }
        $result = Invoke-Scanner @('-ArtifactPath', $archive)
        if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'SCAN=FAIL' -or $result.Output -notmatch 'forbidden path') {
            throw "Developer resource family '$($fixture.Path)' must fail closed by path; exit=$($result.ExitCode) output=$($result.Output)"
        }
        Assert-Pass "developer resource family rejects '$($fixture.Path)' with marker-free payload"
    }

    $mobileArchiveSource = New-FixtureZip 'mobile-archive-source' @{
        'Demo_Data/Resources/DeveloperLogs/ArchiveProbe_v4.txt' = 'marker-free payload'
    }
    foreach ($extension in @('.apk', '.aab', '.ipa')) {
        $mobileArchive = Join-Path $work ('family-probe' + $extension)
        [System.IO.File]::Copy($mobileArchiveSource, $mobileArchive)
        $result = Invoke-Scanner @('-ArtifactPath', $mobileArchive)
        if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'SCAN=FAIL' -or $result.Output -notmatch 'forbidden path') {
            throw "ZIP-formatted $extension fixture must scan developer paths; exit=$($result.ExitCode) output=$($result.Output)"
        }
        Assert-Pass "ZIP-formatted $extension archive scans nested developer paths"
    }

    $badAssembly = New-FixtureZip 'test-assembly' @{
        'Demo_Data/Managed/NUnit.Framework.dll' = [byte[]](1, 0, 2, 255)
    }
    $result = Invoke-Scanner @('-ArtifactPath', $badAssembly)
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'SCAN=FAIL') {
        throw "Test assembly fixture must fail; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Assert-Pass 'marker-free packaged NUnit assembly is rejected by path'

    $badScript = New-FixtureZip 'resource-script' @{
        'Demo_Data/StreamingAssets/Diagnostics/Fixture.cs' = 'class Fixture {}'
    }
    $result = Invoke-Scanner @('-ArtifactPath', $badScript)
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'forbidden path') {
        throw "Script source in StreamingAssets must fail; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Assert-Pass 'script source in a resource directory is rejected'

    $badFlag = New-FixtureZip 'test-flag' @{
        'ProjectSettings/BuildSettings.asset' = "includeTestAssemblies: 1`n"
    }
    $result = Invoke-Scanner @('-ArtifactPath', $badFlag)
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'test-runner or test-assembly marker') {
        throw "Test assembly build flag must fail; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Assert-Pass 'IncludeTestAssemblies build flag is rejected'

    $sourceRoot = Join-Path $work 'source-positive'
    $sourceAssets = Join-Path $sourceRoot 'Assets'
    New-Item -ItemType Directory -Path (Join-Path $sourceAssets 'Match3Demo/Runtime') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sourceAssets 'Match3Demo/StreamingAssets/Factory') -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $sourceAssets 'Match3Demo/Runtime/Runtime.cs'), 'internal class Runtime {}')
    [System.IO.File]::WriteAllText((Join-Path $sourceAssets 'Match3Demo/StreamingAssets/Factory/FactoryGems.png'), 'art')
    $manifest = Join-Path $work 'source-positive-manifest.txt'
    $result = Invoke-Scanner @('-SourceAssetRoot', $sourceAssets, '-ManifestPath', $manifest)
    if ($result.ExitCode -ne 0 -or $result.Output -notmatch 'SOURCE_MANIFEST=PASS' -or -not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
        throw "Clean source asset inventory should produce a manifest; exit=$($result.ExitCode) output=$($result.Output)"
    }
    $manifestText = Get-Content -LiteralPath $manifest -Raw
    if ($manifestText -notmatch 'Match3Demo/Runtime/Runtime.cs SHA256=[0-9a-f]{64} BYTES=') {
        throw "Source manifest is missing the normalized asset path and SHA-256 entry: $manifestText"
    }
    Assert-Pass 'pre-build source inventory produces a normalized SHA-256 manifest'

    $blockedRoot = Join-Path $work 'source-blocked'
    $blockedAssets = Join-Path $blockedRoot 'Assets'
    New-Item -ItemType Directory -Path (Join-Path $blockedAssets 'Match3Demo/Resources/DeveloperLogs') -Force | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $blockedAssets 'Match3Demo/Resources/DeveloperLogs/Capture.txt'), [byte[]](3, 0, 4, 255))
    $blockedManifest = Join-Path $work 'source-blocked-manifest.txt'
    $result = Invoke-Scanner @('-SourceAssetRoot', $blockedAssets, '-ManifestPath', $blockedManifest)
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'SOURCE_MANIFEST=FAIL' -or (Test-Path -LiteralPath $blockedManifest)) {
        throw "Developer-only source asset must prevent manifest creation; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Assert-Pass 'pre-build source inventory fails closed and emits no manifest for a developer-only asset'

    $opaqueRoot = Join-Path $work 'source-opaque-bundle'
    $opaqueAssets = Join-Path $opaqueRoot 'Assets'
    New-Item -ItemType Directory -Path (Join-Path $opaqueAssets 'Match3Demo/StreamingAssets/Bundles') -Force | Out-Null
    [System.IO.File]::WriteAllBytes((Join-Path $opaqueAssets 'Match3Demo/StreamingAssets/Bundles/unknown-content.bundle'), [byte[]](7, 0, 8, 255))
    $opaqueManifest = Join-Path $work 'source-opaque-manifest.txt'
    $result = Invoke-Scanner @('-SourceAssetRoot', $opaqueAssets, '-ManifestPath', $opaqueManifest)
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'SOURCE_MANIFEST=FAIL' -or (Test-Path -LiteralPath $opaqueManifest)) {
        throw "Opaque prebuilt source bundle must prevent manifest creation; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Assert-Pass 'pre-build source inventory refuses an opaque prebuilt bundle'

    Write-Output "SCANNER_TESTS=$script:passed PASSED=$script:passed FAILED=0 SKIPPED=0"
}
finally {
    if (Test-Path -LiteralPath $work) {
        $resolvedWork = (Resolve-Path -LiteralPath $work).Path
        if (-not $resolvedWork.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a scanner fixture path outside the temporary directory: $resolvedWork"
        }
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
