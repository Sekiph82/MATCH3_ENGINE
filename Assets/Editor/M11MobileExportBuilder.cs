using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEngine;

namespace Match3.Engine.M11.Editor
{
    public static class M11MobileExportBuilder
    {
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static void BuildAndroidGradleExport()
        {
            RequireTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            // Unity 6000.6 marks X86_64 obsolete and removes it from the
            // generated Android Gradle export. Keep the supported ARM64
            // phone target explicit; hosted tests must provide ARM64 runtime.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            BuildAddressables("Android");
            BuildPlayer(BuildTarget.Android,
                Path.Combine(ProjectRoot, "android-v12-arm64"),
                BuildOptions.Development | BuildOptions.AcceptExternalModificationsToPlayer,
                "Android-V12-ARM64");
        }

        public static void BuildIosSimulatorXcodeExport()
        {
            RequireTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.iOS.simulatorSdkArchitecture =
                AppleMobileArchitectureSimulator.ARM64;
            BuildAddressables("iOS");
            BuildPlayer(BuildTarget.iOS,
                Path.Combine(ProjectRoot, "ios-v12"),
                BuildOptions.Development,
                "iOS Simulator");
        }

        // Produces the single ARM64 APK consumed by the Firebase Test Lab
        // Game Loop harness. Reuses the already-built signed Android fixture
        // and never overwrites an existing artifact.
        public static void BuildFirebaseArm64PlayerApk()
        {
            RequireTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            var fixture = Path.Combine(ProjectRoot, "Assets", "StreamingAssets",
                "m11-addressables", "Android");
            var starter = Path.Combine(ProjectRoot, "Assets", "StreamingAssets",
                "match3", "Coral_Rescue", "1", "0");
            if (!File.Exists(Path.Combine(fixture, "files.list")) ||
                !Directory.Exists(starter))
                throw new InvalidOperationException(
                    "Signed Android starter or native Addressables fixture is missing.");
            var output = Path.Combine(ProjectRoot, "M11Evidence",
                "m11-firebase-arm64-player-themefix.apk");
            if (File.Exists(output) || Directory.Exists(output))
                throw new InvalidOperationException(
                    "Refusing to overwrite the prepared Firebase Player APK: " + output);
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                .Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled QA build scenes are configured.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });
            var summary = report.summary;
            Debug.Log("M11_FIREBASE_ARM64_APK_BUILD result=" + summary.result +
                " errors=" + summary.totalErrors + " warnings=" + summary.totalWarnings +
                " bytes=" + summary.totalSize + " output=" + output);
            if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0)
                throw new InvalidOperationException(
                    "Firebase ARM64 Player APK build failed: " + summary.result);
        }

        private static void RequireTarget(BuildTargetGroup group, BuildTarget target)
        {
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new InvalidOperationException("Unity BuildPipeline target is unsupported: " + target);
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new InvalidOperationException("Could not activate Unity build target: " + target);
            Debug.Log("M11_BUILD_TARGET_SUPPORTED=PASS target=" + target +
                " active=" + EditorUserBuildSettings.activeBuildTarget);
        }

        private static void BuildAddressables(string platform)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("Official Addressables settings are missing.");
            if (settings.activeProfileId == null)
                throw new InvalidOperationException("Addressables active profile is missing.");
            settings.EnableJsonCatalog = true;
            settings.BuildRemoteCatalog = true;
            var namedTarget = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                ? NamedBuildTarget.Android : NamedBuildTarget.iOS;
            PlayerSettings.GetScriptingDefineSymbols(namedTarget, out var symbols);
            Debug.Log("M11_ADDRESSABLES_JSON_CONFIG target=" + namedTarget +
                " setting=" + settings.EnableJsonCatalog +
                " targetDefine=" + string.Join(";", symbols) +
                " editorActiveDefines=" + string.Join(";", EditorUserBuildSettings.activeScriptCompilationDefines));
            if (!symbols.Contains("ENABLE_JSON_CATALOG"))
                PlayerSettings.SetScriptingDefineSymbols(namedTarget,
                    symbols.Concat(new[] { "ENABLE_JSON_CATALOG" }).ToArray());
            settings.profileSettings.SetValue(settings.activeProfileId,
                AddressableAssetSettings.kRemoteBuildPath,
                "ServerData/[BuildTarget]");
            settings.profileSettings.SetValue(settings.activeProfileId,
                AddressableAssetSettings.kRemoteLoadPath,
                "https://signed.invalid/m11/[BuildTarget]");
            settings.RemoteCatalogBuildPath.SetVariableByName(settings,
                AddressableAssetSettings.kRemoteBuildPath);
            settings.RemoteCatalogLoadPath.SetVariableByName(settings,
                AddressableAssetSettings.kRemoteLoadPath);
            foreach (var group in settings.groups.Where(group =>
                group != null && group.name == "M11_Official_Neutral_Native_QA"))
            {
                var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
                if (schema == null)
                    throw new InvalidOperationException("Neutral M11 Addressables group schema is missing.");
                schema.BuildPath.SetVariableByName(settings,
                    AddressableAssetSettings.kRemoteBuildPath);
                schema.LoadPath.SetVariableByName(settings,
                    AddressableAssetSettings.kRemoteLoadPath);
                EditorUtility.SetDirty(schema);
            }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (result == null || !string.IsNullOrEmpty(result.Error))
                throw new InvalidOperationException("Official " + platform +
                    " Addressables build failed: " + (result == null ? "<null>" : result.Error));

            var root = Path.Combine(ProjectRoot, "ServerData", platform);
            if (!Directory.Exists(root))
                throw new InvalidOperationException("Official Addressables output folder missing: " + root);
            var catalogs = Directory.GetFiles(root, "catalog*.json", SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path).StartsWith("{\"m_LocatorId\"", StringComparison.Ordinal))
                .ToArray();
            var bundles = Directory.GetFiles(root, "*.bundle", SearchOption.AllDirectories);
            if (catalogs.Length == 0 || bundles.Length == 0)
                throw new InvalidOperationException("Official " + platform +
                    " Addressables output must contain a JSON catalog and native bundles.");
            StageMobilePlayerFixture(platform, catalogs.OrderByDescending(
                File.GetLastWriteTimeUtc).First(), bundles);
            Debug.Log("M11_ADDRESSABLES_NATIVE_BUILD=PASS target=" + platform +
                " catalogs=" + catalogs.Length + " bundles=" + bundles.Length +
                " bytes=" + (catalogs.Sum(FileSize) + bundles.Sum(FileSize)));
        }

        private static void StageMobilePlayerFixture(string platform,
            string catalog, string[] bundles)
        {
            var fixture = Path.Combine(ProjectRoot, "Assets", "StreamingAssets",
                "m11-addressables", platform);
            if (Directory.Exists(fixture) || File.Exists(fixture))
                throw new InvalidOperationException(
                    "Refusing to overwrite an existing mobile Addressables fixture: " + fixture);
            Directory.CreateDirectory(Path.Combine(fixture, "bundles"));
            File.Copy(catalog, Path.Combine(fixture, "catalog.json"));
            var names = new System.Collections.Generic.List<string> { "catalog.json" };
            foreach (var bundle in bundles.OrderBy(Path.GetFileName,
                StringComparer.Ordinal))
            {
                var leaf = Path.GetFileName(bundle);
                if (string.IsNullOrWhiteSpace(leaf) || leaf.Contains("/") || leaf.Contains("\\") ||
                    names.Contains("bundles/" + leaf, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        "Ambiguous bundle filename in native Addressables output.");
                var relative = "bundles/" + leaf;
                File.Copy(bundle, Path.Combine(fixture, "bundles", leaf));
                names.Add(relative);
            }
            if (names.Count < 2)
                throw new InvalidOperationException("No platform-native Addressables bundles were staged.");
            File.WriteAllLines(Path.Combine(fixture, "files.list"), names,
                new UTF8Encoding(false));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("M11_ADDRESSABLES_PLAYER_FIXTURE=PASS target=" + platform +
                " files=" + names.Count + " bytes=" +
                (FileSize(Path.Combine(fixture, "catalog.json")) +
                 Directory.GetFiles(Path.Combine(fixture, "bundles"), "*",
                     SearchOption.TopDirectoryOnly).Sum(FileSize)));
        }

        private static long FileSize(string path) => new FileInfo(path).Length;

        private static void BuildPlayer(BuildTarget target, string output,
            BuildOptions options, string label)
        {
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidOperationException("Refusing to overwrite existing export: " + output);
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                .Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled QA build scenes are configured.");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = options
            });
            var summary = report.summary;
            var reportPath = Path.Combine(ProjectRoot, "M11Evidence",
                label.Replace(' ', '-') + "-Unity-BuildReport.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            var text = new StringBuilder()
                .AppendLine("target=" + summary.platform)
                .AppendLine("result=" + summary.result)
                .AppendLine("totalErrors=" + summary.totalErrors)
                .AppendLine("totalWarnings=" + summary.totalWarnings)
                .AppendLine("totalTime=" + summary.totalTime)
                .AppendLine("totalSize=" + summary.totalSize)
                .AppendLine("output=" + output)
                .AppendLine("activeTarget=" + EditorUserBuildSettings.activeBuildTarget)
                .ToString();
            File.WriteAllText(reportPath, text, new UTF8Encoding(false));
            Debug.Log("M11_UNITY_BUILD_REPORT=" + reportPath + "\n" + text);
            if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0)
                throw new InvalidOperationException(label + " Unity BuildPipeline failed: " + summary.result);
        }
    }
}
