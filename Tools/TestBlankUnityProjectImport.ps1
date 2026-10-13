[CmdletBinding()]
param(
    [string] $SourceProjectPath = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [string] $EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceProjectPath).Path
$evidence = [System.IO.Path]::GetFullPath($EvidenceDirectory)
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw "Unity editor not found: $EditorPath" }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null

$project = Join-Path ([System.IO.Path]::GetTempPath()) ('eng-m00-blank-' + [guid]::NewGuid().ToString('N'))
$packageSource = Join-Path $source 'Packages/com.match3.engine'
$localPackage = Join-Path $project 'LocalPackages/com.match3.engine'
foreach ($directory in @((Join-Path $project 'Assets'), (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'), (Split-Path -Parent $localPackage))) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
Copy-Item -LiteralPath $packageSource -Destination $localPackage -Recurse

$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.match3.engine' = 'file:../LocalPackages/com.match3.engine'
        'com.chebanovdd.match3sdk' = 'https://github.com/LibraStack/Match3-SDK.git?path=src/Match3.UnityPackage/Assets/Plugins/Match3#771e62908056c2c5fa69937a52ca52ab589f19ea'
        'com.cysharp.unitask' = 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99'
    }
    testables = @('com.match3.engine')
}
$manifestPath = Join-Path $project 'Packages/manifest.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 6000.6.5f1`r`nm_EditorVersionWithRevision: 6000.6.5f1 (3ff58d469c8a)`r`n", [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/EditorBuildSettings.asset'), "%YAML 1.1`r`n%TAG !u! tag:unity3d.com,2011:`r`n--- !u!1045 &1`r`nEditorBuildSettings:`r`n  m_ObjectHideFlags: 0`r`n  serializedVersion: 2`r`n  m_Scenes: []`r`n  m_configObjects: {}`r`n", [System.Text.UTF8Encoding]::new($false))

$logPath = Join-Path $evidence 'blank-project-upm-import.log'
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $EditorPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in @('-batchmode','-nographics','-quit','-projectPath',$project,'-logFile',$logPath)) { [void]$start.ArgumentList.Add($argument) }
$process = [System.Diagnostics.Process]::Start($start)
$process.WaitForExit()
$exitCode = $process.ExitCode
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) { throw "Unity produced no blank-import log (exit $exitCode)." }
if ($exitCode -ne 0) { throw "Blank-project Unity import exited $exitCode. See $logPath" }

$lockPath = Join-Path $project 'Packages/packages-lock.json'
if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) { throw 'Blank project UPM import produced no packages-lock.json.' }
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable
foreach ($name in @('com.match3.engine','com.chebanovdd.match3sdk','com.cysharp.unitask','com.unity.test-framework')) {
    if (-not $lock.dependencies.ContainsKey($name)) { throw "Blank project lock is missing $name." }
}
foreach ($expected in @{
    'com.chebanovdd.match3sdk' = '771e62908056c2c5fa69937a52ca52ab589f19ea'
    'com.cysharp.unitask' = '2e993ff18f28c931602a07292df0b0804eebef99'
}.GetEnumerator()) {
    $resolved = $lock.dependencies[$expected.Key]
    if ($resolved.source -ne 'git' -or $resolved.hash -ne $expected.Value -or $resolved.version -notmatch [regex]::Escape($expected.Value)) {
        throw "Blank project resolved $($expected.Key) outside the required full commit pin."
    }
}
$framework = $lock.dependencies['com.unity.test-framework']
if ($framework.version -ne '1.8.0' -or $framework.source -ne 'builtin') { throw 'Blank project did not resolve Unity 6.6 built-in Test Framework 1.8.0.' }

$lockEvidence = Join-Path $evidence 'blank-project-packages-lock.json'
Copy-Item -LiteralPath $lockPath -Destination $lockEvidence
$summary = @(
    'BLANK_PROJECT_UPM_IMPORT=PASS'
    "UNITY_EXIT=$exitCode"
    "UNITY_VERSION=6000.6.5f1"
    "SDK_COMMIT=$($lock.dependencies['com.chebanovdd.match3sdk'].hash)"
    "UNITASK_COMMIT=$($lock.dependencies['com.cysharp.unitask'].hash)"
    "TEST_FRAMEWORK=$($framework.source):$($framework.version)"
    "MANIFEST_SHA256=$((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash)"
    "LOCK_SHA256=$((Get-FileHash -LiteralPath $lockEvidence -Algorithm SHA256).Hash)"
    "UNITY_LOG_SHA256=$((Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash)"
)
$summary | Set-Content -LiteralPath (Join-Path $evidence 'blank-project-summary.txt') -Encoding utf8NoBOM
$summary | ForEach-Object { Write-Output $_ }
