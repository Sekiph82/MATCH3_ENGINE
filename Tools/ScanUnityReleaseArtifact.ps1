[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ArtifactPath
)

$ErrorActionPreference = 'Stop'
$artifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
$archiveExtensions = @('.apk', '.aab', '.ipa', '.zip', '.unitypackage')
$maxInspectBytes = 268435456
$script:scannedItems = 0
$script:violations = [System.Collections.Generic.List[string]]::new()

function Add-PathFinding([string] $label, [string] $path) {
    $normalized = $path.Replace([string][char]92, '/')
    $name = [System.IO.Path]::GetFileName($normalized)
    $forbiddenSegment = $normalized -match '(?i)(^|/)(Editor|Tests?|TestFixtures|Evidence)(/|$)'
    $forbiddenAssembly = $name -match '(?i)(NUnit.*\.dll|Unity(?:Engine|Editor)\.TestRunner.*\.dll|[^/]*(?:Tests?|TestAssembly)[^/]*\.dll)$'
    $resourceCode = $normalized -match '(?i)(^|/)(Resources|StreamingAssets|Addressables)(/|$).+\.(cs|ps1|asmdef|rsp)$'
    if ($forbiddenSegment -or $forbiddenAssembly -or $resourceCode) {
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
