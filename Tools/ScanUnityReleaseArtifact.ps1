[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Artifact')][string] $ArtifactPath,
    [Parameter(Mandatory = $true, ParameterSetName = 'SourceManifest')][string[]] $SourceAssetRoot,
    [Parameter(Mandatory = $true, ParameterSetName = 'SourceManifest')][string] $ManifestPath
)

$ErrorActionPreference = 'Stop'
$archiveExtensions = @('.apk', '.aab', '.ipa', '.zip', '.unitypackage')
$maxInspectBytes = 268435456
$script:scannedItems = 0
$script:violations = [System.Collections.Generic.List[string]]::new()

function Get-DeveloperResourceFinding([string] $normalizedPath) {
    $developerDirectoryFamilies = @(
        '(?i)(^|/)StreamingAssets/Diagnostics(/|$)',
        '(?i)(^|/)Addressables/Development(/|$)',
        '(?i)(^|/)Resources/DeveloperLogs(/|$)',
        '(?i)(^|/)StreamingAssets/Factory/(DeveloperOnly|Tooling|Diagnostics|Debug|Database|Pipeline|PipelineOutput|PipelineCache)(/|$)'
    )
    foreach ($pattern in $developerDirectoryFamilies) {
        if ($normalizedPath -match $pattern) {
            return "developer-only resource family matched '$pattern'"
        }
    }

    if ($normalizedPath -match '(?i)(^|/)StreamingAssets/Factory/') {
        $extension = [System.IO.Path]::GetExtension($normalizedPath).ToLowerInvariant()
        # These are production visual formats, including the themed Factory art
        # allowlist. A developer directory above still takes precedence.
        $factoryArtExtensions = @('.png', '.jpg', '.jpeg', '.webp', '.tga', '.psd')
        if ($factoryArtExtensions -contains $extension) { return $null }

        $name = [System.IO.Path]::GetFileNameWithoutExtension($normalizedPath)
        $toolingExtensions = @('.bytes', '.db', '.sqlite', '.cache', '.manifest', '.output', '.dat', '.csv', '.tsv', '.json', '.asset', '.prefab', '.txt', '.log', '.bin', '.xml', '.yaml', '.yml')
        if (($toolingExtensions -contains $extension) -and $name -match '(?i)(database|debug|pipeline)') {
            return 'Factory database, debug, or pipeline tooling family'
        }
    }

    return $null
}

function Add-PathFinding([string] $label, [string] $path, [switch] $SourceAsset) {
    $normalized = $path.Replace([string][char]92, '/')
    $name = [System.IO.Path]::GetFileName($normalized)
    $forbiddenSegment = -not $SourceAsset -and $normalized -match '(?i)(^|/)(Editor|Tests?|TestFixtures|Evidence)(/|$)'
    $forbiddenAssembly = -not $SourceAsset -and $name -match '(?i)(NUnit.*\.dll|Unity(?:Engine|Editor)\.TestRunner.*\.dll|[^/]*(?:Tests?|TestAssembly)[^/]*\.dll)$'
    $resourceCode = $normalized -match '(?i)(^|/)(Resources|StreamingAssets|Addressables)(/|$).+\.(cs|ps1|asmdef|rsp)$'
    $developerResource = Get-DeveloperResourceFinding $normalized
    $opaqueSourceBundle = $SourceAsset -and [System.IO.Path]::GetExtension($normalized) -match '(?i)^\.(bundle|assetbundle|unity3d)$'
    if ($forbiddenSegment -or $forbiddenAssembly -or $resourceCode -or $developerResource -or $opaqueSourceBundle) {
        $script:violations.Add("$label contains forbidden path: $normalized")
    }
}

function Test-PayloadBytes([string] $label, [byte[]] $bytes) {
    $script:scannedItems++

    # Unity engine modules carry a catalog of optional assembly names (including
    # TestRunner) even in a player that does not ship any test assemblies. Their
    # filenames are handled by path checks; marker scanning is for project and
    # payload files where embedded test references are meaningful.
    if ($label -match '(?i)(^|[/\\])UnityEngine\.[^/\\]+Module\.dll$') { return }

    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $marker = '(?i)IncludeTestAssemblies\s*[:=]\s*(?:true|1)|-runTests\b|-testPlatform\b|-testResults\b|Unity(?:Engine|Editor)\.TestRunner|NUnit\.Framework|PlayModeTestRunner'
    if ($ascii -match $marker) {
        $script:violations.Add("$label contains a test-runner or test-assembly marker")
    }
}

