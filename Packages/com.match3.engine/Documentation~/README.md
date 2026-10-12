# Match3 Engine package

The package separates engine-independent runtime assemblies, Editor-only tooling, and Unity Test Framework assemblies. Runtime code must remain free of UnityEngine and UnityEditor references unless a future architecture decision changes that boundary.

## Tests

Run EditMode tests with `pwsh -NoProfile -File .\Tools\RunUnityEditModeTests.ps1` and PlayMode tests with `pwsh -NoProfile -File .\Tools\RunUnityEditModeTests.ps1 -TestPlatform PlayMode` from the project root. The script records Unity's XML results and editor log under the operating system temporary directory and fails when Unity returns an error, a test fails, or no test passes.

The project manifest lists this package as testable. Package tests remain in `Tests/Editor` and `Tests/Runtime`; they are not referenced by the runtime assembly. The Unity Test Framework dependency is the built-in Unity 6.6 package, version 1.8.0, and its package license is the Unity Companion License. It is test tooling and is not part of the runtime package's dependency graph.

See the project-level `Documentation~/UNITY_BOOTSTRAP.md` for the assembly graph, Unity version, licensing and validation commands.
