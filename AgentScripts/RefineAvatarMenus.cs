using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

public static class RefineAvatarMenus
{
    [Serializable] private class Visibility { public string[] names; public bool[] states; public bool canvasActive; }
    private static Transform CanvasRoot()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode.");
        return scene.GetRootGameObjects().Single(r => r.name == "Canvas").transform;
    }
    private static string Show(string page)
    {
        var canvas = CanvasRoot();
        if (string.IsNullOrEmpty(SessionState.GetString("AvatarMenuVisibility", "")))
        {
            var children = canvas.Cast<Transform>().ToArray();
            SessionState.SetString("AvatarMenuVisibility", JsonUtility.ToJson(new Visibility {
                names = children.Select(t => t.name).ToArray(), states = children.Select(t => t.gameObject.activeSelf).ToArray(),
                canvasActive = canvas.gameObject.activeSelf }));
        }
        canvas.gameObject.SetActive(true);
        foreach (Transform t in canvas) t.gameObject.SetActive(t.name == page);
        UnityEngine.Canvas.ForceUpdateCanvases();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        return "Temporarily showing " + page + " in Edit Mode for pre-edit inspection.";
    }
    public static string ShowSelection() => Show("AvatarSelectionPage");
    public static string ShowCustomization() => Show("AvatarCustomizationPage");
    public static string ShowReference() => Show("SensorConnectionPage");
    public static string RestoreVisibility()
    {
        var json = SessionState.GetString("AvatarMenuVisibility", "");
        if (string.IsNullOrEmpty(json)) return "No temporary page visibility to restore.";
        var canvas = CanvasRoot(); var data = JsonUtility.FromJson<Visibility>(json);
        for (int i = 0; i < data.names.Length; i++)
        { var child = canvas.Find(data.names[i]); if (child != null) child.gameObject.SetActive(data.states[i]); }
        canvas.gameObject.SetActive(data.canvasActive);
        SessionState.EraseString("AvatarMenuVisibility");
        return "Restored original Editor page visibility.";
    }

    public static string ImportSources()
    {
        AssetDatabase.ImportAsset("Assets/Scripts/Avatars/AvatarMenuUI.cs");
        AssetDatabase.ImportAsset("Assets/Scripts/Avatars/AvatarPreview.cs");
        return "Imported avatar UI sources for the additional scene references.";
    }

    private static TMP_Text textStyle;
    private static UnityEngine.UI.Button buttonStyle;
    private static UnityEngine.UI.Image panelStyle;

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var t = parent.Find(name) as RectTransform;
        if (t == null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Refine avatar UI");
            go.layer = parent.gameObject.layer;
            t = (RectTransform)go.transform; t.SetParent(parent, false);
        }
        Place(t, x, y, w, h); return t;
    }
    private static void Place(RectTransform t, float x, float y, float w, float h)
    {
        Undo.RecordObject(t, "Align avatar menu layout");
        t.anchorMin = t.anchorMax = t.pivot = new Vector2(0, 1);
        t.anchoredPosition = new Vector2(x, -y); t.sizeDelta = new Vector2(w, h);
        t.localScale = Vector3.one; t.localRotation = Quaternion.identity;
    }
    private static void Move(Transform t, Transform parent, float x, float y, float w, float h)
    {
        if (t.parent != parent) Undo.SetTransformParent(t, parent, "Group avatar panel contents");
        Place((RectTransform)t, x, y, w, h);
    }
    private static void ImageStyle(UnityEngine.UI.Image image, UnityEngine.UI.Image reference)
    {
        Undo.RecordObject(image, "Match existing menu artwork");
        image.sprite = reference.sprite; image.type = reference.type;
        image.color = reference.color; image.material = reference.material;
        image.pixelsPerUnitMultiplier = reference.pixelsPerUnitMultiplier;
    }
    private static RectTransform Panel(Transform page, string name, float x, float y, float w, float h)
    {
        var rect = Rect(page, name, x, y, w, h);
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
        ImageStyle(image, panelStyle); image.raycastTarget = false;
        rect.SetAsFirstSibling(); return rect;
    }
    private static void StyleText(TMP_Text label, float size, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        Undo.RecordObject(label, "Match menu typography");
        label.font = textStyle.font; label.fontSharedMaterial = textStyle.fontSharedMaterial;
        label.color = textStyle.color; label.fontSize = size;
        label.fontStyle = FontStyles.Normal; label.alignment = alignment;
        label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.margin = Vector4.zero; label.raycastTarget = false;
    }
    private static TMP_Text Text(Transform parent, string name, string value, float x, float y, float w, float h,
        float size, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var rect = Rect(parent, name, x, y, w, h);
        var label = rect.GetComponent<TMP_Text>() ?? Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        StyleText(label, size, align); label.text = value; return label;
    }
    private static void StyleButton(UnityEngine.UI.Button button, float labelSize = 35f)
    {
        Undo.RecordObject(button, "Match existing menu button states");
        ImageStyle(button.GetComponent<UnityEngine.UI.Image>(), buttonStyle.GetComponent<UnityEngine.UI.Image>());
        button.transition = buttonStyle.transition; button.colors = buttonStyle.colors;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        StyleText(label, labelSize, TextAlignmentOptions.Center);
        // Keep the standard button dimensions; longer action names fit within the same control.
        label.enableAutoSizing = true; label.fontSizeMin = 26f; label.fontSizeMax = labelSize;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Place(label.rectTransform, 18, 8, ((RectTransform)button.transform).sizeDelta.x - 36,
            ((RectTransform)button.transform).sizeDelta.y - 16);
    }
    private static GameObject Underline(Transform parent, string name)
    {
        var rect = Rect(parent, name, 24, 91, ((RectTransform)parent).sizeDelta.x - 48, 3);
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
        image.color = textStyle.color; image.raycastTarget = false;
        return rect.gameObject;
    }
    private static void Stroke(Transform parent, string name, float x, float y, float w, float h, float angle = 0)
    {
        var rect = Rect(parent, name, x, y, w, h);
        rect.localRotation = Quaternion.Euler(0, 0, angle);
        var image = rect.GetComponent<UnityEngine.UI.Image>() ?? Undo.AddComponent<UnityEngine.UI.Image>(rect.gameObject);
        image.color = textStyle.color; image.raycastTarget = false;
    }
    private static GameObject CardIcon(Transform parent, bool locked)
    {
        var rect = Rect(parent, locked ? "LockedIndicator" : "SelectedIndicator", 30, 32, 36, 36);
        if (locked)
        {
            Stroke(rect, "Body", 1, 14, 28, 21); Stroke(rect, "Left", 5, 3, 4, 14);
            Stroke(rect, "Top", 5, 3, 20, 4); Stroke(rect, "Right", 21, 3, 4, 14);
        }
        else
        { Stroke(rect, "Short", 0, 17, 15, 5, -45); Stroke(rect, "Long", 9, 27, 28, 5, 45); }
        return rect.gameObject;
    }
    private static void Set(SerializedObject data, string field, UnityEngine.Object value)
        => data.FindProperty(field).objectReferenceValue = value;
    private static void Preview(Transform page, RectTransform panel)
    {
        var viewport = Rect(panel, "PreviewViewport", 95, 95, 510, 510);
        var preview = page.Find("CharacterPreview") ?? viewport.Find("CharacterPreview");
        Move(preview, viewport, 0, 0, 510, 510);
        var aspect = preview.GetComponent<UnityEngine.UI.AspectRatioFitter>() ?? Undo.AddComponent<UnityEngine.UI.AspectRatioFitter>(preview.gameObject);
        aspect.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 1f;
        var behaviour = preview.GetComponents<MonoBehaviour>().Single(c => c != null && c.GetType().Name == "AvatarPreview");
        var data = new SerializedObject(behaviour);
        data.FindProperty("previewBackground").colorValue = new Color(.98f, .96f, .89f, 0f);
        data.ApplyModifiedPropertiesWithoutUndo();
        var image = preview.GetComponent<UnityEngine.UI.RawImage>(); image.raycastTarget = false;
    }
    private static TMP_Text Heading(Transform page, string value)
    {
        var label = page.Find("Heading").GetComponent<TMP_Text>();
        Place(label.rectTransform, 130, 45, 1150, 80); StyleText(label, 55); label.text = value; return label;
    }
    private static void Balance(Transform page)
    {
        var label = page.Find("Balance").GetComponent<TMP_Text>();
        Place(label.rectTransform, 1390, 65, 440, 50); StyleText(label, 30, TextAlignmentOptions.MidlineRight);
    }
    private static void FooterButton(Transform page, string name, string value, bool right)
    {
        var button = page.Find(name).GetComponent<UnityEngine.UI.Button>();
        Place((RectTransform)button.transform, right ? 1570 : 50, 940, 300, 100);
        StyleButton(button); button.GetComponentInChildren<TMP_Text>(true).text = value;
    }
    private static void Status(Transform page, string value)
    {
        var label = page.Find("Status").GetComponent<TMP_Text>();
        Place(label.rectTransform, 390, 940, 1090, 100);
        StyleText(label, 25, TextAlignmentOptions.Center); label.text = value;
    }

    private static void SettingsAppearance(Transform canvas)
    {
        var settings = canvas.Find("SettingPage");
        var background = settings.GetComponent<UnityEngine.UI.Image>();
        var referenceButton = settings.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>();
        var colour = settings.Find("PageTitle").GetComponent<TMP_Text>().color;
        foreach (string name in new[] { "AvatarSelectionPage", "AvatarCustomizationPage" })
        {
            var page = canvas.Find(name);
            ImageStyle(page.GetComponent<UnityEngine.UI.Image>(), background);
            foreach (var label in page.GetComponentsInChildren<TMP_Text>(true))
            { Undo.RecordObject(label, "Match Settings text contrast"); label.color = colour; }
            foreach (var image in page.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (image.transform == page) continue;
                Undo.RecordObject(image, "Match Settings control contrast");
                image.color = referenceButton.GetComponent<UnityEngine.UI.Image>().color;
            }
            foreach (var button in page.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            { Undo.RecordObject(button, "Match Settings button states"); button.colors = referenceButton.colors; }
            // Keep the later global Main Menu button treatment when reauthoring these pages.
            var commonStyle = Resources.Load<ScriptableObject>("UI/MainMenuButtonStyle");
            if (commonStyle != null)
                foreach (var button in page.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                    commonStyle.GetType().GetMethod("Apply").Invoke(commonStyle, new object[] { button });
        }
    }

    public static string ApplySettingsAppearance()
    {
        var canvas = CanvasRoot();
        SettingsAppearance(canvas);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
        return "Saved both avatar pages with SettingPage's translucent dark overlay, white labels and control colours. Removed landscape background; preserved layout, button dimensions and events. No Play Mode run.";
    }

    public static string Apply()
    {
        RestoreVisibility();
        var canvas = CanvasRoot(); var scene = canvas.gameObject.scene;
        var reference = canvas.Find("SensorConnectionPage");
        buttonStyle = reference.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>();
        textStyle = buttonStyle.GetComponentInChildren<TMP_Text>(true);
        panelStyle = reference.Find("WearGuidePanel").GetComponent<UnityEngine.UI.Image>();
        var controller = canvas.GetComponents<MonoBehaviour>().Single(c => c != null && c.GetType().Name == "AvatarMenuUI");
        var data = new SerializedObject(controller);
        if (data.FindProperty("draftStatus") == null) throw new InvalidOperationException("Avatar UI source is still importing.");
        var select = canvas.Find("AvatarSelectionPage"); var custom = canvas.Find("AvatarCustomizationPage");
        Directory.CreateDirectory("../.tmp/avatar-ui-refinement-before");
        // Snapshot the current in-memory user-authored scene before making targeted edits.
        if (!File.Exists("../.tmp/avatar-ui-refinement-before/MainMenuScene.unity"))
        {
            EditorSceneManager.SaveScene(scene);
            File.Copy(scene.path, "../.tmp/avatar-ui-refinement-before/MainMenuScene.unity");
        }
        Undo.RegisterFullObjectHierarchyUndo(select.gameObject, "Refine avatar selection");
        Undo.RegisterFullObjectHierarchyUndo(custom.gameObject, "Refine avatar customization");
        Undo.RecordObject(controller, "Wire avatar UI state labels");
        foreach (var page in new[] { select, custom })
        {
            ImageStyle(page.GetComponent<UnityEngine.UI.Image>(), reference.GetComponent<UnityEngine.UI.Image>());
            var pageRect = (RectTransform)page;
            pageRect.anchorMin = pageRect.anchorMax = pageRect.pivot = new Vector2(.5f, .5f);
            pageRect.anchoredPosition = Vector2.zero; pageRect.sizeDelta = new Vector2(1920, 1080);
            Balance(page);
        }
        Heading(select, "Avatar Selection"); Heading(custom, "Avatar Customization");
        var left = Panel(select, "PreviewPanel", 50, 180, 700, 720);
        var right = Panel(select, "ChoicesPanel", 780, 180, 1090, 720);
        var name = select.Find("AvatarName").GetComponent<TMP_Text>();
        Move(name.transform, left, 35, 25, 630, 55); StyleText(name, 36, TextAlignmentOptions.Center);
        Preview(select, left);
        Text(left, "CompanionDescription", "Your companion for session feedback.", 55, 610, 590, 70, 28, TextAlignmentOptions.Center);
        Text(right, "PanelTitle", "Choose a companion", 50, 25, 990, 55, 36, TextAlignmentOptions.Center);
        var cards = data.FindProperty("cards");
        for (int i = 0; i < cards.arraySize; i++)
        {
            var card = cards.GetArrayElementAtIndex(i);
            var button = (UnityEngine.UI.Button)card.FindPropertyRelative("button").objectReferenceValue;
            Move(button.transform, right, 55, 110 + i * 190, 980, 100); StyleButton(button);
            var label = (TMP_Text)card.FindPropertyRelative("label").objectReferenceValue;
            Place(label.rectTransform, 80, 10, 820, 80);
            string id = card.FindPropertyRelative("avatarId").stringValue;
            bool cow = id == "cow";
            label.text = cow ? "Charging Cow" : id == "whale" ? "Tidal Whale" : "Ember Phoenix";
            var status = Text(right, id + "Status", cow ? "Selected" : "Locked - Complete " + (id == "whale" ? "Fast-Paced" : "Standard") + " Easy once",
                55, 215 + i * 190, 980, 55, 27, TextAlignmentOptions.Center);
            card.FindPropertyRelative("status").objectReferenceValue = status;
            var selected = CardIcon(button.transform, false); selected.SetActive(cow);
            var locked = CardIcon(button.transform, true); locked.SetActive(!cow);
            card.FindPropertyRelative("selectedIndicator").objectReferenceValue = selected;
            card.FindPropertyRelative("lockedIndicator").objectReferenceValue = locked;
        }
        FooterButton(select, "BackButton", "Back", false);
        FooterButton(select, "CustomizeButton", "Customize Avatar", true);
        Status(select, "Choose a companion to share feedback after your session.");

        var customLeft = Panel(custom, "PreviewPanel", 50, 180, 700, 720);
        var customRight = Panel(custom, "ChoicesPanel", 780, 180, 1090, 720);
        Set(data, "customizationAvatarName", Text(customLeft, "AvatarName", "Charging Cow", 35, 25, 630, 55, 36, TextAlignmentOptions.Center));
        Preview(custom, customLeft);
        var reaction = custom.Find("PreviewReaction").GetComponent<UnityEngine.UI.Button>();
        Move(reaction.transform, customLeft, 175, 605, 350, 100); StyleButton(reaction);
        Text(customRight, "PanelTitle", "Choose your style", 50, 25, 990, 55, 36, TextAlignmentOptions.Center);
        var rows = data.FindProperty("rows");
        for (int i = 0; i < rows.arraySize; i++)
        {
            var row = rows.GetArrayElementAtIndex(i);
            var root = ((GameObject)row.FindPropertyRelative("root").objectReferenceValue).transform;
            Move(root, customRight, 35, 100 + i * 190, 1020, 180);
            var slot = root.Find("Slot").GetComponent<TMP_Text>();
            StyleText(slot, 28); Place(slot.rectTransform, 0, 0, 1020, 35);
            slot.text = i == 0 ? "Appearance" : i == 1 ? "Accessory" : "Effect";
            foreach (string option in new[] { "Default", "Option" })
            {
                bool isDefault = option == "Default";
                var button = root.Find(option).GetComponent<UnityEngine.UI.Button>();
                Place((RectTransform)button.transform, isDefault ? 0 : 480, 42, isDefault ? 450 : 540, 100);
                StyleButton(button);
                var label = button.GetComponentInChildren<TMP_Text>(true);
                Place(label.rectTransform, 18, 8, isDefault ? 414 : 504, 45);
                label.fontSizeMax = 35; label.fontSizeMin = 26;
                label.text = isDefault ? i == 0 ? "Default Appearance" : "None" : i == 0 ? "Midnight Coat" : i == 1 ? "Cowboy Hat" : "Rebirth Aura";
                var action = Text(button.transform, "Action", isDefault ? "Free - Previewing" : "Buy - " + (i == 0 ? "40" : i == 1 ? "60" : "180") + " coins",
                    18, 52, isDefault ? 414 : 504, 32, 24, TextAlignmentOptions.Center);
                var state = Text(root, option + "State", isDefault ? "Equipped" : "", isDefault ? 0 : 480, 146, isDefault ? 450 : 540, 34, 22, TextAlignmentOptions.Center);
                var indicator = Underline(button.transform, "PreviewIndicator"); indicator.SetActive(isDefault);
                row.FindPropertyRelative(isDefault ? "defaultAction" : "optionAction").objectReferenceValue = action;
                row.FindPropertyRelative(isDefault ? "defaultState" : "optionState").objectReferenceValue = state;
                row.FindPropertyRelative(isDefault ? "defaultIndicator" : "optionIndicator").objectReferenceValue = indicator;
            }
            root.gameObject.SetActive(i < 2);
        }
        Text(customRight, "PurchaseHint", "Purchases save immediately. Save Appearance keeps your preview changes.", 35, 675, 1020, 35, 22, TextAlignmentOptions.Center);
        Set(data, "draftStatus", Text(custom, "DraftStatus", "Appearance saved", 780, 900, 1090, 35, 24, TextAlignmentOptions.MidlineRight));
        FooterButton(custom, "BackButton", "Back to Selection", false);
        FooterButton(custom, "SaveButton", "Save Appearance", true);
        Status(custom, "Choose items to preview, then Save Appearance.");
        data.ApplyModifiedProperties();
        SettingsAppearance(canvas);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return "Saved refined existing avatar pages: Settings overlay/colours, framed panels, 300x100 footer buttons, square previews, 100-high choice controls, separate ownership/preview states and persistent indicators. Preserved existing events, catalog and repository. No Play Mode or acceptance checks run.";
    }
}
