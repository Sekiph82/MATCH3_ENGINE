[CmdletBinding()]
param(
    [string] $ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string] $EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe',
    [switch] $PersistResolvedLock
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SdkDependencyPolicy.psm1') -Force
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
$manifestPath = Join-Path $project 'Packages/manifest.json'
$lockPath = Join-Path $project 'Packages/packages-lock.json'
$versionPath = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
foreach ($path in @($manifestPath, $lockPath, $versionPath, $EditorPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required probe file missing: $path" }
}
if ((Get-Content -LiteralPath $versionPath -Raw) -notmatch '(?m)^m_EditorVersion: 6000\.6\.') {
    throw 'Refusing probe: project does not identify Unity 6000.6.'
}

$manifestBytes = [System.IO.File]::ReadAllBytes($manifestPath)
$lockBytes = [System.IO.File]::ReadAllBytes($lockPath)
$manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes))
$lockHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($lockBytes))
$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$work = Join-Path $tempBase ('eng-m00-sdk-resolve-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$manifestBackup = Join-Path $work 'manifest.original.json'
$lockBackup = Join-Path $work 'packages-lock.original.json'
$manifestProbe = Join-Path $work 'manifest.probe.json'
$lockProbe = Join-Path $work 'packages-lock.probe.json'
$logPath = Join-Path $work 'unity-resolve.log'
[System.IO.File]::WriteAllBytes($manifestBackup, $manifestBytes)
[System.IO.File]::WriteAllBytes($lockBackup, $lockBytes)

$dependencies = if ($PersistResolvedLock) {
    (Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable).dependencies
} else {
    [ordered]@{
    'com.chebanovdd.match3sdk' = 'https://github.com/LibraStack/Match3-SDK.git?path=src/Match3.UnityPackage/Assets/Plugins/Match3#771e62908056c2c5fa69937a52ca52ab589f19ea'
    'com.cysharp.unitask' = 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99'
    }
}
$findings = @(Get-SdkDependencyPolicyFindings -Dependencies $dependencies)
if ($findings.Count -gt 0) { throw "Invalid probe manifest: $($findings -join '; ')" }
$probeJson = if ($PersistResolvedLock) { [System.IO.File]::ReadAllText($manifestPath) } else { @{ dependencies = $dependencies } | ConvertTo-Json -Depth 8 }
[System.IO.File]::WriteAllText($manifestProbe, $probeJson, [System.Text.UTF8Encoding]::new($false))
$exitCode = $null
$resolutionError = $null

try {
    if (-not $PersistResolvedLock) { [System.IO.File]::WriteAllText($manifestPath, $probeJson, [System.Text.UTF8Encoding]::new($false)) }
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $EditorPath
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in @('-batchmode', '-nographics', '-quit', '-projectPath', $project, '-logFile', $logPath)) {
        [void]$start.ArgumentList.Add($argument)
    }
    $process = [System.Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    if (Test-Path -LiteralPath $lockPath) {
        [System.IO.File]::Copy($lockPath, $lockProbe, $true)
        $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable
        foreach ($package in @{
            'com.chebanovdd.match3sdk' = @{ Version = '0.1.3'; Commit = '771e62908056c2c5fa69937a52ca52ab589f19ea' }
            'com.cysharp.unitask' = @{ Version = '2.5.11'; Commit = '2e993ff18f28c931602a07292df0b0804eebef99' }
        }.GetEnumerator()) {
            if (-not $lock.dependencies.ContainsKey($package.Key)) {
                throw "Resolved package lock is missing $($package.Key)."
            }
            $record = $lock.dependencies[$package.Key]
            if ($record.source -ne 'git' -or $record.hash -ne $package.Value.Commit -or $record.version -notmatch [regex]::Escape($package.Value.Commit)) {
                throw "Resolved lock entry does not match $($package.Key) at the expected Git commit."
            }
            $cachePath = Join-Path $project 'Library/PackageCache'
            $packageInfo = Get-ChildItem -LiteralPath $cachePath -Directory | Where-Object {$_.Name -like ($package.Key + '@*')} | ForEach-Object {
                $infoPath = Join-Path $_.FullName 'package.json'
                if (Test-Path -LiteralPath $infoPath) { Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json }
            } | Where-Object {$_.name -eq $package.Key} | Select-Object -First 1
            if ($null -eq $packageInfo -or $packageInfo.version -ne $package.Value.Version) {
                throw "Resolved package cache does not contain $($package.Key) version $($package.Value.Version)."
            }
        }
    } else {
        throw 'Unity did not produce Packages/packages-lock.json.'
    }
    if ($exitCode -ne 0) { $resolutionError = "Unity package resolution exited with code $exitCode. See $logPath" }
} finally {
    [System.IO.File]::WriteAllBytes($manifestPath, $manifestBytes)
    if (-not $PersistResolvedLock) { [System.IO.File]::WriteAllBytes($lockPath, $lockBytes) }
}

$manifestRestored = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($manifestPath))) -eq $manifestHash
if (-not $manifestRestored) { throw 'Probe failed to preserve the original manifest byte-for-byte.' }
if (-not $PersistResolvedLock) {
    $lockRestored = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([System.IO.File]::ReadAllBytes($lockPath))) -eq $lockHash
    if (-not $lockRestored) { throw 'Probe failed to restore the original package lock byte-for-byte.' }
}
if ($null -ne $resolutionError) { throw $resolutionError }
Write-Output "PASS: Unity package resolution exit code $exitCode; exact SDK 0.1.3 and UniTask 2.5.11 Git commits found in package lock."
Write-Output "MANIFEST: preserved byte-for-byte, SHA-256 $manifestHash."
if ($PersistResolvedLock) { Write-Output "LOCK: newly resolved project lock retained at $lockPath" } else { Write-Output "LOCK: original SHA-256 $lockHash restored byte-for-byte." }
Write-Output "REVIEW: probe manifest, original backups, resolved lock, and Unity log retained under $work"
