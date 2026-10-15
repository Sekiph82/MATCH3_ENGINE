using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Match3.Engine.Demo;
using Match3.Engine.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Match3SignedQaWin64Builder
{
    // This is deliberately an EDITOR-ONLY development player builder.
    // No private RSA key is stored here: these bytes were signed offline,
    // and the matching public QA key is pinned in NeutralSignedQaBootstrap
    // only under UNITY_EDITOR || DEVELOPMENT_BUILD.
    private const string QaRoot =
        "Assets/StreamingAssets/match3/Coral_Rescue/2/0";
    private const string QaScene =
        "Assets/Match3Demo/QaSignedScene/NeutralSignedQa.unity";
    private const string CanonicalB64 =
        "TUFUQ0gzLUNPTlRFTlQtVjEKMQpDb3JhbF9SZXNjdWUKMgowCnFhLXNpZ25lZC1uZXV0cmFsLXYxCjEKbTExLXFhLXB1YmxpYwoxLjAuMAoxLjEuMAoxNzkyMDIyNDAwCjE4MjM0NzIwMDAKMQpsZXZlbHMvbmV1dHJhbC5qc29uCjAKMzQKODAzYzBkYzRmZjA0OTgwMjRlODk5MjAwZjIwZGU4ZjI3ZDE5OTVhY2E1ZTMxYmM1NzRhMWIwZDkyNWEyYTcwZAowCg==";
    private const string SignatureB64 =
        "yPoKEmdX+NusZVarcphiA5zAIaCgshJet6z5stqol1+MyFBmX455XFgC0VyUiWrjTvLc1kefS9q23VrJ9n1uOZrjJH/eDZsghFIs0eove2nkwX4C9MU+2Ee6P83FQeP9Cthyt0HJB/JU3oslqGntsq1aDDnJQ+qzAEp4DcB0oNygNDVygOPj4AUiq/SCiDCWUGs8z9yiyHz2VeGGCpbBTdU/yfaXJZUTBsmYv6LGP+dPpOu/COjZzC1SrfkCEb9YfsK5NYPNRCsep6uzTI8uD+M8UfAdgKDeL5SIfVXeSc13bQx5sIz0eA4yGU4D3e4C98dKewzzf3L9FDyuP8jecA==";
    private const string LevelB64 =
        "eyJuZXV0cmFsIjoicWEtc3RhcnRlciIsImxldmVsIjoxfQ==";
    private const string ModulusB64 =
        "zoodN2wo20EQxljrIekNMICz96ZUP60ns3ShOygfHdBo4O5k7AcerQx8+Cf3UrCz52k+s+C9RuxlsyU9Y+AlXIJUYJ5CfI3IIpvb7LDHWzgUEu9CXIIrQ9ZX3y4/5D3GMF1CeHZOj9If2rOS41HpVXPf/OdtQxlccGdVdGi9LEw5p5yb39Zt2zA6OFlHzlk7fNB/w27bPVVCinYfk+kipmKHJ93G6dykWqcrh2X4dFarqpcZe3UCFaEAVhA06NiTFm79L1ESeDTJORE2pB7gmgFPY8p0vfexiZwvkYD9BgfGlWZYeA8k7TFxwog1MzLNwZbIEeR5rQ+G5mzkOtN4TQ==";

    [MenuItem("Match3 QA/Build Isolated Signed Neutral Win64 Player")]
    public static void BuildSignedQaPlayer()
    {
        if (EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException(
                "Save the owner's current scene before running QA; it will not be overwritten.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            throw new InvalidOperationException(
                "Switch to installed Windows x64 target first; no package or module installs.");

        // Refuse to mutate any existing owner fixture. Re-run is idempotent
        // only when every existing artifact has exactly the approved bytes.
        ProvisionFixture();
        ProvisionScene();

        var dir = Path.Combine(Path.GetTempPath(),
            "MATCH3_SIGNED_QA_" + DateTime.UtcNow.ToString(
                "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        if (Directory.Exists(dir))
            throw new IOException("QA evidence directory already exists.");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "Match3SignedNeutralQA.exe");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { QaScene },
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        if (report.summary.result != BuildResult.Succeeded ||
            !File.Exists(exe))
            throw new InvalidOperationException(
                "Development player failed; inspect Unity build log and retain QA evidence.");
        Debug.Log("MATCH3_SIGNED_QA_PLAYER=" + exe);
        Debug.Log("MATCH3_SIGNED_QA_SOURCE=Development-only signed starter; " +
                  "production HTTPS, Addressables and mobile NOT tested.");
    }

    private static void ProvisionFixture()
    {
        var manifest = Convert.FromBase64String(CanonicalB64);
        var signature = Convert.FromBase64String(SignatureB64);
        var level = Convert.FromBase64String(LevelB64);
        var pinned = new RemotePublicSigningKey("m11-qa-public",
            Convert.FromBase64String(ModulusB64),
            new byte[] { 0x01, 0x00, 0x01 },
            1792022400L, 1823472000L);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var parsed = RemoteManifestCodec.ParseVerified(
            manifest, signature, new[] { pinned }, "Coral_Rescue",
            RemotePlatform.Windows, RemoteChannel.Development,
            new EngineVersion(1, 0, 0), now);
        RemoteContentStager.VerifyComplete(parsed, signature,
            new[] { pinned }, parsed.GameId, parsed.Platform,
            parsed.Channel, new EngineVersion(1, 0, 0), now,
            new Dictionary<string, byte[]> {
                { "levels/neutral.json", level }
            });

        WriteNewOrVerify(QaRoot + "/manifest.canonical", manifest);
        WriteNewOrVerify(QaRoot + "/manifest.sig", signature);
        WriteNewOrVerify(QaRoot + "/levels/neutral.json", level);
        AssetDatabase.Refresh();
    }

    private static void WriteNewOrVerify(string path, byte[] expected)
    {
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).SequenceEqual(expected))
                throw new InvalidOperationException(
                    "Refusing to overwrite mismatched existing QA/owner file: " + path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (var stream = new FileStream(path, FileMode.CreateNew,
                   FileAccess.Write, FileShare.None))
        {
            stream.Write(expected, 0, expected.Length);
            stream.Flush(true);
        }
    }

    private static void ProvisionScene()
    {
        if (File.Exists(QaScene))
        {
            var existing = EditorSceneManager.OpenScene(
                QaScene, OpenSceneMode.Single);
            var demo = existing.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<NeutralMatch3Demo>())
                .SingleOrDefault();
            if (demo == null ||
                demo.GetComponent<NeutralSignedQaBootstrap>() == null ||
                demo.GetComponent<NeutralPatchBootstrapView>() == null)
                throw new InvalidOperationException(
                    "Existing QA scene is not the required signed neutral scene.");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(QaScene));
        var scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.GetComponent<Camera>().orthographic = true;
        var gameObject = new GameObject("Signed Neutral Windows QA");
        gameObject.AddComponent<NeutralMatch3Demo>();
        gameObject.AddComponent<NeutralPatchBootstrapView>();
        gameObject.AddComponent<NeutralSignedQaBootstrap>();
        if (!EditorSceneManager.SaveScene(scene, QaScene))
            throw new IOException("Failed to persist isolated signed QA scene.");
        AssetDatabase.Refresh();
    }
}

// If a developer provisions signed QA assets in StreamingAssets, NEVER
// silently include those development fixtures in a non-development build.
// A real shipping build must remove them in an explicit owner-approved flow.
public sealed class Match3SignedQaShippingGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => -300;

    public void OnPreprocessBuild(BuildReport report)
    {
        if ((report.summary.options & BuildOptions.Development) != 0)
            return;
        var qaMarker =
            "Assets/StreamingAssets/match3/Coral_Rescue/2/0/manifest.canonical";
        if (File.Exists(qaMarker))
            throw new BuildFailedException(
                "Non-development build blocked: isolated signed QA fixture " +
                "would otherwise be included in StreamingAssets. Preserve " +
                "the files until the owner approves an explicit exclusion.");
    }
}
