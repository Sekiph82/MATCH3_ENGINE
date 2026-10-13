# Unity batch validation and release content firewall

Run the local Windows validation adapter with PowerShell 7:

```powershell
pwsh -NoProfile -File Tools/RunUnityCI.ps1 -ProjectPath . -EditorPath 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe'
```

It executes EditMode and PlayMode through Unity batchmode, runs the release-scanner, SDK-pin, Unity-bootstrap, and repository-policy positive/negative fixtures, validates the pinned provenance records, and propagates child nonzero exit codes. Each Unity run writes its NUnit XML and Editor log to a unique directory under the system temporary path. Any failed, skipped, inconclusive-only, or empty required suite fails closed. `-EvidenceDirectory <path>` places raw logs/XML at a chosen evidence root.

To scan a release output directory or ZIP-based `.apk`, `.aab`, `.ipa`, `.zip`, or `.unitypackage`, pass `-ReleaseArtifactPath <path>`. The scanner checks entry paths, assembly names, scripts and fixtures in Resources/StreamingAssets/Addressables, and embedded test-runner/test-assembly markers. It does not extract archives. Payload entries over 256 MiB fail closed when content cannot be inspected.

Run `pwsh -NoProfile -File Tests/Policy/ReleaseArtifactScanner.Tests.ps1` for scanner fixtures. The positive fixture contains ordinary Resources/StreamingAssets data, including themed Factory artwork. Negative fixtures prove rejection of a packaged NUnit assembly, script source in StreamingAssets, the `IncludeTestAssemblies` flag, and developer-only `StreamingAssets/Diagnostics/evidence.json`, `StreamingAssets/Factory/FactoryDatabase.bytes`, and `Addressables/Development/DebugOverlay.prefab` resources by path even when their payload bytes are innocuous. The Factory rule is filename-specific so themed game assets remain accepted.

## ENG-M00 R01 independent proof route

`.github/workflows/eng-m00-independent-runtime.yml` is manual-dispatch only. It is pinned to immutable checkout/upload action SHAs and the dispatch commit SHA. It requires a clean external runner labeled `independent-unity-6000.6.5f1` and `windows-build-support`, with Unity 6000.6.5f1, its existing license, and Windows player support already installed. The runner must be separate from the owner workstation and independently controlled. Configure the `m00-independent-runtime` GitHub Environment with a required reviewer before dispatch. The workflow does not download or activate Unity, install player modules, or use a GitHub-hosted runner.

When explicitly dispatched on that prepared runner, it exercises the intentional failing assertion and verifies the one-failed/zero-skipped/nonzero-exit result; runs the complete EditMode, PlayMode, and repository policy suites; imports the engine plus exact SDK/UniTask pins in a clean blank project; opens the immutable upstream sample and sends a real pointer-down/drag pair until an accepted match changes the board; and builds/scans the Windows player. Raw Editor logs, NUnit XML, resolved lockfile, player scan, run metadata, and SHA-256 manifest are uploaded as a workflow artifact. The action is not dispatched by this repository change, and no CI execution is claimed until its run URL and uploaded raw result are available.

The local adapter remains builder-side evidence. Android/iOS package and device inspection remains pending platform modules and signing access; this scanner pass does not substitute for those checks. No hosted compute, Unity activation, or module installation was authorized or started for this R01 source change.
