[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot '../../Tools/ThirdPartyProvenance.psm1'
Import-Module (Resolve-Path -LiteralPath $modulePath) -Force

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$actualFindings = @(Get-ThirdPartyProvenanceFindings -Root $root)
if ($actualFindings.Count -ne 0) {
    throw "Expected the current repository to pass, found: $($actualFindings -join '; ')"
}

$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $tempBase ("eng-m00-009-policy-" + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Assets/ThirdParty/Rimaethon/Gem-Match3') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Packages') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Assets/ThirdParty/Rimaethon/Gem-Match3/placeholder.txt') -Value 'test fixture only' -NoNewline
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Packages/manifest.json') -Value '{"dependencies":{}}' -NoNewline
    $blockedFindings = @(Get-ThirdPartyProvenanceFindings -Root $fixtureRoot)
    if (-not ($blockedFindings | Where-Object { $_ -like '*Restricted or unidentified reference source path*' })) {
        throw 'Negative path test failed: restricted Rimaethon/Gem-Match3 path was accepted.'
    }

    Remove-Item -LiteralPath (Join-Path $fixtureRoot 'Assets') -Recurse -Force
    $manifest = '{"dependencies":{"com.chebanovdd.match3sdk":"https://github.com/LibraStack/Match3-SDK.git?path=src/Match3.UnityPackage/Assets/Plugins/Match3#771e62908056c2c5fa69937a52ca52ab589f19ea","com.cysharp.unitask":"https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2e993ff18f28c931602a07292df0b0804eebef99"}}'
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Packages/manifest.json') -Value $manifest -NoNewline
    $missingNoticeFindings = @(Get-ThirdPartyProvenanceFindings -Root $fixtureRoot)
    if (-not ($missingNoticeFindings | Where-Object { $_ -like '*retained MIT notice*' })) {
        throw 'Negative notice test failed: pinned LibraStack package without its MIT notice was accepted.'
    }

    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'Documentation~') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Documentation~/THIRD_PARTY_NOTICES.md') -Value 'LibraStack/Match3-SDK; MIT License; Copyright (c) 2022 Dima; Cysharp/UniTask; Copyright (c) 2019 Yoshifumi Kawai / Cysharp, Inc.; e44b59b10deacda8e7d1a81555429cdb3f7d12c8' -NoNewline

    $validUltraGemProvenance = @'
| [JohannesBuchner/ultragem](https://github.com/JohannesBuchner/ultragem/tree/0e64a5bc66a5fce4b899bf0734c9cc7417b4ed12), commit `0e64a5bc66a5fce4b899bf0734c9cc7417b4ed12` | `LICENSE.rst`; Git blob SHA-1 `800af6b52afb288d2b5146644dfe8376b4fc7f2a`; SHA-256 `7ea84a9613f919b33151f651fb4b6be0ac4b660285d2f1539f140e869f2d46a1`; GitHub metadata `NOASSERTION`. | pinned |
| `gemgem.py` | Al Sweigart Simplified BSD | **COPY_ALLOWED_WITH_CONDITIONS** with full notice |
| `ultragem.py`, `gemengine.py` | Johannes-authored/unclear grant | **BLOCKED** |
| `graphics/*.png`, `screenshot.png` | Asset-to-author map missing | **BLOCKED** |
| `sounds/*.wav` | Sounds by ?? | **BLOCKED** |
| `gamestats/**`, `journey-auto/**` | No data rights stated | **BLOCKED**; ideas **REFERENCE_ONLY** |

No UltraGem code, graphics, sounds, levels, or statistics are imported by ENG-M00-009.
'@
    $provenancePath = Join-Path $fixtureRoot 'Documentation~/THIRD_PARTY_PROVENANCE.md'
    Set-Content -LiteralPath $provenancePath -Value $validUltraGemProvenance -NoNewline
    $noticeFindings = @(Get-ThirdPartyProvenanceFindings -Root $fixtureRoot)
    if ($noticeFindings.Count -ne 0) {
        throw "Positive provenance test failed: $($noticeFindings -join '; ')"
    }
    Write-Output 'PASS: pinned UltraGem source and per-file rights matrix are accepted.'

    $missingUltraGemSource = $validUltraGemProvenance.Replace('https://github.com/JohannesBuchner/ultragem/tree/0e64a5bc66a5fce4b899bf0734c9cc7417b4ed12', 'UltraGem (source omitted)')
    Set-Content -LiteralPath $provenancePath -Value $missingUltraGemSource -NoNewline
    $missingSourceFindings = @(Get-ThirdPartyProvenanceFindings -Root $fixtureRoot)
    if (-not ($missingSourceFindings | Where-Object { $_ -like '*UltraGem source URL is not pinned*' })) {
        throw 'Negative source test failed: an omitted UltraGem source URL was accepted.'
    }
    Write-Output 'PASS: an omitted UltraGem source URL is rejected.'

    $falselyPermissiveUltraGem = $validUltraGemProvenance.Replace('| `graphics/*.png`, `screenshot.png` | Asset-to-author map missing | **BLOCKED** |', '| `graphics/*.png`, `screenshot.png` | Asset-to-author map missing | **COPY_ALLOWED_WITH_CONDITIONS** |').Replace('| `sounds/*.wav` | Sounds by ?? | **BLOCKED** |', '| `sounds/*.wav` | Sounds by ?? | **COPY_ALLOWED_WITH_CONDITIONS** |').Replace('| `gamestats/**`, `journey-auto/**` | No data rights stated | **BLOCKED**; ideas **REFERENCE_ONLY** |', '| `gamestats/**`, `journey-auto/**` | No data rights stated | **COPY_ALLOWED_WITH_CONDITIONS** |')
    Set-Content -LiteralPath $provenancePath -Value $falselyPermissiveUltraGem -NoNewline
    $permissiveFindings = @(Get-ThirdPartyProvenanceFindings -Root $fixtureRoot)
    if (-not ($permissiveFindings | Where-Object { $_ -like '*falsely marked copyable*' })) {
        throw 'Negative rights test failed: falsely permissive asset/data grants were accepted.'
    }
    Write-Output 'PASS: falsely permissive graphics, sound, and data classifications are rejected.'
} finally {
    $fullFixture = [System.IO.Path]::GetFullPath($fixtureRoot)
    if (-not $fullFixture.StartsWith($tempBase, [System.StringComparison]::OrdinalIgnoreCase) -or $fullFixture -eq $tempBase) {
        throw "Refusing to remove fixture outside the temporary root: $fullFixture"
    }
    if (Test-Path -LiteralPath $fullFixture) {
        Remove-Item -LiteralPath $fullFixture -Recurse -Force
    }
}

Write-Output 'PASS: 6 provenance policy scenarios completed (repo allowed; restricted path rejected; missing MIT notice rejected; pinned UltraGem matrix accepted; omitted source rejected; false asset/data grants rejected).'
