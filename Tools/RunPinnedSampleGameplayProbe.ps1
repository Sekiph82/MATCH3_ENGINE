[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $SdkCheckoutPath,
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [string] $EditorPath = 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$sdk = (Resolve-Path -LiteralPath $SdkCheckoutPath).Path
$evidence = [System.IO.Path]::GetFullPath($EvidenceDirectory)
$expectedCommit = '771e62908056c2c5fa69937a52ca52ab589f19ea'
$actualCommit = (& git -C $sdk rev-parse HEAD).Trim()
if ($actualCommit -ne $expectedCommit) { throw "Pinned SDK checkout mismatch: expected $expectedCommit, got $actualCommit." }
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw "Unity editor not found: $EditorPath" }
$sampleSource = Join-Path $sdk 'samples/Unity.Match3'
if (-not (Test-Path -LiteralPath (Join-Path $sampleSource 'Assets/Scenes/MainScene.unity') -PathType Leaf)) { throw 'Pinned original sample MainScene is missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'LICENSE') -PathType Leaf)) { throw 'Pinned SDK MIT license file is missing.' }

New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$project = Join-Path ([System.IO.Path]::GetTempPath()) ('eng-m00-sample-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project | Out-Null
Copy-Item -Path (Join-Path $sampleSource '*') -Destination $project -Recurse
$editorAssets = Join-Path $project 'Assets/Editor'
New-Item -ItemType Directory -Path $editorAssets -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'M00PinnedSampleGameplayProbe.cs') -Destination (Join-Path $editorAssets 'M00PinnedSampleGameplayProbe.cs')

$manifestPath = Join-Path $project 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$sdkPackagePath = (Join-Path $sdk 'src/Match3.UnityPackage/Assets/Plugins/Match3').Replace('\','/')
$manifest.dependencies['com.chebanovdd.match3sdk'] = 'file:' + $sdkPackagePath
$manifest.dependencies['com.cysharp.unitask'] = 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99'
$manifest.dependencies['com.unity.test-framework'] = '1.8.0'
foreach ($optionalPackage in @('com.unity.collab-proxy','com.unity.ide.rider','com.unity.ide.visualstudio','com.unity.ide.vscode','com.unity.visualscripting','com.unity.modules.vr')) {
    $manifest.dependencies.Remove($optionalPackage)
}
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 16), [System.Text.UTF8Encoding]::new($false))
$versionPath = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
[System.IO.File]::WriteAllText($versionPath, "m_EditorVersion: 6000.6.5f1`r`nm_EditorVersionWithRevision: 6000.6.5f1 (3ff58d469c8a)`r`n", [System.Text.UTF8Encoding]::new($false))
$lockPath = Join-Path $project 'Packages/packages-lock.json'
if (Test-Path -LiteralPath $lockPath -PathType Leaf) { Remove-Item -LiteralPath $lockPath -Force }

$logPath = Join-Path $evidence 'pinned-sdk-sample-gameplay.log'
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $EditorPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
foreach ($argument in @('-batchmode','-nographics','-projectPath',$project,'-executeMethod','M00PinnedSampleGameplayProbe.Run','-logFile',$logPath)) { [void]$start.ArgumentList.Add($argument) }
$process = [System.Diagnostics.Process]::Start($start)
$process.WaitForExit()
$exitCode = $process.ExitCode
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) { throw "Unity produced no pinned sample gameplay log (exit $exitCode)." }
$log = [System.IO.File]::ReadAllText($logPath)
if ($exitCode -ne 0 -or $log -notmatch 'M00_SAMPLE_GAMEPLAY_PROBE=PASS') {
    throw "Pinned original sample gameplay probe failed (Unity exit $exitCode). See $logPath"
}

$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json -AsHashtable
if (-not $lock.dependencies.ContainsKey('com.chebanovdd.match3sdk') -or
    $lock.dependencies['com.chebanovdd.match3sdk'].source -notin @('local','embedded')) {
    throw 'Pinned SDK source checkout did not resolve as the local package in the migrated sample.'
}
$unitask = $lock.dependencies['com.cysharp.unitask']
if ($null -eq $unitask -or $unitask.source -ne 'git' -or $unitask.hash -ne '2e993ff18f28c931602a07292df0b0804eebef99') {
    throw 'Pinned original sample lock mismatch for UniTask.'
}
$dotween = @(Get-ChildItem -LiteralPath (Join-Path $project 'Assets') -Filter 'DOTween.dll' -Recurse -File | Select-Object -First 1)
if ($dotween.Count -eq 0) { throw 'The original sample DOTween binary was not present for dependency/import validation.' }
$summary = @(
    'PINNED_SAMPLE_GAMEPLAY=PASS'
    "SOURCE_COMMIT=$actualCommit"
    "UNITY_EXIT=$exitCode"
    "SDK_COMMIT=$actualCommit"
    "UNITASK_COMMIT=$($unitask.hash)"
    "DOTWEEN_ASSEMBLY=$($dotween[0].FullName.Substring($project.Length + 1))"
    "DOTWEEN_SHA256=$((Get-FileHash -LiteralPath $dotween[0].FullName -Algorithm SHA256).Hash)"
    "SAMPLE_LICENSE_SHA256=$((Get-FileHash -LiteralPath (Join-Path $sdk 'LICENSE') -Algorithm SHA256).Hash)"
    "UNITY_LOG_SHA256=$((Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash)"
)
$summary | Set-Content -LiteralPath (Join-Path $evidence 'pinned-sample-summary.txt') -Encoding utf8NoBOM
$summary | ForEach-Object { Write-Output $_ }
