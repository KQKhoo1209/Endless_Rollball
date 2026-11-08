using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

public static class BuildSensorReticle
{
    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainMenuScene.unity" || Application.isPlaying)
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before authoring.");
        Transform page = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .First(t => t.name == "SensorCalibrationPage");
        Transform panel = page.Find("CalibrationPanel");
        RectTransform hand = page.Find("WearGuidePanel/HandImage") as RectTransform;
        Component owner = page.GetComponents<Component>().First(c => c != null && c.GetType().Name == "SensorCalibrationUI");
        Type visualType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("SensorCalibrationVisual"))
            .FirstOrDefault(t => t != null);
        if (visualType == null) throw new InvalidOperationException("The new visual source is awaiting Unity's normal script import.");
        if (panel == null || hand == null) throw new InvalidOperationException("The existing calibration panel or wear-guide hand was not found.");

        Directory.CreateDirectory("../.tmp/sensor-visual-before");
        File.Copy(scene.path, "../.tmp/sensor-visual-before/MainMenuScene.unity", true);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add sensor calibration crosshair");
        try
        {
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);
            RectTransform area = Rect(panel, "MovementCrosshair", 35, 115, 830, 245);
            Component visual = area.GetComponent(visualType) ?? Undo.AddComponent(area.gameObject, visualType);
            var graphic = (UnityEngine.UI.Graphic)visual;
            Undo.RecordObject(graphic, "Configure crosshair");
            graphic.raycastTarget = false;
            graphic.color = Color.white;
            TMP_Text template = panel.GetComponentsInChildren<TMP_Text>(true).First();
            TMP_Text message = Text(panel, "CalibrationMessage", "Connect your sensor to begin.", 35, 369, 830, 44, 28, template);
            Text(panel, "MovementHint", "Tilt moves the dot. Wrist rotation turns the cross.", 35, 421, 830, 34, 23, template);
            Transform oldTrack = panel.Find("CalibrationProgressTrack");
            if (oldTrack != null)
            {
                Undo.RecordObject(oldTrack.gameObject, "Use progress ring instead of bar");
                oldTrack.gameObject.SetActive(false);
            }

            Transform footer = page.Find("DownPanel");
            UnityEngine.UI.Button retryConnection = footer.Find("RetryConnectionButton").GetComponent<UnityEngine.UI.Button>();
            UnityEngine.UI.Button next = footer.GetComponentsInChildren<UnityEngine.UI.Button>(true)
                .First(b => b.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.Trim().StartsWith("Continue")));
            var calibrationButtonRect = Rect(footer, "CalibrateButton", 800, 40, 300, 100);
            var image = calibrationButtonRect.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(calibrationButtonRect.gameObject);
            Undo.RecordObject(image, "Style calibration button");
            var referenceImage = retryConnection.GetComponent<UnityEngine.UI.Image>();
            image.sprite = referenceImage.sprite;
            image.type = referenceImage.type;
            image.color = referenceImage.color;
            image.pixelsPerUnitMultiplier = referenceImage.pixelsPerUnitMultiplier;
            image.raycastTarget = true;
            var calibrate = calibrationButtonRect.GetComponent<UnityEngine.UI.Button>() ?? Undo.AddComponent<UnityEngine.UI.Button>(calibrationButtonRect.gameObject);
            Undo.RecordObject(calibrate, "Wire calibration action");
            calibrate.targetGraphic = image;
            calibrate.interactable = false;
            Text(calibrationButtonRect, "Label", "Calibrate / Retry", 12, 12, 276, 76, 30, template);
            Bind(calibrate, owner, "RetryCalibration");
            Bind(retryConnection, owner, "RetryConnection");
            Bind(next, owner, "ContinueWhenReady");
            Undo.RecordObject(next, "Require valid calibration");
            next.interactable = false;

            Undo.RecordObject(owner, "Wire calibration visual");
            var serialized = new SerializedObject(owner);
            Set(serialized, "movementVisual", visual);
            Set(serialized, "handPreview", hand);
            Set(serialized, "calibrationMessage", message);
            Set(serialized, "calibrationRetry", calibrate);
            Set(serialized, "calibrationContinue", next);
            Set(serialized, "progressFill", null);
            Set(serialized, "progressLabel", null);
            serialized.ApplyModifiedProperties();
            var device = page.Find("ConnectSensorPanel/SensorDevice").GetComponent<TMP_Text>();
            Undo.RecordObject(device, "Set sensor device label");
            device.text = "Sensor Device: ---";
            device.richText = false;
            var deviceBinding = new SerializedObject(owner);
            Set(deviceBinding, "sensorDevice", device);
            deviceBinding.ApplyModifiedProperties();
            var status = page.Find("ConnectSensorPanel/ConnectionStatus").GetComponent<TMP_Text>();
            Undo.RecordObject(status, "Set disconnected label");
            status.text = "Connection Status: Not connected";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved MovementCrosshair, calibration message and action wiring on the existing SensorCalibrationPage.";
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        Transform found = parent.Find(name);
        RectTransform rect;
        if (found != null) rect = (RectTransform)found;
        else
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Create calibration visual");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            rect = (RectTransform)go.transform;
        }
        Undo.RecordObject(rect, "Position calibration visual");
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static TMP_Text Text(Transform parent, string name, string value,
        float x, float y, float w, float h, float size, TMP_Text template)
    {
        var rect = Rect(parent, name, x, y, w, h);
        var label = rect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        Undo.RecordObject(label, "Set calibration copy");
        label.font = template.font;
        label.fontSharedMaterial = template.fontSharedMaterial;
        label.color = template.color;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }

    private static void Set(SerializedObject owner, string field, UnityEngine.Object value)
    {
        var property = owner.FindProperty(field);
        if (property == null) throw new InvalidOperationException("Awaiting updated controller import: " + field);
        property.objectReferenceValue = value;
    }

    private static void Bind(UnityEngine.UI.Button button, Component owner, string method)
    {
        Undo.RecordObject(button, "Wire calibration button");
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        var callback = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), owner, method);
        UnityEventTools.AddPersistentListener(button.onClick, callback);
    }
}