function Read-StreamBytes([System.IO.Stream] $stream, [long] $length, [string] $label) {
    if ($length -gt $maxInspectBytes) {
        $script:violations.Add("$label is too large for bounded content inspection ($length bytes)")
        return
    }

    $buffer = [System.IO.MemoryStream]::new()
    try {
        $stream.CopyTo($buffer)
        Test-PayloadBytes $label $buffer.ToArray()
    }
    finally {
        $buffer.Dispose()
    }
}

function Inspect-Zip([string] $zipPath, [string] $label) {
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    }
    catch {
        $script:violations.Add("$label is not a readable ZIP archive: $($_.Exception.Message)")
        return
    }

    try {
        foreach ($entry in $zip.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $entryLabel = "$label!/$($entry.FullName)"
            Add-PathFinding $entryLabel $entry.FullName
            $stream = $entry.Open()
            try {
                Read-StreamBytes $stream $entry.Length $entryLabel
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

function Inspect-File([string] $filePath, [string] $relativePath) {
    Add-PathFinding $filePath $relativePath
    $extension = [System.IO.Path]::GetExtension($filePath).ToLowerInvariant()
    if ($archiveExtensions -contains $extension) {
        Inspect-Zip $filePath $relativePath
        return
    }

    $info = [System.IO.FileInfo]::new($filePath)
    $stream = [System.IO.File]::OpenRead($filePath)
    try {
        Read-StreamBytes $stream $info.Length $relativePath
    }
    finally {
        $stream.Dispose()
    }
}

if ($PSCmdlet.ParameterSetName -eq 'SourceManifest') {
    $roots = [System.Collections.Generic.List[string]]::new()
    foreach ($rootPath in $SourceAssetRoot) {
        $resolvedRoot = (Resolve-Path -LiteralPath $rootPath).Path
        if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
            throw "Source asset root must be a directory: $rootPath"
        }
        $roots.Add($resolvedRoot)
    }
    if ($roots.Count -eq 0) { throw 'At least one source asset root is required.' }

    $manifest = [System.IO.Path]::GetFullPath($ManifestPath)
    if (Test-Path -LiteralPath $manifest) { throw "Refusing to overwrite an existing source manifest: $manifest" }
    $manifestParent = Split-Path -Parent $manifest
    if (-not (Test-Path -LiteralPath $manifestParent -PathType Container)) {
        throw "Source manifest parent directory does not exist: $manifestParent"
    }
    foreach ($root in $roots) {
        $rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        if ($manifest.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Source manifest must be outside the input asset roots: $manifest"
        }
    }

    $records = [System.Collections.Generic.List[string]]::new()
    foreach ($root in $roots) {
        $rootName = Split-Path -Leaf $root
        $files = @(Get-ChildItem -LiteralPath $root -Recurse -File)
        if ($files.Count -eq 0) { throw "Source asset root is empty; refusing an uninformative manifest: $root" }
        foreach ($file in $files) {
            $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace([string][char]92, '/')
            $assetPath = "$rootName/$relative"
            Add-PathFinding $assetPath $assetPath -SourceAsset
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $records.Add("$assetPath SHA256=$hash BYTES=$($file.Length)")
            $script:scannedItems++
        }
    }

    if ($script:violations.Count -gt 0) {
        $script:violations | ForEach-Object { Write-Output $_ }
        Write-Output "SOURCE_MANIFEST=FAIL ITEMS=$script:scannedItems VIOLATIONS=$($script:violations.Count)"
        exit 1
    }

    $header = @(
        'MATCH3_ENGINE Unity release source asset manifest',
        'All files under the declared source roots were SHA-256 inventoried before the player build.',
        'Opaque prebuilt bundles are not introspected; they must be built from these validated source roots or excluded.',
        "ROOTS=$($roots.Count) FILES=$script:scannedItems"
    )
    [System.IO.File]::WriteAllLines($manifest, [string[]]($header + @($records | Sort-Object -CaseSensitive)), [System.Text.UTF8Encoding]::new($false))
    Write-Output "SOURCE_MANIFEST=PASS ROOTS=$($roots.Count) FILES=$script:scannedItems VIOLATIONS=0 PATH=$manifest"
    exit 0
}

$artifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
if (Test-Path -LiteralPath $artifact -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $artifact -Recurse -File) {
        $relative = [System.IO.Path]::GetRelativePath($artifact, $file.FullName)
        Inspect-File $file.FullName $relative
    }
}
else {
    Inspect-File $artifact ([System.IO.Path]::GetFileName($artifact))
}

if ($script:violations.Count -gt 0) {
    $script:violations | ForEach-Object { Write-Output $_ }
    Write-Output "SCAN=FAIL ITEMS=$script:scannedItems VIOLATIONS=$($script:violations.Count)"
    exit 1
}

Write-Output "SCAN=PASS ITEMS=$script:scannedItems PATH=$artifact"
exit 0
