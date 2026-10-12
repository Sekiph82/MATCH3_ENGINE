# Unity batch validation and release content firewall

Run the local Windows validation adapter with PowerShell 7:

```powershell
pwsh -NoProfile -File Tools/RunUnityCI.ps1 -ProjectPath . -EditorPath 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe'
```

It executes EditMode and PlayMode through Unity batchmode, runs the release-scanner positive/negative fixtures, and propagates child nonzero exit codes. Each Unity run writes its NUnit XML and Editor log to a unique directory under the system temporary path. Any failed, skipped, inconclusive-only, or empty required suite fails closed.

To scan a release output directory or ZIP-based `.apk`, `.aab`, `.ipa`, `.zip`, or `.unitypackage`, pass `-ReleaseArtifactPath <path>`. The scanner checks entry paths, assembly names, scripts and fixtures in Resources/StreamingAssets/Addressables, and embedded test-runner/test-assembly markers. It does not extract archives. Payload entries over 256 MiB fail closed when content cannot be inspected.

Run `pwsh -NoProfile -File Tests/Policy/ReleaseArtifactScanner.Tests.ps1` for scanner fixtures. The positive fixture contains ordinary Resources/StreamingAssets data; negative fixtures prove rejection of a packaged NUnit assembly, script source in StreamingAssets, and the `IncludeTestAssemblies` flag.

The adapter is local and unhosted. It needs the pinned Unity 6000.6.5f1 Editor and an available Unity license. No paid hosted runner, CI account, or Unity license secret is configured here. Android/iOS package and device inspection remains pending platform modules and signing access; this scanner pass does not substitute for those checks.
