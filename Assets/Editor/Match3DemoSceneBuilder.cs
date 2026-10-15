using System.IO;
using Match3.Engine.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Match3DemoSceneBuilder
{
    public static void CreateScene()
    {
        const string scenePath = "Assets/Match3Demo/Scenes/NeutralMatch3Demo.unity";
        if (File.Exists(scenePath))
        {
            throw new IOException("Refusing to overwrite existing scene: " + scenePath);
        }

        Directory.CreateDirectory("Assets/Match3Demo/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera", typeof(Camera));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.GetComponent<Camera>().orthographic = true;

        var demoObject = new GameObject("Neutral Match-3 Demo");
        demoObject.AddComponent<NeutralMatch3Demo>();
        // Dormant until an authenticated prelaunch session is bound;
        // preserve the existing playable demo and never invent a patch.
        demoObject.AddComponent<NeutralPatchBootstrapView>();

        if (!EditorSceneManager.SaveScene(scene, scenePath))
        {
            throw new IOException("Could not save scene: " + scenePath);
        }

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("Created neutral Match-3 scene at " + scenePath);
    }
}
