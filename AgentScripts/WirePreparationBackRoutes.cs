using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WirePreparationBackRoutes
{
    public static string ImportSources()
    {
        AssetDatabase.ImportAsset("Assets/Scripts/UI/PreparationFlowController.cs", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Scripts/UI/GameSetupUI.cs", ImportAssetOptions.ForceUpdate);
        return "Imported corrected preparation Back routing for scene reference wiring.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode to wire navigation.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var flow = all.SelectMany(t => t.GetComponents<Component>()).First(c => c != null && c.GetType().Name == "PreparationFlowController");
        var data = new SerializedObject(flow);
        if (data.FindProperty("gameModePage") == null) throw new InvalidOperationException("The updated navigation source is awaiting import.");
        var mode = all.Single(t => t.name == "GameModePage").gameObject;
        var difficulty = all.Single(t => t.name == "DifficultyPage").gameObject;
        var main = all.Single(t => t.name == "MainMenuPage");
        var start = main.Find("ButtonPanel/StartButton").GetComponent<UnityEngine.UI.Button>();
        GameObject title = null;
        for (int i = 0; i < start.onClick.GetPersistentEventCount(); i++)
        {
            var target = start.onClick.GetPersistentTarget(i) as GameObject;
            if (target != null && target.name == "PageTitle") title = target;
        }
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("../.tmp/preparation-back-before");
        File.Copy(scene.path, "../.tmp/preparation-back-before/MainMenuScene.unity", true);
        Undo.RecordObject(flow, "Return from connection to the correct setup page");
        data.FindProperty("gameModePage").objectReferenceValue = mode;
        data.FindProperty("difficultyPage").objectReferenceValue = difficulty;
        data.FindProperty("mainMenuTitle").objectReferenceValue = title;
        data.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Saved setup page references. Connection Back now returns to Difficulty or Game Mode; Rest remains separate. Main-menu title visibility follows MainMenuPage.";
    }
}
