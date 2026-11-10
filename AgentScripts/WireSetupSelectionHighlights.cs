using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WireSetupSelectionHighlights
{
    public static string ImportSource()
    {
        AssetDatabase.ImportAsset("Assets/Scripts/UI/GameSetupUI.cs");
        return "Imported GameSetupUI's option references for scene wiring.";
    }
    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode.");
        var setup = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
            .Single(c => c != null && c.GetType().Name == "GameSetupUI");
        var data = new SerializedObject(setup);
        if (data.FindProperty("standardModeButton") == null)
            throw new InvalidOperationException("Updated source is awaiting import.");
        var modePage = (GameObject)data.FindProperty("gameModePage").objectReferenceValue;
        var difficultyPage = (GameObject)data.FindProperty("difficultyPage").objectReferenceValue;
        string[] fields = { "standardModeButton", "fastPacedModeButton", "endlessModeButton", "easyDifficultyButton", "normalDifficultyButton", "hardDifficultyButton" };
        string[] methods = { "SelectStandard", "SelectFastPaced", "SelectEndless", "SelectEasy", "SelectNormal", "SelectHard" };
        var buttons = new UnityEngine.UI.Button[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            var page = i < 3 ? modePage : difficultyPage;
            string method = methods[i];
            buttons[i] = page.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b =>
                Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(e =>
                    b.onClick.GetPersistentTarget(e) == setup && b.onClick.GetPersistentMethodName(e) == method));
        }
        Undo.RecordObject(setup, "Keep selected game mode and difficulty highlighted");
        for (int i = 0; i < fields.Length; i++) data.FindProperty(fields[i]).objectReferenceValue = buttons[i];
        data.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return "Saved all six option-button references. Selected mode and difficulty retain their existing hover colour; choosing another option restores the previous option. Existing events, navigation and button artwork preserved. No Play Mode run.";
    }
}
