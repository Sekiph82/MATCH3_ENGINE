[CmdletBinding()]
param([string] $Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
Import-Module (Join-Path $PSScriptRoot 'UnityBootstrapPolicy.psm1') -Force

$paths = @{
    Manifest = Join-Path $rootPath 'Packages/manifest.json'
    Lock = Join-Path $rootPath 'Packages/packages-lock.json'
    ProjectVersion = Join-Path $rootPath 'ProjectSettings/ProjectVersion.txt'
    BuildSettings = Join-Path $rootPath 'ProjectSettings/EditorBuildSettings.asset'
    Package = Join-Path $rootPath 'Packages/com.match3.engine/package.json'
    Runtime = Join-Path $rootPath 'Packages/com.match3.engine/Runtime/Match3.Engine.Runtime.asmdef'
    Editor = Join-Path $rootPath 'Packages/com.match3.engine/Editor/Match3.Engine.Editor.asmdef'
    RuntimeTests = Join-Path $rootPath 'Packages/com.match3.engine/Tests/Runtime/Match3.Engine.Runtime.Tests.asmdef'
    EditorTests = Join-Path $rootPath 'Packages/com.match3.engine/Tests/Editor/Match3.Engine.Editor.Tests.asmdef'
}
foreach ($path in $paths.Values) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Bootstrap file is missing: $path" }
}

if ((Get-Content -LiteralPath $paths.ProjectVersion -Raw) -notmatch '(?m)^m_EditorVersion: 6000\.6\.5f1$') {
    throw 'ProjectVersion.txt does not pin Unity 6000.6.5f1.'
}
$manifest = Get-Content -LiteralPath $paths.Manifest -Raw | ConvertFrom-Json -AsHashtable
$package = Get-Content -LiteralPath $paths.Package -Raw | ConvertFrom-Json -AsHashtable
$lock = Get-Content -LiteralPath $paths.Lock -Raw | ConvertFrom-Json -AsHashtable
$manifestDeps = $manifest.dependencies
if ($manifestDeps['com.match3.engine'] -ne 'file:com.match3.engine') { throw 'Embedded Match3 engine package is absent from Packages/manifest.json.' }
if (@($manifest.testables) -notcontains 'com.match3.engine') { throw 'Embedded package is not listed as testable.' }
if ($package.name -ne 'com.match3.engine' -or $package.version -ne '0.1.0' -or $package.unity -ne '6000.6') {
    throw 'The engine package identity or Unity compatibility line is incorrect.'
}
if ($package.dependencies['com.unity.test-framework'] -ne '1.8.0') { throw 'Package Test Framework dependency must match Unity 6.6 built-in version 1.8.0.' }

$assemblies = @{}
foreach ($name in @('Runtime','Editor','RuntimeTests','EditorTests')) {
    $assemblies[$name] = Get-Content -LiteralPath $paths[$name] -Raw | ConvertFrom-Json -AsHashtable
}
$findings = @(Get-UnityBootstrapAssemblyFindings -Runtime $assemblies.Runtime -Editor $assemblies.Editor -RuntimeTests $assemblies.RuntimeTests -EditorTests $assemblies.EditorTests)
if ($findings.Count -gt 0) { throw ($findings -join '; ') }

foreach ($name in @('com.match3.engine','com.chebanovdd.match3sdk','com.cysharp.unitask','com.unity.test-framework')) {
    if (-not $lock.dependencies.ContainsKey($name)) { throw "Resolved package lock is missing $name." }
}
if ($lock.dependencies['com.match3.engine'].source -notin @('embedded','local')) { throw 'Engine package is not resolved as a local embedded package.' }
if ($lock.dependencies['com.unity.test-framework'].version -ne '1.8.0' -or $lock.dependencies['com.unity.test-framework'].source -ne 'builtin') {
    throw 'Test Framework must resolve to the Unity 6.6 built-in version 1.8.0.'
}

foreach ($relative in @('Library/','Temp/','Obj/','Logs/')) {
    & git -C $rootPath check-ignore -q --no-index -- $relative
    if ($LASTEXITCODE -ne 0) { throw "Unity cache directory is not ignored by .gitignore: $relative" }
}
$trackedCacheFiles = @(& git -C $rootPath ls-files -- Library/ Temp/ Obj/ Logs/)
if ($trackedCacheFiles.Count -ne 0) { throw "Unity caches are tracked: $($trackedCacheFiles -join ', ')" }

Write-Output 'PASS: Unity bootstrap manifests, exact editor version, embedded package, separated asmdefs, test framework lock, build settings, and cache ignore rules are valid.'
