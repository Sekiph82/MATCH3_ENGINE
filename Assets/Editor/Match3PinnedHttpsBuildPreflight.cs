using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Demo;
using Match3.Engine.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Audit the ACTUAL scene the Unity player builder processes. This also
// protects custom BuildPlayerOptions.scenes arrays, not merely the default
// EditorBuildSettings list. No remote GET, private key or file mutation.
public sealed class Match3PinnedHttpsBuildPreflight : IProcessSceneWithReport
{
    public int callbackOrder => -280;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        // Development QA scenes are built with frozen test-only credentials
        // and a separate non-development QA fixture firewall.
        if ((report.summary.options & BuildOptions.Development) != 0)
            return;

        ValidateLoadedScene(scene, report.summary.platform,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [MenuItem("Match3 QA/Read-only Audit Enabled HTTPS Build Scenes")]
    public static void AuditDefaultScenes()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(item => item.enabled).Select(item => item.path);
        var count = ValidateScenes(scenes,
            EditorUserBuildSettings.activeBuildTarget,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Debug.Log("MATCH3_PINNED_HTTPS_BUILD_AUDIT=PASS" +
                  " VERIFIED_SCENE_BOOTSTRAPS=" + count +
                  " ACTUAL_NETWORK=NOT_RUN");
    }

    // Explicit, read-only owner preflight. It may inspect inactive components
    // in closed scenes, but never closes, saves or unloads an owner's scene.
    public static int ValidateScenes(IEnumerable<string> scenePaths,
        BuildTarget target, long nowUnixSeconds)
    {
        if (scenePaths == null)
            throw new ArgumentNullException(nameof(scenePaths));
        var paths = scenePaths.ToArray();
        if (paths.Any(string.IsNullOrWhiteSpace) ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new BuildFailedException("Invalid or repeated build scene path.");

        var accepted = 0;
        foreach (var path in paths)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var openedByAudit = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedByAudit)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                accepted += ValidateLoadedScene(scene, target, nowUnixSeconds);
            }
            catch (BuildFailedException) { throw; }
            catch (Exception)
            {
                throw new BuildFailedException(
                    "Signed HTTPS scene preflight rejected unsafe configuration.");
            }
            finally
            {
                // Only the scene opened by THIS audit may be closed, and it
                // must not be saved or replace the owner's active scene.
                if (openedByAudit && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
        return accepted;
    }

    private static int ValidateLoadedScene(Scene scene, BuildTarget target,
        long nowUnixSeconds)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new BuildFailedException("Unloaded Unity build scene.");
        var validated = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var player in root
                .GetComponentsInChildren<NeutralPinnedHttpsPlayerBootstrap>(true))
            {
                if (!player.SignedUpdatesEnabled)
                    continue; // Dormant stand-alone neutral engine is allowed.
                if (!player.enabled || !player.gameObject.activeInHierarchy)
                    throw new BuildFailedException(
                        "Signed HTTPS player requires an active behaviour.");
                var go = player.gameObject;
                if (go.GetComponent<NeutralMatch3Demo>() == null ||
                    go.GetComponent<NeutralPatchBootstrapView>() == null ||
                    go.GetComponent<NeutralPatchSessionHost>() == null)
                    throw new BuildFailedException(
                        "Signed HTTPS player lacks required scene siblings.");

                // Same production code checks the RSA public modulus,
                // date validity, title/channel and first-party HTTPS base.
                // Neither a valid config nor a build proves TLS connectivity.
                player.RequireValidPinnedConfiguration(
                    MapTarget(target), nowUnixSeconds);
                validated++;
            }
        }
        return validated;
    }

    private static RemotePlatform MapTarget(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.StandaloneWindows64:
                return RemotePlatform.Windows;
            case BuildTarget.Android:
                return RemotePlatform.Android;
            case BuildTarget.iOS:
                return RemotePlatform.Ios;
            default:
                throw new BuildFailedException(
                    "Signed HTTPS player target must be Windows, Android or iOS.");
        }
    }
}
