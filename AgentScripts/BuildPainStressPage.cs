using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

public static class BuildPainStressPage
{
    private static Color Ink = new Color(.16f, .22f, .22f);
    private static Color Cream = new Color(1f, .97f, .87f);
    private static Color Teal = new Color(.30f, .57f, .52f);
    private static TMP_Text textTemplate;
    private static UnityEngine.UI.Image borderTemplate;
    private static UnityEngine.UI.Image buttonTemplate;
    private static Component owner;
    private static Type sliderType;

    public static string ImportSources()
    {
        foreach (string path in new[] { "Assets/Scripts/UI/PainStressCheckUI.cs",
            "Assets/Scripts/UI/ExplicitRatingSlider.cs", "Assets/Scripts/Session/GameSessionSettings.cs",
            "Assets/Scripts/UI/SessionFlowUI.cs" })
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return "Imported the wellbeing UI source assets for scene authoring.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainMenuScene.unity" || Application.isPlaying)
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before creating the page.");
        Type ownerType = FindType("PainStressCheckUI");
        sliderType = FindType("ExplicitRatingSlider");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        Transform sensor = all.Single(t => t.name == "SensorCalibrationPage");
        Transform menu = all.Single(t => t.name == "MainMenuPage");
        Transform guide = all.Single(t => t.name == "GuidancePage");
        textTemplate = sensor.Find("PageTitle").GetComponent<TMP_Text>();
        borderTemplate = sensor.Find("WearGuidePanel").GetComponent<UnityEngine.UI.Image>();
        buttonTemplate = sensor.Find("DownPanel/RetryConnectionButton").GetComponent<UnityEngine.UI.Image>();
        if (sensor.parent.Find("PainStressCheckPage") != null)
            throw new InvalidOperationException("PainStressCheckPage already exists; edit it instead of recreating it.");

