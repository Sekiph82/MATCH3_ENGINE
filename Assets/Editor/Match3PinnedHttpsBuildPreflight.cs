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

// No server connection, unsigned manifest read, source-file rewrite or
// generated build data is needed to validate a shipping scene's HTTPS
// trust configuration. This guard does not claim that an endpoint is live.
public sealed class Match3PinnedHttpsBuildPreflight : IPreprocessBuildWithReport
{
    public int callbackOrder => -280;

    public void OnPreprocessBuild(BuildReport report)
    {
        if ((report.summary.options & BuildOptions.Development) != 0)
            return; // Development-only, pre-signed QA fixtures are separate.

        // Unity's default build scene list. Any custom BuildPlayerOptions
        // caller must ALSO invoke ValidateScenes on its explicit scene list.
        var scenes = EditorBuildSettings.scenes
            .Where(item => item.enabled).Select(item => item.path);
        ValidateScenes(scenes, report.summary.platform,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    [MenuItem("Match3 QA/Read-only Audit Enabled HTTPS Build Scenes")]
    public static void AuditDefaultScenes()
    {
        var activeTarget = EditorUserBuildSettings.activeBuildTarget;
        var scenes = EditorBuildSettings.scenes
            .Where(item => item.enabled).Select(item => item.path);
        var found = ValidateScenes(scenes, activeTarget,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Debug.Log("MATCH3_PINNED_HTTPS_BUILD_AUDIT=PASS" +
                  " VERIFIED_SCENE_BOOTSTRAPS=" + found +
                  " ACTUAL_NETWORK=NOT_RUN");
    }

    // The caller passes exactly the scene paths used by its own player
    // builder. Scenes loaded additively for review are closed without saving.
    // Already-open scenes are NEVER closed, saved, unloaded or overwritten.
    public static int ValidateScenes(IEnumerable<string> scenePaths,
        BuildTarget target, long nowUnixSeconds)
    {
        if (scenePaths == null)
            throw new ArgumentNullException(nameof(scenePaths));

        var paths = scenePaths.ToArray();
        if (paths.Any(string.IsNullOrWhiteSpace))
            throw new BuildFailedException("Invalid Unity build scene path.");
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
            throw new BuildFailedException("Repeated Unity build scene path.");

        var accepted = 0;
        foreach (var path in paths)
        {
            var scene = SceneManager.GetSceneByPath(path);
            var openedByAudit = !scene.IsValid() || !scene.isLoaded;
            try
            {
                if (openedByAudit)
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new BuildFailedException(
                        "Build scene could not be safely inspected.");
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var player in root
                        .GetComponentsInChildren<NeutralPinnedHttpsPlayerBootstrap>(true))
                    {
                        if (!player.SignedUpdatesEnabled) continue;
                        if (!player.enabled || !player.gameObject.activeInHierarchy)
                            throw new BuildFailedException(
                                "Signed HTTPS player must run in an active build scene.");
                        var parent = player.gameObject;
                        if (parent.GetComponent<NeutralMatch3Demo>() == null ||
                            parent.GetComponent<NeutralPatchBootstrapView>() == null ||
                            parent.GetComponent<NeutralPatchSessionHost>() == null)
                            throw new BuildFailedException(
                                "Signed HTTPS player lacks required safe sibling components.");

                        // No network I/O or filesystem changes. Public-key
                        // import, host syntax, trusted dates, title/channel
                        // and build-target constraints are actually checked.
                        player.RequireValidPinnedConfiguration(
                            MapTarget(target), nowUnixSeconds);
                        accepted++;
                    }
                }
            }
            catch (BuildFailedException) { throw; }
            catch (Exception ex)
            {
                throw new BuildFailedException(
                    "Signed HTTPS scene preflight rejected unsafe configuration.",
                    ex);
            }
            finally
            {
                if (openedByAudit && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
        return accepted;
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
                    "Signed HTTPS player requires a supported Android, iOS or Windows target.");
        }
    }
}
