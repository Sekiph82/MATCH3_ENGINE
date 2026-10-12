[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../../Tools/SdkDependencyPolicy.psm1') -Force
$unitask = 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99'
$valid = [ordered]@{
    'com.chebanovdd.match3sdk' = 'https://github.com/LibraStack/Match3-SDK.git?path=src/Match3.UnityPackage/Assets/Plugins/Match3#771e62908056c2c5fa69937a52ca52ab589f19ea'
    'com.cysharp.unitask' = $unitask
    'com.unity.modules.audio' = '1.0.0'
}
if (@(Get-SdkDependencyPolicyFindings -Dependencies $valid).Count -ne 0) {
    throw 'Positive test failed: the audited pinned configuration was rejected.'
}
$unpinned = [ordered]@{
    'com.chebanovdd.match3sdk' = $valid['com.chebanovdd.match3sdk']
    'com.cysharp.unitask' = 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask'
}
if (-not (@(Get-SdkDependencyPolicyFindings -Dependencies $unpinned) | Where-Object { $_ -like '*full 40-character commit*' })) {
    throw 'Negative test failed: an unpinned transitive UniTask URL was accepted.'
}
$tagOnly = [ordered]@{
    'com.chebanovdd.match3sdk' = 'https://github.com/LibraStack/Match3-SDK.git?path=src/Match3.UnityPackage/Assets/Plugins/Match3#v0.1.3'
    'com.cysharp.unitask' = $unitask
}
if (-not (@(Get-SdkDependencyPolicyFindings -Dependencies $tagOnly) | Where-Object { $_ -like '*full 40-character commit*' })) {
    throw 'Negative test failed: a tag-only SDK reference was accepted.'
}
$withTween = [ordered]@{
    'com.chebanovdd.match3sdk' = $valid['com.chebanovdd.match3sdk']
    'com.cysharp.unitask' = $unitask
    'com.demigiant.dotween' = '1.0.0'
}
if (-not (@(Get-SdkDependencyPolicyFindings -Dependencies $withTween) | Where-Object { $_ -like '*DOTween is sample-only*' })) {
    throw 'Negative test failed: DOTween was accepted as an engine dependency.'
}
Write-Output 'PASS: 4 dependency policy scenarios (exact pins accepted; unpinned transitive URL, tag-only SDK ref, and DOTween engine dependency rejected).'
