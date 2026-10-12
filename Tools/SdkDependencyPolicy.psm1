Set-StrictMode -Version Latest

function Get-SdkDependencyPolicyFindings {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Collections.IDictionary] $Dependencies)

    $expected = @{
        'com.chebanovdd.match3sdk' = @{
            Prefix = 'https://github.com/LibraStack/Match3-SDK.git?path=src/Match3.UnityPackage/Assets/Plugins/Match3#'
            Commit = '771e62908056c2c5fa69937a52ca52ab589f19ea'
        }
        'com.cysharp.unitask' = @{
            Prefix = 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#'
            Commit = '2e993ff18f28c931602a07292df0b0804eebef99'
        }
    }
    $findings = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $expected.Keys) {
        if (-not $Dependencies.Contains($name)) {
            $findings.Add("Required package is missing: $name")
            continue
        }
        $entry = $expected[$name]
        $pattern = '^' + [regex]::Escape($entry.Prefix) + '(?<commit>[0-9a-f]{40})$'
        $match = [regex]::Match([string]$Dependencies[$name], $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $match.Success) {
            $findings.Add("Package must use a Git URL pinned to a full 40-character commit: $name")
        } elseif ($match.Groups['commit'].Value -ine $entry.Commit) {
            $findings.Add("Package commit does not match the audited compatibility pin: $name")
        }
    }
    foreach ($name in $Dependencies.Keys) {
        if ([string]$name -match '(?i)dotween|dg\.tweening') {
            $findings.Add("DOTween is sample-only and must not be an engine dependency: $name")
        }
    }
    return $findings.ToArray()
}

Export-ModuleMember -Function Get-SdkDependencyPolicyFindings
