using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Coat;

/// Builds the MainMenu scene and the (empty) shop stock asset, and puts the
/// menu first in the build settings so a build opens on it.
///
/// Additive, and it never touches SampleScene: the menu is a separate scene
/// with two objects in it, so nothing about the game scene has to move.
public static class CoatMenuBuilder
{
    const string MenuScene = "Assets/Scenes/MainMenu.unity";
    const string GameScene = "Assets/Scenes/SampleScene.unity";
    const string StockPath = "Assets/CoatGame/Resources/CoatShopStock.asset";

    [MenuItem("Coat/Build Main Menu")]
    public static void FromMenu() => Debug.Log(Run());

    public static string Run()
    {
        var log = new System.Text.StringBuilder();

        log.AppendLine(Stock());
        log.AppendLine(Scene());
        log.AppendLine(BuildList());

        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static string Stock()
    {
        var existing = AssetDatabase.LoadAssetAtPath<CoatShopStock>(StockPath);
        if (existing != null)
            return $"stock: already there, {existing.Items.Count} item(s), left alone";

        Directory.CreateDirectory(Path.GetDirectoryName(StockPath));
        var stock = ScriptableObject.CreateInstance<CoatShopStock>();
        AssetDatabase.CreateAsset(stock, StockPath);
        return "stock: created empty at " + StockPath;
    }

    static string Scene()
    {
        if (File.Exists(MenuScene)) return "scene: already there, left alone";

        Directory.CreateDirectory(Path.GetDirectoryName(MenuScene));

        // Additive, so whatever the user has open and possibly unsaved stays
        // open and possibly unsaved.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = CoatPalette.UiBackground;
        camGo.AddComponent<AudioListener>();
        camGo.tag = "MainCamera";
        SceneManager.MoveGameObjectToScene(camGo, scene);

        var menuGo = new GameObject("CoatMenu");
        var menu = menuGo.AddComponent<CoatMenu>();
        menu.GameScene = "SampleScene";
        SceneManager.MoveGameObjectToScene(menuGo, scene);

        EditorSceneManager.SaveScene(scene, MenuScene);
        EditorSceneManager.CloseScene(scene, true);
        return "scene: created " + MenuScene;
    }

    static string BuildList()
    {
        var list = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MenuScene, true)
        };

        bool hadGame = false;
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == MenuScene) continue;
            if (s.path == GameScene) hadGame = true;
            list.Add(s);
        }
        if (!hadGame && File.Exists(GameScene))
            list.Add(new EditorBuildSettingsScene(GameScene, true));

        EditorBuildSettings.scenes = list.ToArray();

        var names = new List<string>();
        for (int i = 0; i < list.Count; i++)
            names.Add(i + " " + Path.GetFileNameWithoutExtension(list[i].path));
        return "build settings: " + string.Join(", ", names);
    }
}
