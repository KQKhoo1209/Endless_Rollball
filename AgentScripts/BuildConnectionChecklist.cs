using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BuildConnectionChecklist
{
    public static string ImportSources()
    {
        foreach (string path in new[] { "Assets/Scripts/Input/Imu/IImuTransport.cs",
            "Assets/Scripts/Input/Imu/WindowsBleImuTransport.cs", "Assets/Scripts/Input/Imu/ImuRuntimeSession.cs",
            "Assets/Scripts/UI/SensorCalibrationUI.cs", "Assets/Scripts/UI/PaintingSessionUI.cs" })
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return "Imported three-stage connection readiness source assets for scene wiring.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before creating readiness checks.");
        var page = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Single(t => t.name == "SensorConnectionPage");
        var panel = page.Find("ConnectSensorPanel");
        if (panel.Find("ReadinessChecklist") != null) throw new InvalidOperationException("The checklist already exists; edit it in place.");
        var owner = page.GetComponents<Component>().First(c => c != null && c.GetType().Name == "SensorCalibrationUI");
        var data = new SerializedObject(owner);
        if (data.FindProperty("connectionCheckBoxes") == null) throw new InvalidOperationException("The updated checklist source is awaiting import.");
        var template = panel.GetComponentsInChildren<TMP_Text>(true).First();
        var square = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
        if (square == null || checkmark == null) throw new InvalidOperationException("Unity checkbox sprites are unavailable.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add three visual sensor connection checks");
        try
        {
            var oldStatus = (TMP_Text)data.FindProperty("connectionStatus").objectReferenceValue;
            if (oldStatus != null)
            {
                Undo.RecordObject(oldStatus.gameObject, "Replace single-line status with checklist");
                oldStatus.gameObject.SetActive(false);
            }
            var oldGuide = panel.Find("ConnectionGuide");
            if (oldGuide != null)
            {
                Undo.RecordObject(oldGuide.gameObject, "Use space for readiness checklist");
                oldGuide.gameObject.SetActive(false);
            }
            var heading = Text(panel, "ReadinessTitle", "Connection checks", 35, 178, 830, 44, 29, template);
            heading.fontStyle = FontStyles.Bold;
            var root = Rect(panel, "ReadinessChecklist", 35, 238, 830, 298);
            string[] labels = { "Sensor detected", "Bluetooth connected", "Live data received" };
            string[] fields = { "poweredStatus", "detectedStatus", "dataStatus" };
            var boxes = data.FindProperty("connectionCheckBoxes");
            var ticks = data.FindProperty("connectionCheckTicks");
            var waiting = data.FindProperty("connectionCheckWaiting");
            var states = data.FindProperty("connectionCheckStatuses");
            boxes.arraySize = ticks.arraySize = waiting.arraySize = states.arraySize = 3;
            Undo.RecordObject(owner, "Wire connection milestone indicators");
            data.FindProperty("connectionStatus").objectReferenceValue = null;
            for (int i = 0; i < 3; i++)
            {
                var row = Rect(root, "Check" + (i + 1), 0, i * 104, 830, 88);
                Image(row, new Color(.98f, .98f, .94f, .94f));
                var boxRect = Rect(row, "StatusBox", 24, 24, 40, 40);
                var box = Image(boxRect, new Color(.78f, .80f, .76f), square);
                var tickRect = Rect(boxRect, "CompletedTick", 8, 8, 24, 24);
                var tick = Image(tickRect, Color.white, checkmark);
                tick.type = UnityEngine.UI.Image.Type.Simple;
                tick.preserveAspect = true;
                tickRect.gameObject.SetActive(false);
                var dash = Rect(boxRect, "WaitingDash", 13, 18, 14, 4);
                Image(dash, new Color(.36f, .41f, .37f));
                var label = Text(row, "Label", labels[i], 90, 19, 570, 50, 28, template);
                var state = Text(row, "StatusText", "Waiting", 675, 24, 130, 40, 23, template);
                state.alignment = TextAlignmentOptions.Right;
                data.FindProperty(fields[i]).objectReferenceValue = label;
                boxes.GetArrayElementAtIndex(i).objectReferenceValue = box;
                ticks.GetArrayElementAtIndex(i).objectReferenceValue = tickRect.gameObject;
                waiting.GetArrayElementAtIndex(i).objectReferenceValue = dash.gameObject;
                states.GetArrayElementAtIndex(i).objectReferenceValue = state;
            }
            data.ApplyModifiedProperties();
            var detail = panel.Find("ConnectionDetail") as RectTransform;
            if (detail != null)
            {
                Undo.RecordObject(detail, "Keep connection guidance below checklist");
                detail.anchorMin = detail.anchorMax = new Vector2(0, 1);
                detail.pivot = new Vector2(0, 1);
                detail.anchoredPosition = new Vector2(35, -570);
                detail.sizeDelta = new Vector2(830, 100);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved Sensor detected, Bluetooth connected and Live data received checkboxes with real milestone updates, green completed ticks and grey waiting markers.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create connection readiness row");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static UnityEngine.UI.Image Image(RectTransform rect, Color tint, Sprite sprite = null)
    {
        var image = Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
        image.sprite = sprite;
        image.color = tint;
        image.raycastTarget = false;
        if (sprite != null) image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    private static TMP_Text Text(Transform parent, string name, string text, float x, float y,
        float w, float h, float size, TMP_Text template)
    {
        var label = Undo.AddComponent<TextMeshProUGUI>(Rect(parent, name, x, y, w, h).gameObject);
        label.font = template.font;
        label.fontSharedMaterial = template.fontSharedMaterial;
        label.color = template.color;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Left;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }
}
