using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class UnifyButtonImages
{
    private const string MenuPath = "Assets/Scenes/MainMenuScene.unity";
    private const string StylePath = "Assets/Resources/UI/MainMenuButtonStyle.asset";
    public static string ImportSources()
    {
        AssetDatabase.ImportAsset("Assets/Scripts/UI/UIButtonStyle.cs");
        AssetDatabase.ImportAsset("Assets/Scripts/UI/PainStressCheckUI.cs");
        AssetDatabase.ImportAsset("Assets/Scripts/Tutorial/TutorialView.cs");
        return "Imported the shared button style and runtime button integration sources for asset authoring.";
    }
    private static Scene Menu(out bool opened)
    {
        var scene = SceneManager.GetSceneByPath(MenuPath);
        opened = !scene.IsValid() || !scene.isLoaded;
        return opened ? EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Additive) : scene;
    }
    private static UnityEngine.UI.Button Reference(Scene scene)
    {
        var canvas = scene.GetRootGameObjects().Single(r => r.name == "Canvas").transform;
        return canvas.Find("MainMenuPage/ButtonPanel/StartButton").GetComponent<UnityEngine.UI.Button>();
    }
    private static void SetField(UnityEngine.Object target, string field, object value)
        => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Backup(string path)
    {
        string destination = "../.tmp/common-button-style-before/" + path;
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination);
    }
    public static string Inspect()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring UI.");
        bool opened; var menu = Menu(out opened);
        try
        {
            var button = Reference(menu); var image = button.GetComponent<UnityEngine.UI.Image>();
            var label = button.GetComponentInChildren<TMP_Text>(true);
            return "Source: " + button.name + "; sprite=" + AssetDatabase.GetAssetPath(image.sprite) +
                "; imageColour=" + image.color + "; textColour=" + label.color + "; type=" + image.type +
                "; pixelsPerUnit=" + image.pixelsPerUnitMultiplier;
        }
        finally { if (opened) EditorSceneManager.CloseScene(menu, true); }
    }
    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring UI.");
        var styleType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("EndlessRollball.UI.UIButtonStyle")).FirstOrDefault(t => t != null);
        if (styleType == null) throw new InvalidOperationException("Shared button style source is awaiting import.");
        bool menuOpened; var menu = Menu(out menuOpened);
        var originalActive = SceneManager.GetActiveScene();
        int total = 0;
        var report = new StringBuilder();
        try
        {
            var source = Reference(menu); var sourceImage = source.GetComponent<UnityEngine.UI.Image>();
            var sourceLabel = source.GetComponentInChildren<TMP_Text>(true);
            if (sourceImage.sprite == null) throw new InvalidOperationException("The Main Menu source button has no sprite.");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
            var style = AssetDatabase.LoadAssetAtPath<ScriptableObject>(StylePath);
            if (style == null) { style = ScriptableObject.CreateInstance(styleType); AssetDatabase.CreateAsset(style, StylePath); }
            else Undo.RecordObject(style, "Use Main Menu button styling everywhere");
            SetField(style, "background", sourceImage.sprite);
            SetField(style, "imageType", sourceImage.type);
            SetField(style, "backgroundColour", sourceImage.color);
            SetField(style, "labelColour", sourceLabel.color);
            SetField(style, "colours", source.colors);
            SetField(style, "material", sourceImage.material == UnityEngine.UI.Graphic.defaultGraphicMaterial ? null : sourceImage.material);
            SetField(style, "pixelsPerUnitMultiplier", sourceImage.pixelsPerUnitMultiplier);
            SetField(style, "fillCenter", sourceImage.fillCenter);
            EditorUtility.SetDirty(style); AssetDatabase.SaveAssetIfDirty(style);
            var apply = styleType.GetMethod("Apply", BindingFlags.Instance | BindingFlags.Public);

            // Author reusable project UI prefabs, including the tutorial's instantiated pause UI.
            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/EndlessRollballAssets/UI Asset" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
            foreach (var path in prefabPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null || asset.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length == 0) continue;
                Backup(path);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var buttons = root.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                    foreach (var button in buttons) apply.Invoke(style, new object[] { button });
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    report.AppendLine(path + ": " + buttons.Length + " buttons"); total += buttons.Length;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            // The project scene folder includes the main menu, gameplay and tutorial interfaces.
            var scenePaths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
            foreach (var path in scenePaths)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    var buttons = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<UnityEngine.UI.Button>(true)).ToArray();
                    if (buttons.Length == 0) continue;
                    if (scene.isDirty) EditorSceneManager.SaveScene(scene);
                    Backup(path);
                    foreach (var button in buttons)
                    {
                        Undo.RegisterFullObjectHierarchyUndo(button.gameObject, "Use shared Main Menu button image");
                        apply.Invoke(style, new object[] { button });
                        if (PrefabUtility.IsPartOfPrefabInstance(button))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(button);
                            foreach (var graphic in button.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                                PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
                        }
                    }
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                    report.AppendLine(path + ": " + buttons.Length + " buttons"); total += buttons.Length;
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            return "Saved " + total + " authored buttons with Main Menu artwork and contrasting labels.\n" + report +
                "Runtime tutorial buttons use the same Resources style. Existing sizes, events and interactability preserved. No Play Mode or tests run.";
        }
        finally
        {
            if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            if (menuOpened && menu.IsValid() && menu.isLoaded) EditorSceneManager.CloseScene(menu, true);
        }
    }
}
