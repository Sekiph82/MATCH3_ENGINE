Set-StrictMode -Version Latest

function Get-UnityBootstrapAssemblyFindings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Collections.IDictionary] $Runtime,
        [Parameter(Mandatory)][System.Collections.IDictionary] $Editor,
        [Parameter(Mandatory)][System.Collections.IDictionary] $RuntimeTests,
        [Parameter(Mandatory)][System.Collections.IDictionary] $EditorTests
    )

    $findings = [System.Collections.Generic.List[string]]::new()
    if (-not $Runtime.noEngineReferences) { $findings.Add('Runtime assembly must set noEngineReferences=true.') }
    if (@($Runtime.includePlatforms).Count -ne 0) { $findings.Add('Runtime assembly must not be Editor-only.') }
    if (@($Runtime.references).Count -ne 0) { $findings.Add('Runtime assembly must not reference test or Editor assemblies.') }
    if (@($Runtime.optionalUnityReferences) -contains 'TestAssemblies') { $findings.Add('Runtime assembly must not be a test assembly.') }

    if (@($Editor.includePlatforms) -notcontains 'Editor') { $findings.Add('Editor assembly must be Editor-only.') }
    if (@($Editor.references) -notcontains 'Match3.Engine.Runtime') { $findings.Add('Editor assembly must reference the runtime assembly.') }

    if (@($RuntimeTests.references) -notcontains 'Match3.Engine.Runtime') { $findings.Add('Runtime tests must reference the runtime assembly.') }
    if (@($RuntimeTests.optionalUnityReferences) -notcontains 'TestAssemblies') { $findings.Add('Runtime tests must be marked TestAssemblies.') }
    if ($RuntimeTests.autoReferenced) { $findings.Add('Runtime tests must not be auto-referenced by player assemblies.') }

    if (@($EditorTests.includePlatforms) -notcontains 'Editor') { $findings.Add('Editor tests must be Editor-only.') }
    if (@($EditorTests.optionalUnityReferences) -notcontains 'TestAssemblies') { $findings.Add('Editor tests must be marked TestAssemblies.') }
    if (@($EditorTests.references) -notcontains 'Match3.Engine.Runtime' -or @($EditorTests.references) -notcontains 'Match3.Engine.Editor') {
        $findings.Add('Editor tests must reference the runtime and Editor assemblies.')
    }
    if ($EditorTests.autoReferenced) { $findings.Add('Editor tests must not be auto-referenced.') }

    return $findings.ToArray()
}

Export-ModuleMember -Function Get-UnityBootstrapAssemblyFindings