        Directory.CreateDirectory("../.tmp/pain-stress-before");
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        File.Copy(scene.path, "../.tmp/pain-stress-before/MainMenuScene.unity", true);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Pain and Stress Check page");
        try
        {
            RectTransform page = Rect(sensor.parent, "PainStressCheckPage", 0, 0, 1920, 1080);
            page.gameObject.SetActive(false);
            page.anchorMin = page.anchorMax = page.pivot = new Vector2(.5f, .5f);
            page.anchoredPosition = Vector2.zero;
            var background = Undo.AddComponent<UnityEngine.UI.Image>(page.gameObject);
            var sensorBackground = sensor.GetComponent<UnityEngine.UI.Image>();
            background.sprite = sensorBackground.sprite;
            background.type = sensorBackground.type;
            background.color = Color.white;
            background.raycastTarget = true;
            owner = Undo.AddComponent(page.gameObject, ownerType);
            Text(page, "PageTitle", "Pain & Stress Check", 70, 32, 1780, 66, 44, TextAlignmentOptions.Left, true);
            Text(page, "Introduction", "How are you feeling today? Choose a number for your current pain and stress.", 74, 110, 1770, 46, 28);

            UnityEngine.UI.Slider painSlider, stressSlider;
            TMP_Text painValue, stressValue;
            RatingCard(page, "PainPanel", "Pain", "How much pain do you feel now?", "No pain", "Severe pain", 70,
                "SetPain", "DecreasePain", "IncreasePain", out painSlider, out painValue);
            RatingCard(page, "StressPanel", "Stress", "How stressed do you feel now?", "Calm", "Very stressed", 980,
                "SetStress", "DecreaseStress", "IncreaseStress", out stressSlider, out stressValue);

            RectTransform feedback = Panel(page, "FeedbackPanel", 70, 645, 1780, 190);
            Text(feedback, "Title", "Choose your feedback", 35, 16, 1710, 42, 29, TextAlignmentOptions.Left, true);
            var normal = Button(feedback, "NormalFeedbackButton", "Normal Feedback", 35, 77, 380, 72, "SelectNormal", true);
            var reduced = Button(feedback, "ReducedFeedbackButton", "Reduced Feedback", 445, 77, 380, 72, "SelectReduced");
            TMP_Text feedbackMessage = Text(feedback, "FeedbackDescription", "Normal Feedback selected. Scores and route progress will be shown.",
                885, 69, 850, 90, 25);
            TMP_Text support = Text(page, "SupportMessage", "Please answer both ratings. Take your time; you can rest instead.",
                75, 855, 1770, 66, 28, TextAlignmentOptions.Center);
            Text(page, "PrivacyNote", "Your ratings are used only for this session and do not affect your score.",
                75, 925, 1770, 32, 21, TextAlignmentOptions.Center);
            var back = Button(page, "BackButton", "Back", 70, 980, 240, 72, "Back");
            var rest = Button(page, "RestAndReturnButton", "Rest and Return", 705, 980, 510, 72, "RestAndReturn");
            var next = Button(page, "ContinueButton", "Continue", 1470, 980, 380, 72, "Continue", true);
            next.interactable = true;

            var serialized = new SerializedObject(owner);
            Set(serialized, "painSlider", painSlider); Set(serialized, "stressSlider", stressSlider);
            Set(serialized, "painValue", painValue); Set(serialized, "stressValue", stressValue);
            Set(serialized, "supportMessage", support); Set(serialized, "feedbackMessage", feedbackMessage);
            Set(serialized, "normalButton", normal); Set(serialized, "reducedButton", reduced);
            Set(serialized, "continueButton", next); Set(serialized, "restBackground", rest.GetComponent<UnityEngine.UI.Image>());
            Set(serialized, "restLabel", rest.GetComponentInChildren<TMP_Text>());
            Set(serialized, "sensorPage", sensor.gameObject); Set(serialized, "guidePage", guide.gameObject);
            Set(serialized, "mainMenuPage", menu.gameObject);
            serialized.ApplyModifiedProperties();

            // Keep a direct entry for inspecting this first version without hardware.
            Button(menu, "PainStressCheckButton", "Pain & Stress Check", 160, 810, 360, 74, "OpenFresh");
            Component sensorOwner = sensor.GetComponents<Component>().First(c => c != null && c.GetType().Name == "SensorCalibrationUI");
            var readyField = sensorOwner.GetType().GetField("readyRequested", BindingFlags.NonPublic | BindingFlags.Instance);
            var ready = (UnityEvent)readyField.GetValue(sensorOwner);
            Undo.RecordObject(sensorOwner, "Open wellbeing check after sensor setup");
            for (int i = ready.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                var target = ready.GetPersistentTarget(i);
                if ((target == sensor.gameObject || target == guide.gameObject) && ready.GetPersistentMethodName(i) == "SetActive")
                    UnityEventTools.RemovePersistentListener(ready, i);
            }
            UnityEventTools.AddPersistentListener(ready, Action("OpenFresh"));
            EditorUtility.SetDirty(sensorOwner);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved PainStressCheckPage, explicit 0-10 ratings, feedback options, rest/back/continue navigation, and sensor-to-check-to-guide routing.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name))
        .FirstOrDefault(t => t != null) ?? throw new InvalidOperationException("Source is awaiting normal import: " + name);

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create wellbeing UI");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    private static UnityEngine.UI.Image Image(RectTransform rt, Color tint, Sprite sprite = null)
    {
        var image = Undo.AddComponent<UnityEngine.UI.Image>(rt.gameObject);
        image.color = tint;
        image.sprite = sprite;
        image.raycastTarget = false;
        if (sprite != null) image.type = UnityEngine.UI.Image.Type.Sliced;
        return image;
    }

    private static RectTransform Panel(Transform parent, string name, float x, float y, float w, float h)
    {
        var rt = Rect(parent, name, x, y, w, h);
        Image(rt, new Color(1, .98f, .91f, .94f));
        var border = Image(Rect(rt, "Border", 0, 0, w, h), Color.white, borderTemplate.sprite);
        border.pixelsPerUnitMultiplier = borderTemplate.pixelsPerUnitMultiplier;
        return rt;
    }

    private static TMP_Text Text(Transform parent, string name, string value, float x, float y,
        float w, float h, float size, TextAlignmentOptions alignment = TextAlignmentOptions.Left, bool bold = false)
    {
        var label = Undo.AddComponent<TextMeshProUGUI>(Rect(parent, name, x, y, w, h).gameObject);
        label.font = textTemplate.font;
        label.fontSharedMaterial = textTemplate.fontSharedMaterial;
        label.color = Ink;
        label.fontSize = size;
        label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }

    private static UnityAction Action(string method) => (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), owner, method);
    private static UnityEngine.UI.Button Button(Transform parent, string name, string text, float x, float y,
        float w, float h, string method, bool primary = false)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var image = Image(rt, primary ? Teal : Cream, buttonTemplate.sprite);
        image.pixelsPerUnitMultiplier = buttonTemplate.pixelsPerUnitMultiplier;
        image.raycastTarget = true;
        var button = Undo.AddComponent<UnityEngine.UI.Button>(rt.gameObject);
        button.targetGraphic = image;
        var label = Text(rt, "Label", text, 10, 6, w - 20, h - 12, w < 100 ? 38 : 26, TextAlignmentOptions.Center, true);
        label.color = primary ? Color.white : Ink;
        UnityEventTools.AddPersistentListener(button.onClick, Action(method));
        return button;
    }

    private static void RatingCard(Transform page, string name, string title, string question,
        string low, string high, float x, string changed, string minus, string plus,
        out UnityEngine.UI.Slider slider, out TMP_Text value)
    {
        var card = Panel(page, name, x, 190, 870, 430);
        Text(card, "Title", title, 35, 20, 800, 46, 35, TextAlignmentOptions.Left, true);
        Text(card, "Question", question, 35, 78, 800, 46, 28);
        value = Text(card, "SelectedValue", "0 / 10", 35, 139, 800, 86, 62, TextAlignmentOptions.Center, true);
        Button(card, "DecreaseButton", "-", 35, 240, 72, 72, minus);
        Button(card, "IncreaseButton", "+", 763, 240, 72, 72, plus);
        var rt = Rect(card, "RatingSlider", 130, 251, 610, 50);
        var target = Image(rt, Color.clear); target.raycastTarget = true;
        Image(Rect(rt, "Track", 10, 18, 590, 14), new Color(.72f, .75f, .69f));
        var fillArea = Rect(rt, "FillArea", 10, 18, 590, 14);
        var fill = Rect(fillArea, "Fill", 0, 0, 590, 14);
        Image(fill, Teal);
        var handleArea = Rect(rt, "HandleArea", 10, 0, 590, 50);
        var handle = Rect(handleArea, "Handle", 0, 0, 34, 50);
        handle.pivot = new Vector2(.5f, .5f);
        var handleImage = Image(handle, Teal, buttonTemplate.sprite);
        slider = (UnityEngine.UI.Slider)Undo.AddComponent(rt.gameObject, sliderType);
        slider.targetGraphic = handleImage;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.minValue = 0; slider.maxValue = 10; slider.wholeNumbers = true;
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        slider.SetValueWithoutNotify(0);
        UnityEventTools.AddPersistentListener(slider.onValueChanged,
            (UnityAction<float>)Delegate.CreateDelegate(typeof(UnityAction<float>), owner, changed));
        for (int i = 0; i <= 10; i++)
            Text(card, "Tick" + i, i.ToString(), 125 + i * 59, 309, 40, 30, 21, TextAlignmentOptions.Center);
        Text(card, "LowEndpoint", "0: " + low, 35, 350, 390, 34, 24);
        Text(card, "HighEndpoint", "10: " + high, 445, 350, 390, 34, 24, TextAlignmentOptions.Right);
        Text(card, "InputHint", "Choose a number, drag the slider, or use - / +.", 35, 391, 800, 27, 21, TextAlignmentOptions.Center);
    }

    private static void Set(SerializedObject owner, string name, UnityEngine.Object value)
    {
        var property = owner.FindProperty(name);
        if (property == null) throw new InvalidOperationException("Missing UI field: " + name);
        property.objectReferenceValue = value;
    }
}
