[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scanner = Join-Path (Split-Path -Parent $PSScriptRoot) '..\Tools\ScanUnityReleaseArtifact.ps1'
$scanner = (Resolve-Path -LiteralPath $scanner).Path
$powerShell = (Get-Command pwsh -ErrorAction Stop).Source
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$work = [System.IO.Path]::GetFullPath((Join-Path $tempRoot ('eng-m00-006-scanner-' + [guid]::NewGuid().ToString('N'))))
if (-not $work.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create test fixtures outside the temporary directory: $work"
}
New-Item -ItemType Directory -Path $work | Out-Null

function Write-FixtureFile([string] $root, [string] $relativePath, [string] $content) {
    $path = Join-Path $root ($relativePath.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar))
    $parent = Split-Path -Parent $path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
}

function New-FixtureZip([string] $name, [hashtable] $entries) {
    $source = Join-Path $work ($name + '-source')
    $archive = Join-Path $work ($name + '.zip')
    New-Item -ItemType Directory -Path $source | Out-Null
    foreach ($entry in $entries.GetEnumerator()) {
        Write-FixtureFile $source $entry.Key $entry.Value
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($source, $archive)
    return $archive
}

function Invoke-Scanner([string] $artifact) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $powerShell
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-NoProfile', '-File', $scanner, '-ArtifactPath', $artifact)) {
        [void]$start.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($start)
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = $stdoutTask.Result + $stderrTask.Result
    }
}

try {
    $positive = New-FixtureZip 'positive' @{
        'Demo_Data/Resources/config.json' = '{"theme":"neutral"}'
        'Demo_Data/StreamingAssets/readme.txt' = 'demo payload'
        'Demo_Data/StreamingAssets/Factory/FactoryGems.png' = 'themed game artwork'
    }
    $result = Invoke-Scanner $positive
    if ($result.ExitCode -ne 0 -or $result.Output -notmatch 'SCAN=PASS') {
        throw "Allowed release fixture, including themed Factory artwork, should pass; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Write-Output 'PASS: permitted Resources, StreamingAssets payload, and themed Factory artwork are accepted.'

    $developerResourceFixtures = @(
        @{
            Name = 'diagnostics-evidence'
            Path = 'Demo_Data/StreamingAssets/Diagnostics/evidence.json'
            Content = '{"note":"innocuous payload"}'
        },
        @{
            Name = 'factory-database'
            Path = 'Demo_Data/StreamingAssets/Factory/FactoryDatabase.bytes'
            Content = 'innocuous payload'
        },
        @{
            Name = 'addressables-debug-overlay'
            Path = 'Demo_Data/Addressables/Development/DebugOverlay.prefab'
            Content = 'innocuous payload'
        }
    )
    foreach ($fixture in $developerResourceFixtures) {
        $archive = New-FixtureZip $fixture.Name @{ $fixture.Path = $fixture.Content }
        $result = Invoke-Scanner $archive
        if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'forbidden path') {
            throw "Developer resource '$($fixture.Path)' must fail by path even with innocuous payload; exit=$($result.ExitCode) output=$($result.Output)"
        }
        Write-Output "PASS: developer resource '$($fixture.Path)' is rejected by path."
    }

    $badAssembly = New-FixtureZip 'test-assembly' @{
        'Demo_Data/Managed/NUnit.Framework.dll' = 'forbidden test assembly'
    }
    $result = Invoke-Scanner $badAssembly
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'SCAN=FAIL') {
        throw "Test assembly fixture must fail; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Write-Output 'PASS: a packaged NUnit test assembly is rejected.'

    $badScript = New-FixtureZip 'resource-script' @{
        'Demo_Data/StreamingAssets/Diagnostics/Fixture.cs' = 'class Fixture {}'
    }
    $result = Invoke-Scanner $badScript
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'forbidden path') {
        throw "Script source in StreamingAssets must fail; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Write-Output 'PASS: script fixtures in StreamingAssets are rejected.'

    $badFlag = New-FixtureZip 'test-flag' @{
        'ProjectSettings/BuildSettings.asset' = "includeTestAssemblies: 1`n"
    }
    $result = Invoke-Scanner $badFlag
    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'test-runner or test-assembly marker') {
        throw "Test assembly build flag must fail; exit=$($result.ExitCode) output=$($result.Output)"
    }
    Write-Output 'PASS: IncludeTestAssemblies build flag is rejected.'
    Write-Output 'SCANNER_TESTS=7 PASSED=7 FAILED=0'
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
