using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using EndlessRollball.Imu;
using EndlessRollball.Track;
using EndlessRollball.Tutorial;

if (Application.isPlaying) throw new InvalidOperationException("Leave Play Mode before authoring gameplay input.");
var original = SceneManager.GetActiveScene();
var report = new System.Collections.Generic.List<string>();
foreach (string scenePath in new[] { "Assets/Scenes/GameplayScene.unity", "Assets/Scenes/TutorialScene.unity" })
{
    var scene = SceneManager.GetSceneByPath(scenePath);
    bool opened = !scene.IsValid() || !scene.isLoaded;
    if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
    SceneManager.SetActiveScene(scene);
    var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
    var balls = transforms.Select(t => t.GetComponent<BallMovement>()).Where(b => b != null).ToArray();
    if (balls.Length != 1) throw new InvalidOperationException("Expected exactly one player ball in " + scenePath);
    var ball = balls[0];
    var router = ball.GetComponent<ImuGameplayInput>() ?? Undo.AddComponent<ImuGameplayInput>(ball.gameObject);
    var tutorialInput = ball.GetComponent<TutorialInput>();
    var ballSettings = new SerializedObject(ball);
    ballSettings.FindProperty("inputProvider").objectReferenceValue = tutorialInput != null ? (UnityEngine.Object)tutorialInput : router;
    ballSettings.ApplyModifiedProperties();
    var run = transforms.Select(t => t.GetComponent<GameplayRunController>()).FirstOrDefault(c => c != null);
    var tutorial = transforms.Select(t => t.GetComponent<TutorialController>()).FirstOrDefault(c => c != null);
    var template = transforms.Select(t => t.GetComponent<TMP_Text>()).FirstOrDefault(c => c != null);
    var font = template != null ? template.font : TMP_Settings.defaultFontAsset;

    var canvasObject = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ImuRecoveryCanvas");
    if (canvasObject == null)
    {
        canvasObject = new GameObject("ImuRecoveryCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Add sensor recovery canvas");
    }
    var canvas = canvasObject.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 2000;
    var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920, 1080);
    scaler.matchWidthOrHeight = .5f;
    var recovery = canvasObject.GetComponent<ImuGameplayRecoveryUI>() ?? Undo.AddComponent<ImuGameplayRecoveryUI>(canvasObject);

    RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var child = parent.Find(name);
        RectTransform rect;
        if (child == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Add sensor recovery control");
            go.transform.SetParent(parent, false);
            rect = (RectTransform)go.transform;
        }
        else rect = (RectTransform)child;
        Undo.RecordObject(rect, "Position sensor recovery control");
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
    TMP_Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = Rect(parent, name, position, size);
        var label = rect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        Undo.RecordObject(label, "Set sensor recovery text");
        label.font = font;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }
    var shade = Rect(canvasObject.transform, "SensorRecoveryPanel", Vector2.zero, Vector2.zero);
    shade.anchorMin = Vector2.zero;
    shade.anchorMax = Vector2.one;
    var shadeImage = shade.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(shade.gameObject);
    shadeImage.color = new Color(.02f, .04f, .06f, .94f);
    shadeImage.raycastTarget = true;
    Label(shade, "Title", "Sensor connection paused", new Vector2(0, 175), new Vector2(1000, 75), 42);
    var status = Label(shade, "Status", "Reconnect and calibrate your sensor to continue.", new Vector2(0, 60), new Vector2(1100, 145), 28);
    UnityEngine.UI.Button Button(string name, string title, float x, UnityAction action)
    {
        var rect = Rect(shade, name, new Vector2(x, -100), new Vector2(280, 85));
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
        image.color = new Color(.12f, .37f, .4f, 1);
        var button = rect.GetComponent<UnityEngine.UI.Button>() ?? Undo.AddComponent<UnityEngine.UI.Button>(rect.gameObject);
        button.targetGraphic = image;
        var colors = button.colors;
        colors.disabledColor = new Color(.3f, .3f, .3f, .45f);
        button.colors = colors;
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, action);
        Label(rect, "Label", title, Vector2.zero, new Vector2(260, 70), 27);
        return button;
    }
    var reconnect = Button("Reconnect", "Reconnect", -310, recovery.Reconnect);
    var calibrate = Button("Calibrate", "Calibrate", 0, recovery.Calibrate);
    var resume = Button("Resume", "Resume", 310, recovery.Resume);
    var exit = Button("Exit", run != null ? "End run" : "Return to menu", 0, recovery.Exit);
    ((RectTransform)exit.transform).anchoredPosition = new Vector2(0, -220);
    var settings = new SerializedObject(recovery);
    settings.FindProperty("input").objectReferenceValue = router;
    settings.FindProperty("run").objectReferenceValue = run;
    settings.FindProperty("tutorial").objectReferenceValue = tutorial;
    settings.FindProperty("panel").objectReferenceValue = shade.gameObject;
    settings.FindProperty("message").objectReferenceValue = status;
    settings.FindProperty("reconnect").objectReferenceValue = reconnect;
    settings.FindProperty("calibrate").objectReferenceValue = calibrate;
    settings.FindProperty("resume").objectReferenceValue = resume;
    settings.ApplyModifiedProperties();
    shade.gameObject.SetActive(false);
    EditorUtility.SetDirty(router);
    EditorUtility.SetDirty(ball);
    EditorSceneManager.MarkSceneDirty(scene);
    EditorSceneManager.SaveScene(scene);
    report.Add(scenePath + ": routed " + ball.name + " input and added reconnect/calibrate/resume panel.");
    SceneManager.SetActiveScene(original);
    if (opened) EditorSceneManager.CloseScene(scene, true);
}
AssetDatabase.SaveAssets();
return report;
