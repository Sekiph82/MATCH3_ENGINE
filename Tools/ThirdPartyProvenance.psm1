Set-StrictMode -Version Latest

function Get-ThirdPartyProvenanceFindings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $findings = [System.Collections.Generic.List[string]]::new()
    $scanRoots = @('Assets', 'Packages', 'Runtime', 'Editor', 'Tests', 'Samples~', '.github')
    $blockedSegments = @(
        'rimaethon', 'gem-match3', 'thefcan', 'unity-match3', 'daltonbr',
        'ultragem', 'gemhuntermatch3', 'game-simulation-match-3-sample'
    )

    foreach ($relativeRoot in $scanRoots) {
        $scanPath = Join-Path $resolvedRoot $relativeRoot
        if (-not (Test-Path -LiteralPath $scanPath -PathType Container)) { continue }

        foreach ($file in Get-ChildItem -LiteralPath $scanPath -Force -Recurse -File -ErrorAction Stop) {
            $relativePath = [System.IO.Path]::GetRelativePath($resolvedRoot, $file.FullName).Replace('\', '/')
            $segments = $relativePath.Split('/') | ForEach-Object { $_.ToLowerInvariant() }
            foreach ($segment in $segments) {
                if ($blockedSegments -contains $segment) {
                    $findings.Add("Restricted or unidentified reference source path: $relativePath")
                    break
                }
            }
        }
    }

    $manifestPath = Join-Path $resolvedRoot 'Packages/manifest.json'
    $noticePath = Join-Path $resolvedRoot 'Documentation~/THIRD_PARTY_NOTICES.md'
    $notice = if (Test-Path -LiteralPath $noticePath -PathType Leaf) {
        Get-Content -LiteralPath $noticePath -Raw
    } else {
        ''
    }

    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
            $sdkDependency = $manifest.dependencies.PSObject.Properties['com.chebanovdd.match3sdk']
            $sdkUri = if ($sdkDependency) { $sdkDependency.Value } else { $null }
            if ($sdkUri) {
                if ($sdkUri -ne 'https://github.com/LibraStack/Match3-SDK.git?path=src/Match3.UnityPackage/Assets/Plugins/Match3#771e62908056c2c5fa69937a52ca52ab589f19ea') {
                    $findings.Add('LibraStack package dependency differs from the audited origin, package path, or full commit pin.')
                }
                if ($notice -notmatch 'MIT License' -or $notice -notmatch 'LibraStack/Match3-SDK' -or $notice -notmatch 'Copyright \(c\) 2022 Dima') {
                    $findings.Add('LibraStack package is present without its retained MIT notice in Documentation~/THIRD_PARTY_NOTICES.md.')
                }

                $unitaskDependency = $manifest.dependencies.PSObject.Properties['com.cysharp.unitask']
                $unitaskUri = if ($unitaskDependency) { $unitaskDependency.Value } else { $null }
                if ($unitaskUri -ne 'https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99') {
                    $findings.Add('UniTask transitive package dependency is not overridden with the audited full commit pin.')
                }
                if ($notice -notmatch 'Cysharp/UniTask' -or $notice -notmatch 'Copyright \(c\) 2019 Yoshifumi Kawai / Cysharp, Inc\.' -or $notice -notmatch 'e44b59b10deacda8e7d1a81555429cdb3f7d12c8') {
                    $findings.Add('UniTask is missing its retained MIT notice or upstream LICENSE blob SHA in Documentation~/THIRD_PARTY_NOTICES.md.')
                }
            }
        } catch {
            $findings.Add("Packages/manifest.json is invalid JSON: $($_.Exception.Message)")
        }
    }

    foreach ($relativeRoot in @('Assets/ThirdParty/LibraStack', 'Packages/ThirdParty/LibraStack', 'Assets/ThirdParty/AlperenGarip', 'Packages/ThirdParty/AlperenGarip')) {
        $componentPath = Join-Path $resolvedRoot $relativeRoot
        if (Test-Path -LiteralPath $componentPath -PathType Container) {
            if ($notice -notmatch 'MIT License' -or $notice -notmatch [regex]::Escape(($relativeRoot -split '/')[-1])) {
                $findings.Add("$relativeRoot is present without its retained MIT notice in Documentation~/THIRD_PARTY_NOTICES.md.")
            }
        }
    }

    return $findings.ToArray()
}

Export-ModuleMember -Function Get-ThirdPartyProvenanceFindings
