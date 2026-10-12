[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '../../Tools/UnityBootstrapPolicy.psm1') -Force

$runtime = [ordered]@{ name='Match3.Engine.Runtime'; references=@(); includePlatforms=@(); optionalUnityReferences=@(); noEngineReferences=$true }
$editor = [ordered]@{ name='Match3.Engine.Editor'; references=@('Match3.Engine.Runtime'); includePlatforms=@('Editor') }
$runtimeTests = [ordered]@{ name='Match3.Engine.Runtime.Tests'; references=@('Match3.Engine.Runtime'); optionalUnityReferences=@('TestAssemblies'); autoReferenced=$false }
$editorTests = [ordered]@{ name='Match3.Engine.Editor.Tests'; references=@('Match3.Engine.Runtime','Match3.Engine.Editor'); includePlatforms=@('Editor'); optionalUnityReferences=@('TestAssemblies'); autoReferenced=$false }

if (@(Get-UnityBootstrapAssemblyFindings -Runtime $runtime -Editor $editor -RuntimeTests $runtimeTests -EditorTests $editorTests).Count -ne 0) {
    throw 'Positive test failed: valid runtime, Editor, and test assembly boundaries were rejected.'
}

$runtimeWithEngineAccess = [ordered]@{ name=$runtime.name; references=@(); includePlatforms=@(); optionalUnityReferences=@(); noEngineReferences=$false }
if (-not (@(Get-UnityBootstrapAssemblyFindings -Runtime $runtimeWithEngineAccess -Editor $editor -RuntimeTests $runtimeTests -EditorTests $editorTests) | Where-Object { $_ -like '*noEngineReferences=true*' })) {
    throw 'Negative test failed: runtime assembly with engine references enabled was accepted.'
}

$runtimeTestInPlayerGraph = [ordered]@{ name=$runtimeTests.name; references=@('Match3.Engine.Runtime'); optionalUnityReferences=@(); autoReferenced=$true }
if (-not (@(Get-UnityBootstrapAssemblyFindings -Runtime $runtime -Editor $editor -RuntimeTests $runtimeTestInPlayerGraph -EditorTests $editorTests) | Where-Object { $_ -like '*TestAssemblies*' })) {
    throw 'Negative test failed: a runtime test assembly exposed to player assemblies was accepted.'
}

Write-Output 'PASS: 3 Unity bootstrap policy scenarios (valid separated assemblies accepted; engine-dependent runtime and player-visible tests rejected).'
