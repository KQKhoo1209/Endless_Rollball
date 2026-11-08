using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

public static class BuildPaintingPage
{
    private static TMP_Text font;
    private static UnityEngine.UI.Image buttonStyle;
    private static UnityEngine.UI.Button buttonReference;
    private static UnityEngine.UI.Image panelStyle;
    private static Component painting;
    private static Color ink;

    public static string ImportSources()
    {
        foreach (string path in new[] { "Assets/Shaders/PaintingStroke.shader", "Assets/Scripts/UI/PaintingCanvas.cs",
            "Assets/Scripts/UI/PaintingSessionUI.cs", "Assets/Scripts/UI/PreparationFlowController.cs",
            "Assets/Scripts/UI/SensorCalibrationUI.cs", "Assets/Scripts/UI/PainStressCheckUI.cs",
            "Assets/Scripts/Input/Imu/ImuRuntimeSession.cs" })
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return "Imported painting source assets for page authoring.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainMenuScene.unity" || Application.isPlaying)
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before authoring.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var sensor = all.Single(t => t.name == "SensorCalibrationPage");
        var ratings = all.Single(t => t.name == "PainStressCheckPage");
        var menu = all.Single(t => t.name == "MainMenuPage");
        var guide = all.Single(t => t.name == "GuidancePage");
        if (all.Any(t => t.name == "PaintingPage")) throw new InvalidOperationException("PaintingPage already exists; edit it in place.");
        Type canvasType = FindType("PaintingCanvas"), viewType = FindType("PaintingSessionUI"), flowType = FindType("PreparationFlowController");
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/PaintingStroke.shader");
        if (shader == null) throw new InvalidOperationException("The painting shader source is awaiting asset import.");
        font = sensor.Find("PageTitle").GetComponent<TMP_Text>();
        buttonReference = sensor.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>();
        buttonStyle = buttonReference.GetComponent<UnityEngine.UI.Image>();
        ink = buttonReference.GetComponentInChildren<TMP_Text>().color;
        panelStyle = sensor.Find("WearGuidePanel").GetComponent<UnityEngine.UI.Image>();
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("../.tmp/painting-before");
        File.Copy(scene.path, "../.tmp/painting-before/MainMenuScene.unity", true);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add optional gentle painting and preparation flow");
        try
        {
            var flowObject = new GameObject("Preparation Flow");
            Undo.RegisterCreatedObjectUndo(flowObject, "Create preparation flow owner");
            Component flow = Undo.AddComponent(flowObject, flowType);
            RectTransform page = Rect(sensor.parent, "PaintingPage", 0, 0, 1920, 1080);
            page.gameObject.SetActive(false);
            page.anchorMin = page.anchorMax = page.pivot = new Vector2(.5f, .5f);
            page.anchoredPosition = Vector2.zero;
            var backdrop = Image(page, Color.white, sensor.GetComponent<UnityEngine.UI.Image>().sprite);
            backdrop.type = UnityEngine.UI.Image.Type.Simple;
            backdrop.raycastTarget = true;
            painting = Undo.AddComponent(page.gameObject, viewType);
            Component canvas = Undo.AddComponent(page.gameObject, canvasType);
            Text(page, "PageTitle", "Gentle Painting", 70, 30, 1780, 66, 44);
            Text(page, "Introduction", "Rotate your wrist gently to paint. You can skip this optional activity at any time.", 74, 103, 1770, 42, 27);
            Text(page, "PreparationSteps", "CONNECT   /   PAIN & STRESS   /   PAINTING (OPTIONAL)   /   NEUTRAL CALIBRATION", 74, 149, 1770, 30, 21);

            var canvasPanel = Panel(page, "CanvasPanel", 70, 190, 1290, 650);
            var surfaceRect = Rect(canvasPanel, "DrawingSurface", 20, 20, 1250, 610);
            var surface = Undo.AddComponent<UnityEngine.UI.RawImage>(surfaceRect.gameObject);
            surface.color = new Color(1, .995f, .98f);
            surface.raycastTarget = false;
            var cursorRect = Rect(surfaceRect, "BrushCursor", 0, 0, 24, 24);
            cursorRect.anchorMin = cursorRect.anchorMax = Vector2.zero;
            cursorRect.pivot = new Vector2(.5f, .5f);
            cursorRect.anchoredPosition = new Vector2(625, 305);
            var cursor = Image(cursorRect, new Color(.20f, .48f, .76f), AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"));
            cursor.type = UnityEngine.UI.Image.Type.Simple;
            cursor.preserveAspect = true;

            var tools = Panel(page, "ToolsPanel", 1390, 190, 460, 730);
            var connection = Text(tools, "ConnectionStatus", "BLE: Not connected", 25, 20, 410, 78, 26);
            var settings = Text(tools, "SelectedSettings", "Choose a game mode before play", 25, 106, 410, 62, 23);
            var originalHand = sensor.Find("WearGuidePanel/HandImage").GetComponent<UnityEngine.UI.Image>();
            var hand = Rect(tools, "HandPreview", 175, 184, 110, 140);
            var handImage = Image(hand, originalHand.color, originalHand.sprite);
            handImage.type = UnityEngine.UI.Image.Type.Simple;
            handImage.preserveAspect = true;
            Text(tools, "MovementHint", "Rotate your wrist gently to move the brush.", 25, 343, 410, 75, 25);
            var selections = new GameObject[3];
            string[] methods = { "SelectBlue", "SelectCoral", "SelectPurple" };
            string[] names = { "Blue", "Coral", "Purple" };
            Color[] colours = { new Color(.20f, .48f, .76f), new Color(.85f, .36f, .29f), new Color(.56f, .35f, .70f) };
            for (int i = 0; i < 3; i++)
            {
                var swatch = Button(tools, names[i] + "Swatch", "", 25 + i * 145, 445, 120, 94, painting, methods[i]);
                var disc = Image(Rect(swatch.transform, "Colour", 30, 8, 60, 60), colours[i],
                    AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"));
                disc.type = UnityEngine.UI.Image.Type.Simple;
                Text(swatch.transform, "ColourName", names[i], 8, 64, 104, 24, 18);
                selections[i] = Rect(swatch.transform, "SelectedUnderline", 8, 89, 104, 3).gameObject;
                Image((RectTransform)selections[i].transform, ink);
                selections[i].SetActive(i == 0);
            }
            Button(tools, "ClearButton", "Clear", 25, 565, 410, 70, painting, "Clear");
            var reconnect = Button(tools, "ReconnectButton", "Retry Connection", 25, 648, 410, 60, painting, "Reconnect");
            var message = Text(page, "InputMessage", "Connect your sensor to paint, or continue to calibration.", 75, 849, 1280, 70, 26);
            Text(page, "SupportMessage", "No target to complete. Take your time, or continue whenever you are ready.", 75, 928, 1770, 32, 22);
            Button(page, "BackButton", "Back", 70, 978, 240, 72, painting, "Back");
            Button(page, "RestAndReturnButton", "Rest and Return", 705, 978, 450, 72, painting, "RestAndReturn");
            Button(page, "ContinueToCalibrationButton", "Continue to Calibration", 1380, 978, 470, 72, painting, "ContinueToCalibration");

            Set(canvas, "surface", surface); Set(canvas, "strokeShader", shader);
            Set(painting, "drawing", canvas); Set(painting, "drawingRect", surfaceRect); Set(painting, "brushCursor", cursor);
            Set(painting, "connectionStatus", connection); Set(painting, "selectedSettings", settings); Set(painting, "inputMessage", message);
            Set(painting, "reconnectButton", reconnect); Set(painting, "handPreview", hand); Set(painting, "flow", flow);
            var viewData = new SerializedObject(painting);
            var colourSelections = viewData.FindProperty("colourSelections");
            colourSelections.arraySize = 3;
            for (int i = 0; i < 3; i++) colourSelections.GetArrayElementAtIndex(i).objectReferenceValue = selections[i];
            viewData.ApplyModifiedProperties();
            var sensorOwner = sensor.GetComponents<Component>().First(c => c != null && c.GetType().Name == "SensorCalibrationUI");
            var ratingsOwner = ratings.GetComponents<Component>().First(c => c != null && c.GetType().Name == "PainStressCheckUI");
            Set(flow, "sensor", sensorOwner); Set(flow, "ratings", ratingsOwner); Set(flow, "painting", painting);
            Set(flow, "guidePage", guide.gameObject); Set(flow, "mainMenuPage", menu.gameObject);
            Set(sensorOwner, "preparationFlow", flow); Set(sensorOwner, "preparationTitle", font);
            Set(ratingsOwner, "preparationFlow", flow);

            Bind(sensor.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>(), sensorOwner, "Close");
            Bind(sensor.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b => b.onClick.GetPersistentEventCount() > 0 &&
                Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == "ContinueWhenReady")), sensorOwner, "ContinueWhenReady");
            var ratingData = new SerializedObject(ratingsOwner);
            Bind(ratings.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>(), ratingsOwner, "Back");
            Bind((UnityEngine.UI.Button)ratingData.FindProperty("continueButton").objectReferenceValue, ratingsOwner, "Continue");
            var restImage = (UnityEngine.UI.Image)ratingData.FindProperty("restBackground").objectReferenceValue;
            Bind(restImage.GetComponent<UnityEngine.UI.Button>(), ratingsOwner, "RestAndReturn");
            Button(menu, "GentlePaintingButton", "Gentle Painting", 160, 710, 360, 74, flow, "PreviewPainting");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved PaintingPage, live wrist brush wiring, three colours/Clear, skip/rest/back actions and final-neutral preparation routing.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null)
        ?? throw new InvalidOperationException("Awaiting source import: " + name);

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create painting UI");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
        return rect;
    }

    private static UnityEngine.UI.Image Image(RectTransform rect, Color colour, Sprite sprite = null)
    {
        var image = Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
        image.color = colour; image.sprite = sprite;
        image.raycastTarget = false;
        if (sprite != null) image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    private static RectTransform Panel(Transform parent, string name, float x, float y, float w, float h)
    {
        var panel = Rect(parent, name, x, y, w, h);
        Image(panel, new Color(1, .98f, .92f, .96f));
        var border = Image(Rect(panel, "Border", 0, 0, w, h), panelStyle.color, panelStyle.sprite);
        border.pixelsPerUnitMultiplier = panelStyle.pixelsPerUnitMultiplier;
        return panel;
    }

    private static TMP_Text Text(Transform parent, string name, string text, float x, float y, float w, float h, float size)
    {
        var label = Undo.AddComponent<TextMeshProUGUI>(Rect(parent, name, x, y, w, h).gameObject);
        label.font = font.font;
        label.fontSharedMaterial = font.fontSharedMaterial;
        label.fontSize = size; label.color = ink;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false; label.text = text;
        return label;
    }

    private static UnityEngine.UI.Button Button(Transform parent, string name, string text,
        float x, float y, float w, float h, Component target, string method)
    {
        var rect = Rect(parent, name, x, y, w, h);
        var image = Image(rect, buttonStyle.color, buttonStyle.sprite);
        image.pixelsPerUnitMultiplier = buttonStyle.pixelsPerUnitMultiplier;
        image.raycastTarget = true;
        var button = Undo.AddComponent<UnityEngine.UI.Button>(rect.gameObject);
        button.targetGraphic = image;
        button.colors = buttonReference.colors;
        button.transition = buttonReference.transition;
        Text(rect, "Label", text, 10, 6, w - 20, h - 12, 26);
        Bind(button, target, method);
        return button;
    }

    private static void Bind(UnityEngine.UI.Button button, Component target, string method)
    {
        Undo.RecordObject(button, "Wire preparation navigation");
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--) UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), target, method));
    }

    private static void Set(Component target, string field, UnityEngine.Object value)
    {
        Undo.RecordObject(target, "Wire painting component");
        var data = new SerializedObject(target);
        var property = data.FindProperty(field);
        if (property == null) throw new InvalidOperationException("Awaiting updated source field: " + field);
        property.objectReferenceValue = value;
        data.ApplyModifiedProperties();
    }
}
