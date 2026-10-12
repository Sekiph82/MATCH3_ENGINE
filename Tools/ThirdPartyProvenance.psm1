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
                if ($sdkUri -notmatch '#[0-9a-fA-F]{40}$') {
                    $findings.Add('LibraStack package dependency is not pinned to a full 40-character commit SHA.')
                }
                if ($notice -notmatch 'MIT License' -or $notice -notmatch 'LibraStack/Match3-SDK') {
                    $findings.Add('LibraStack package is present without its retained MIT notice in Documentation~/THIRD_PARTY_NOTICES.md.')
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
