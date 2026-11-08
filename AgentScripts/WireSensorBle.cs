using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using EndlessRollball.Imu;

var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
    throw new InvalidOperationException("Open MainMenuScene in Edit Mode before authoring the BLE UI.");
var page = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
    .Single(t => t.name == "SensorCalibrationPage");
var view = page.GetComponent<SensorCalibrationUI>();
if (view == null) throw new InvalidOperationException("The existing calibration view is missing.");
var importer = AssetImporter.GetAtPath("Assets/Plugins/BleWinrt/BleWinrtDll.dll") as PluginImporter;
if (importer == null) throw new InvalidOperationException("The native plugin is awaiting normal Unity import. Do not refresh for validation.");
importer.SetCompatibleWithAnyPlatform(false);
importer.SetCompatibleWithEditor(true);
importer.SetEditorData("OS", "Windows");
importer.SetEditorData("CPU", "x86_64");
importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
// Apply authoring settings through the importer; no explicit refresh/recompile is requested.
AssetDatabase.WriteImportSettingsIfDirty(importer.assetPath);

Undo.IncrementCurrentGroup();
Undo.SetCurrentGroupName("Connect real IMU to calibration UI");
var controller = page.GetComponent<ImuCalibrationController>() ?? Undo.AddComponent<ImuCalibrationController>(page.gameObject);
var panel = page.Find("WearGuidePanel");
var connect = page.Find("ConnectSensorPanel");
var footer = page.Find("DownPanel");
if (panel == null || connect == null || footer == null) throw new InvalidOperationException("Calibration UI panels are missing.");
var template = panel.GetComponentsInChildren<TMP_Text>(true).First();

RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
{
    var child = parent.Find(name);
    RectTransform rect;
    if (child == null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create BLE diagnostics UI");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        rect = (RectTransform)go.transform;
    }
    else rect = (RectTransform)child;
    Undo.RecordObject(rect, "Position BLE diagnostic UI");
    rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
    rect.pivot = new Vector2(0, 1);
    rect.anchoredPosition = new Vector2(x, -y);
    rect.sizeDelta = new Vector2(width, height);
    return rect;
}

TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h, float size)
{
    var rect = Rect(parent, name, x, y, w, h);
    var label = rect.GetComponent<TextMeshProUGUI>() ?? Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
    Undo.RecordObject(label, "Configure sensor readout");
    label.font = template.font;
    label.fontSharedMaterial = template.fontSharedMaterial;
    label.fontSize = size;
    label.color = template.color;
    label.alignment = TextAlignmentOptions.Center;
    label.raycastTarget = false;
    label.text = value;
    return label;
}

var diagnostics = Text(panel, "LiveSensorReadings", "Waiting for live IMU data. No samples are recorded to disk.", 35, 535, 830, 145, 22);
var message = Text(connect, "ConnectionDetail", "Power on your sensor and select Retry Connection.", 35, 190, 830, 32, 19);
var retry = footer.Find("RetryConnectionButton").GetComponent<UnityEngine.UI.Button>();
var disconnectRect = Rect(footer, "DisconnectSensorButton", 400, 40, 220, 100);
var image = disconnectRect.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(disconnectRect.gameObject);
var referenceImage = retry.GetComponent<UnityEngine.UI.Image>();
Undo.RecordObject(image, "Style disconnect button");
image.sprite = referenceImage.sprite;
image.type = referenceImage.type;
image.color = referenceImage.color;
image.raycastTarget = true;
var button = disconnectRect.GetComponent<UnityEngine.UI.Button>() ?? Undo.AddComponent<UnityEngine.UI.Button>(disconnectRect.gameObject);
Undo.RecordObject(button, "Wire disconnect button");
button.targetGraphic = image;
button.colors = retry.colors;
for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
UnityEventTools.AddPersistentListener(button.onClick, new UnityAction(controller.Disconnect));
Text(disconnectRect, "Label", "Disconnect", 10, 10, 200, 80, 24);

var serialized = new SerializedObject(view);
serialized.FindProperty("sensorDiagnostics").objectReferenceValue = diagnostics;
serialized.FindProperty("connectionMessage").objectReferenceValue = message;
serialized.FindProperty("previewTimeout").floatValue = .25f;
serialized.ApplyModifiedProperties();
EditorUtility.SetDirty(view);
EditorUtility.SetDirty(controller);
// Preserve existing preparation/ready navigation and every unrelated object in the scene.
PrefabUtility.SaveAsPrefabAsset(page.gameObject, "Assets/EndlessRollballAssets/UI Asset/SensorCalibrationUI.prefab");
EditorSceneManager.MarkSceneDirty(scene);
EditorSceneManager.SaveScene(scene);
AssetDatabase.SaveAssets();
return "Added Windows BLE controller, live readings, connection detail and Disconnect to existing calibration page and prefab.";
