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
    $noticeFindings = @(Get-ThirdPartyProvenanceFindings -Root $fixtureRoot)
    if ($noticeFindings.Count -ne 0) {
        throw "Positive notice test failed: $($noticeFindings -join '; ')"
    }
} finally {
    $fullFixture = [System.IO.Path]::GetFullPath($fixtureRoot)
    if (-not $fullFixture.StartsWith($tempBase, [System.StringComparison]::OrdinalIgnoreCase) -or $fullFixture -eq $tempBase) {
        throw "Refusing to remove fixture outside the temporary root: $fullFixture"
    }
    if (Test-Path -LiteralPath $fullFixture) {
        Remove-Item -LiteralPath $fullFixture -Recurse -Force
    }
}

Write-Output 'PASS: 4 provenance policy scenarios (repo allowed, restricted path rejected, missing MIT notice rejected, retained MIT notice accepted).'
