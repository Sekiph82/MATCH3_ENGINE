# ENG-M00 SDK dependency and Unity compatibility

Audit date: 2026-10-12. Target editor: **Unity 6000.6.5f1** (`3ff58d469c8a`). The audited SDK's Unity sample is configured for **2021.3.34f1** (`25266724e7bd`).

## Dependency decisions

| Component | Disposition | Version / target | Evidence and action |
| --- | --- | --- | --- |
| LibraStack Match3 SDK Core | REQUIRED | source commit `771e62908056c2c5fa69937a52ca52ab589f19ea`; .NET project `netstandard2.0`; Unity `Match3.Core` asmdef | MIT at the pinned upstream [LICENSE](https://github.com/LibraStack/Match3-SDK/blob/771e62908056c2c5fa69937a52ca52ab589f19ea/LICENSE). Consume its Unity package source, not compiled .NET 5 `App`/`Infrastructure` outputs. Retain the complete MIT notice. |
| LibraStack App and Infrastructure assemblies | REQUIRED as package source | same commit; `Match3.App` and `Match3.Infrastructure` asmdefs | The .NET projects target `net5.0`; Unity imports the C# source under asmdefs. These are not .NET 5 binaries. |
| Terminal sample hosting dependency | REMOVE from Unity game | NuGet `Microsoft.Extensions.Hosting` 5.0.0; terminal sample targets `net5.0` | Applies only to `samples/Terminal.Match3/Terminal.Match3.csproj`; not in the Unity package manifest. |
| UniTask | REQUIRED | **2.5.11**, Git commit `2e993ff18f28c931602a07292df0b0804eebef99`; package declares Unity `2018.4` minimum | MIT at the pinned [LICENSE](https://github.com/Cysharp/UniTask/blob/2e993ff18f28c931602a07292df0b0804eebef99/LICENSE); package `com.cysharp.unitask` has no package dependencies. The SDK's .NET App project references NuGet UniTask 2.3.1, while its Unity package declares UniTask with an unpinned Git URL. Unity uses the UPM package at this exact commit; do not import the .NET 5 project output. |
| DOTween | REMOVE from engine dependencies; sample-only optional | no engine pin | Not declared by the SDK Unity package and no `DG.Tweening` reference occurs in its 37 runtime C# files. Fourteen sample C# files reference `DG.Tweening`; the sample carries DOTween DLLs but does not declare it in its package manifest. Do not copy opaque binaries or sample media. |
| Upstream sample packages | OPTIONAL when a selected sample feature needs one | configured for Unity 2021.3.34f1 | The sample manifest includes editor integrations, Test Framework 1.1.33, TextMeshPro 3.0.6, Timeline 1.6.5, uGUI 1.0.0, Visual Scripting 1.9.1 and Unity modules. None is a dependency of the SDK package. |
| Unity built-in modules | OPTIONAL by feature | Unity-managed `1.0.0` modules | Keep only modules needed by the project and selected features. They are not third-party SDKs. |

The SDK package declares no dependency besides UniTask. Its Unity package source path is `src/Match3.UnityPackage/Assets/Plugins/Match3`; it contains `Runtime/Match3.Core`, `Runtime/Match3.App`, and `Runtime/Match3.Infrastructure` with one asmdef for each. The inspected .NET project paths are `src/Match3.Core/Match3.Core.csproj`, `src/Match3.App/Match3.App.csproj`, and `src/Match3.Infrastructure/Match3.Infrastructure.csproj`. The scanned Unity runtime source imports `Cysharp.Threading.Tasks`, SDK namespaces, and standard `System` namespaces. It has three asmdefs and the bounded scan found no UnityEditor, UnityEngine, .NET 5-only, or identified removed Unity API calls. No type-name collision was found within the SDK package's declared assemblies. Source provenance and license blob IDs are recorded in [THIRD_PARTY_PROVENANCE.md](THIRD_PARTY_PROVENANCE.md); full license texts are retained in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## 2021 sample and platform limits

The old sample manifest and bundled DOTween DLLs are sample state, not a Unity 6.6 baseline. The SDK runtime itself does not call Unity APIs; any Unity 6.6 API migration belongs in separately selected sample code or an adapter. No sample code or binary is imported by this task.

The installed editor has `WebGLSupport` and `windowsstandalonesupport`; Android and iOS playback-engine modules are absent. This checkout can validate editor package import plus Windows/WebGL availability only. Android/iOS compilation, Mono player execution, Apple signing, and Android IL2CPP output are **NOT TESTED** here. UniTask upstream IL2CPP tests do not establish this project's player compatibility.

If selected sample code fails to compile, record the compiler diagnostic and patch only the affected adapter or call site; preserve the upstream SDK algorithms. Before claiming Android/iOS support, install those Unity modules and run the target's actual IL2CPP build.

## Reproducible package resolution

`Tools/ResolveSdkDependencyCompatibility.ps1` checks the exact SDK and UniTask commits with the installed Unity editor in batch mode. By default it uses a temporary manifest and restores the original manifest and lock byte-for-byte. For the committed project manifest, add `-PersistResolvedLock` to resolve and retain `Packages/packages-lock.json`. Both modes retain their temporary backups and log for review.

```powershell
pwsh -NoProfile -File .\Tools\ResolveSdkDependencyCompatibility.ps1 -PersistResolvedLock
pwsh -NoProfile -File .\Tests\Policy\SdkDependencyPolicy.Tests.ps1
```

Resolution result: **PASS for Unity editor resolution/import only**. Editor process exit code: `0`; `AssetDatabase` reported script compilation time `0.641051s`; the log contains no `CS####` compiler errors. Package lock reports `source: git`, SDK package metadata `0.1.3` at commit `771e62908056c2c5fa69937a52ca52ab589f19ea`, and UniTask metadata `2.5.11` at commit `2e993ff18f28c931602a07292df0b0804eebef99`. The generated project's original manifest SHA-256 `4F4E356778889E91A5C767FE96DF647BE8B5FCEB06419B6121B0DE3672EDB505` and lock SHA-256 `C1BD4EFC832C4894DEE38BA7DF9D47CBC6B32EB9901198C015D20C3DCEAA4409` were restored byte-for-byte. This is not a Windows/WebGL player build. Android/iOS module installation and player builds remain NOT TESTED.
