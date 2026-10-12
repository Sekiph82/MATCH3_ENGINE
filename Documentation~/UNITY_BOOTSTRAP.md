# Unity 6.6 bootstrap

## Editor and project

- Unity Editor version: `6000.6.5f1` (revision `3ff58d469c8a`). The version is pinned in `ProjectSettings/ProjectVersion.txt`.
- The repository root is the Unity project. UPM package `com.match3.engine` is embedded at `Packages/com.match3.engine`.
- Unity-generated `Library`, `Temp`, `Obj`, `Logs`, build output and user-specific IDE settings are ignored by `.gitignore`.
- Keep Unity's generated `.meta` files with package folders and source files so references have stable GUIDs.

## Assembly boundaries

| Assembly | Location | References | Platform |
| --- | --- | --- | --- |
| `Match3.Engine.Runtime` | `Packages/com.match3.engine/Runtime` | No Unity engine assemblies | All |
| `Match3.Engine.Editor` | `Packages/com.match3.engine/Editor` | Runtime | Editor only |
| `Match3.Engine.Runtime.Tests` | `Packages/com.match3.engine/Tests/Runtime` | Runtime and Test Framework | All test platforms; not auto-referenced |
| `Match3.Engine.Editor.Tests` | `Packages/com.match3.engine/Tests/Editor` | Runtime, Editor and Test Framework | Editor only; not auto-referenced |

The `com.match3.engine` package is listed in the project manifest's `testables` array. Test framework references are optional `TestAssemblies` references, and test assemblies are excluded from normal player compilation.

## Dependencies and licensing

The embedded engine package declares the built-in `com.unity.test-framework` 1.8.0 package for its tests. Unity's installed package includes its `LICENSE.md` under the Unity Package Cache; it identifies the Unity Companion License. The framework is not copied into the embedded package, and its assemblies are referenced only by the test assemblies.

The pinned LibraStack Match3-SDK and UniTask versions and full MIT notices are listed in `THIRD_PARTY_NOTICES.md`. Their source revisions and file-level reuse decisions are recorded in `THIRD_PARTY_PROVENANCE.md`.

## Validation

From the repository root, run:

```powershell
pwsh -NoProfile -File .\Tools\ValidateUnityBootstrap.ps1
pwsh -NoProfile -File .\Tests\Policy\UnityBootstrap.Tests.ps1
pwsh -NoProfile -File .\Tools\RunUnityEditModeTests.ps1
pwsh -NoProfile -File .\Tools\RunUnityEditModeTests.ps1 -TestPlatform PlayMode
```

The batch-mode runner writes an NUnit XML result file and Unity editor log to the operating system's temporary directory. The ENG-M00-002 evidence log records the exact commands, exits, test counts and SHA-256 hashes for the captured runs.

The local Unity installation resolved an assigned Unity Personal named-user entitlement. This records the local editor environment used for the import/tests; it does not claim a CI license setup, Android/iOS player build, or distribution entitlement review.
