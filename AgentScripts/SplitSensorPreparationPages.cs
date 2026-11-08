using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

public static class SplitSensorPreparationPages
{
    public static string ImportSources()
    {
        AssetDatabase.ImportAsset("Assets/Scripts/UI/PreparationFlowController.cs", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Scripts/UI/SensorCalibrationUI.cs", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Scripts/UI/GameSetupUI.cs", ImportAssetOptions.ForceUpdate);
        return "Imported the separate-page preparation routing for scene authoring.";
    }

    public static string RepairEntryPoints()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before wiring preparation entry points.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var calibration = all.Single(t => t.name == "NeutralCalibrationPage");
        var flow = all.SelectMany(t => t.GetComponents<Component>()).First(c => c != null && c.GetType().Name == "PreparationFlowController");
        var setup = all.SelectMany(t => t.GetComponents<Component>()).First(c => c != null && c.GetType().Name == "GameSetupUI");
        Set(setup, "preparationFlow", flow);
        int changed = 0;
        foreach (var button in all.SelectMany(t => t.GetComponents<UnityEngine.UI.Button>()))
        {
            var data = new SerializedObject(button);
            var calls = data.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int i = 0; i < calls.arraySize; i++)
            {
                var call = calls.GetArrayElementAtIndex(i);
                if (call.FindPropertyRelative("m_Target").objectReferenceValue != calibration.gameObject ||
                    call.FindPropertyRelative("m_MethodName").stringValue != "SetActive" ||
                    !call.FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue) continue;
                Undo.RecordObject(button, "Enter connection stage through preparation flow");
                call.FindPropertyRelative("m_Target").objectReferenceValue = flow;
                call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = flow.GetType().AssemblyQualifiedName;
                call.FindPropertyRelative("m_MethodName").stringValue = "Begin";
                call.FindPropertyRelative("m_Mode").enumValueIndex = 1;
                changed++;
            }
            data.ApplyModifiedProperties();
        }
        var guide = all.Single(t => t.name == "GuidancePage");
        var back = guide.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>();
        Undo.RecordObject(back, "Return from guide to neutral calibration");
        for (int i = back.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(back.onClick, i);
        UnityEventTools.AddPersistentListener(back.onClick,
            (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), flow, "ShowNeutralCalibration"));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Saved setup routing for all modes, guide Back to calibration, and " + changed + " legacy page entry replacement(s).";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainMenuScene.unity" || Application.isPlaying)
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before separating the pages.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        if (all.Any(t => t.name == "SensorConnectionPage" || t.name == "NeutralCalibrationPage"))
            throw new InvalidOperationException("Separate sensor pages already exist; edit them in place.");
        Transform original = all.Single(t => t.name == "SensorCalibrationPage");
        Component flow = all.SelectMany(t => t.GetComponents<Component>()).First(c => c != null && c.GetType().Name == "PreparationFlowController");
        var flowData = new SerializedObject(flow);
        if (flowData.FindProperty("connection") == null || flowData.FindProperty("calibration") == null)
            throw new InvalidOperationException("The separate-page flow source is awaiting import.");
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("../.tmp/separate-sensor-pages-before");
        File.Copy(scene.path, "../.tmp/separate-sensor-pages-before/MainMenuScene.unity", true);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Separate sensor connection and calibration pages");
        try
        {
            bool wasVisible = original.gameObject.activeSelf;
            var connectionObject = UnityEngine.Object.Instantiate(original.gameObject, original.parent);
            Undo.RegisterCreatedObjectUndo(connectionObject, "Create separate connection page");
            connectionObject.name = "SensorConnectionPage";
            connectionObject.SetActive(false);
            Transform connection = connectionObject.transform;
            Component connectionView = View(connection);
            Component calibrationView = View(original);
            Undo.RecordObject(original.gameObject, "Name final neutral calibration page");
            original.name = "NeutralCalibrationPage";
            original.gameObject.SetActive(false);

            // Keep only connection controls on the new page. Clear references before removing duplicates.
            foreach (string field in new[] { "calibrationPage", "calibrationMessage", "calibrationConnection",
                "selectedSettings", "progressLabel", "progressFill", "calibrationRetry", "movementVisual", "handPreview" })
                Set(connectionView, field, null);
            Bool(connectionView, "combinedPanels", false);
            Set(connectionView, "preparationFlow", flow);
            var connectionTitle = connection.Find("PageTitle").GetComponent<TMP_Text>();
            Set(connectionView, "preparationTitle", connectionTitle);
            Copy(connectionTitle, "Connect Sensor");
            var removeCalibration = connection.Find("CalibrationPanel");
            if (removeCalibration != null) Undo.DestroyObjectImmediate(removeCalibration.gameObject);
            var removeCalibrateButton = connection.Find("DownPanel/CalibrateButton");
            if (removeCalibrateButton != null) Undo.DestroyObjectImmediate(removeCalibrateButton.gameObject);
            var connectionPanel = (RectTransform)connection.Find("ConnectSensorPanel");
            SizePanel(connectionPanel);
            var connectionFont = connectionPanel.GetComponentsInChildren<TMP_Text>(true).First();
            Text(connectionPanel, "ConnectionGuide", "1   Power on your wrist sensor.\n2   Enable Bluetooth on this PC.\n3   Select Retry Connection.\n\nContinue when live sensor data is received.",
                40, 280, 820, 245, 30, connectionFont);
            var detail = connectionPanel.Find("ConnectionDetail") as RectTransform;
            if (detail != null) Position(detail, 35, 570, 830, 100);
            var connectInstructions = connection.Find("WearGuidePanel/Instructions").GetComponent<TMP_Text>();
            Copy(connectInstructions, "1. Place the controller on your wrist.\n2. Fasten the strap comfortably.\n3. Keep the controller facing upward.");
            Position(connectInstructions.rectTransform, 70, 420, 760, 105);
            connectInstructions.fontSize = 28;

            // The original calibration widgets retain their identities and real sensor behaviour.
            Transform neutralPanel = original.Find("CalibrationPanel");
            SizePanel((RectTransform)neutralPanel);
            var calibrationTitle = original.Find("PageTitle").GetComponent<TMP_Text>();
            Copy(calibrationTitle, "Neutral Calibration");
            Set(calibrationView, "preparationTitle", calibrationTitle);
            Set(calibrationView, "preparationFlow", flow);
            Bool(calibrationView, "combinedPanels", false);
            Copy(neutralPanel.Find("PanelTitle").GetComponent<TMP_Text>(), "Neutral Calibration");
            var template = neutralPanel.GetComponentsInChildren<TMP_Text>(true).First();
            var device = Text(neutralPanel, "SensorDevice", "Sensor Device: ---", 35, 110, 830, 36, 25, template);
            device.richText = false;
            var status = Text(neutralPanel, "ConnectionStatus", "Connection Status: Not connected", 35, 155, 830, 40, 25, template);
            Set(calibrationView, "sensorDevice", device);
            Set(calibrationView, "connectionStatus", status);
            Set(calibrationView, "connectionMessage", null);
            Set(calibrationView, "connectPage", null);
            Set(calibrationView, "connectContinue", null);
            var removeConnectionPanel = original.Find("ConnectSensorPanel");
            if (removeConnectionPanel != null) Undo.DestroyObjectImmediate(removeConnectionPanel.gameObject);
            Position((RectTransform)neutralPanel.Find("MovementCrosshair"), 35, 215, 830, 300);
            var message = neutralPanel.Find("CalibrationMessage").GetComponent<TMP_Text>();
            Position(message.rectTransform, 35, 548, 830, 66);
            Copy(message, "Rest your wrist comfortably, then select Calibrate.");
            var hint = neutralPanel.Find("MovementHint").GetComponent<TMP_Text>();
            Position(hint.rectTransform, 35, 639, 830, 52);
            Copy(hint, "Rotate your wrist to turn the cross. Hold still to set your neutral position.");
            Copy(original.Find("WearGuidePanel/PanelTitle").GetComponent<TMP_Text>(), "Find Your Resting Position");
            var neutralInstructions = original.Find("WearGuidePanel/Instructions").GetComponent<TMP_Text>();
            Copy(neutralInstructions, "Rest your wrist in a comfortable position.\nHold still while your neutral position is recorded.");
            Position(neutralInstructions.rectTransform, 70, 420, 760, 105);
            neutralInstructions.fontSize = 28;
            var calibrate = original.Find("DownPanel/CalibrateButton") as RectTransform;
            if (calibrate != null) Position(calibrate, 800, 40, 300, 100);

            Set(flow, "connection", connectionView);
            Set(flow, "calibration", calibrationView);
            var pain = all.Single(t => t != null && t.name == "PainStressCheckPage");
            Set(pain.GetComponents<Component>().First(c => c != null && c.GetType().Name == "PainStressCheckUI"), "sensorPage", connectionObject);
            // Existing menu event references still call SensorCalibrationUI.Open -> flow.Begin.
            var menuEntry = all.FirstOrDefault(t => t != null && t.name == "SensorSetupButton");
            if (menuEntry != null)
            {
                var label = menuEntry.GetComponentInChildren<TMP_Text>();
                if (label != null) Copy(label, "Connect Sensor");
            }
            connectionObject.SetActive(wasVisible);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved separate SensorConnectionPage and NeutralCalibrationPage with one shared BLE session and corrected preparation routing.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static Component View(Transform page) => page.GetComponents<Component>().First(c => c != null && c.GetType().Name == "SensorCalibrationUI");

    private static void Set(Component target, string name, UnityEngine.Object value)
    {
        Undo.RecordObject(target, "Wire separated sensor page");
        var data = new SerializedObject(target);
        var property = data.FindProperty(name);
        if (property == null) throw new InvalidOperationException("Missing page field: " + name);
        property.objectReferenceValue = value;
        data.ApplyModifiedProperties();
    }

    private static void Bool(Component target, string name, bool value)
    {
        Undo.RecordObject(target, "Use a single-purpose page");
        var data = new SerializedObject(target);
        data.FindProperty(name).boolValue = value;
        data.ApplyModifiedProperties();
    }

    private static void SizePanel(RectTransform rect)
    {
        Undo.RecordObject(rect, "Expand page content panel");
        rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(-500, 0);
        rect.sizeDelta = new Vector2(900, 720);
    }

    private static void Position(RectTransform rect, float x, float y, float w, float h)
    {
        Undo.RecordObject(rect, "Position separated page content");
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
    }

    private static void Copy(TMP_Text label, string text)
    {
        Undo.RecordObject(label, "Set page guidance");
        label.text = text;
    }

    private static TMP_Text Text(Transform parent, string name, string content, float x, float y, float w, float h, float size, TMP_Text template)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        Undo.RegisterCreatedObjectUndo(go, "Add separated page status");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        Position((RectTransform)go.transform, x, y, w, h);
        label.font = template.font;
        label.fontSharedMaterial = template.fontSharedMaterial;
        label.color = template.color;
        label.fontSize = size;
        label.alignment = TextAlignmentOptions.Left;
        label.raycastTarget = false;
        label.text = content;
        return label;
    }
}
