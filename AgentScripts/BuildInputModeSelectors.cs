using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

public static class BuildInputModeSelectors
{
    public static string ImportSources()
    {
        foreach (string path in new[] { "Assets/Scripts/Session/GameSessionSettings.cs",
            "Assets/Scripts/UI/InputModeSelector.cs", "Assets/Scripts/UI/InputModeGuideUI.cs",
            "Assets/Scripts/UI/GameSetupUI.cs", "Assets/Scripts/UI/PreparationFlowController.cs",
            "Assets/Scripts/Input/Imu/ImuGameplayInput.cs", "Assets/Scripts/Tutorial/TutorialController.cs" })
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return "Imported the explicit IMU/Keyboard selection sources for UI wiring.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before adding input selectors.");
        Type selectorType = FindType("InputModeSelector"), guideType = FindType("InputModeGuideUI");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var difficulty = all.Single(t => t.name == "DifficultyPage");
        var mode = all.Single(t => t.name == "GameModePage");
        var guide = all.Single(t => t.name == "GuidancePage");
        var setup = all.SelectMany(t => t.GetComponents<Component>()).First(c => c != null && c.GetType().Name == "GameSetupUI");
        var reference = difficulty.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>();
        var imageStyle = reference.GetComponent<UnityEngine.UI.Image>();
        var textStyle = reference.GetComponentInChildren<TMP_Text>();
        if (difficulty.Find("InputModeButton") != null || mode.Find("InputModeButton") != null)
            throw new InvalidOperationException("Input selectors already exist; edit them in place.");
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("../.tmp/input-mode-before");
        File.Copy(scene.path, "../.tmp/input-mode-before/MainMenuScene.unity", true);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add IMU and Keyboard input selection");
        try
        {
            foreach (var page in new[] { difficulty, mode })
            {
                var rect = Rect(page, "InputModeButton", 1355, 32, 490, 64);
                var image = Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
                image.sprite = imageStyle.sprite;
                image.type = imageStyle.type;
                image.color = imageStyle.color;
                image.pixelsPerUnitMultiplier = imageStyle.pixelsPerUnitMultiplier;
                var button = Undo.AddComponent<UnityEngine.UI.Button>(rect.gameObject);
                button.targetGraphic = image;
                button.colors = reference.colors;
                button.transition = reference.transition;
                var label = Text(rect, "Label", "Input: IMU", 10, 4, 470, 56, 28, textStyle);
                var hint = Text(page, "InputModeDescription", "Continue opens sensor preparation. Click to switch to Keyboard.",
                    1260, 102, 590, 60, 21, textStyle);
                Component selector = Undo.AddComponent(rect.gameObject, selectorType);
                Set(selector, "buttonLabel", label);
                Set(selector, "description", hint);
                Bind(button, selector, "ToggleMode");
            }
            var banner = Rect(guide, "InputModeBanner", 70, 112, 1780, 52);
            var bannerImage = Undo.AddComponent<UnityEngine.UI.Image>(banner.gameObject);
            bannerImage.sprite = imageStyle.sprite;
            bannerImage.type = imageStyle.type;
            bannerImage.color = imageStyle.color;
            bannerImage.raycastTarget = false;
            var instructions = Text(banner, "ControlHint",
                "IMU: Roll your wrist left / right to steer and turn avatars. Tilt upward to jump. Forward movement is automatic.",
                14, 4, 1752, 44, 23, textStyle);
            Component guidePresenter = guide.GetComponent(guideType) ?? Undo.AddComponent(guide.gameObject, guideType);
            Set(guidePresenter, "controlHint", instructions);
            Set(setup, "guidancePage", guide.gameObject);
            Bind(difficulty.Find("DownPanel/ContinueButton").GetComponent<UnityEngine.UI.Button>(), setup, "ContinueFromDifficulty");
            Bind(guide.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>(), setup, "BackFromGuide");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved input-mode buttons on Difficulty/Game Mode pages, keyboard-direct Guidance routing and mode-specific guide hints.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null)
        ?? throw new InvalidOperationException("Awaiting source import: " + name);

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create input selection UI");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static TMP_Text Text(Transform parent, string name, string content, float x, float y, float w, float h, float size, TMP_Text template)
    {
        var label = Undo.AddComponent<TextMeshProUGUI>(Rect(parent, name, x, y, w, h).gameObject);
        label.font = template.font;
        label.fontSharedMaterial = template.fontSharedMaterial;
        label.color = template.color;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.text = content;
        return label;
    }

    private static void Set(Component owner, string name, UnityEngine.Object value)
    {
        Undo.RecordObject(owner, "Wire input selection UI");
        var data = new SerializedObject(owner);
        var property = data.FindProperty(name);
        if (property == null) throw new InvalidOperationException("Awaiting updated source field: " + name);
        property.objectReferenceValue = value;
        data.ApplyModifiedProperties();
    }

    private static void Bind(UnityEngine.UI.Button button, Component owner, string method)
    {
        Undo.RecordObject(button, "Wire input-specific navigation");
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), owner, method));
    }
}
