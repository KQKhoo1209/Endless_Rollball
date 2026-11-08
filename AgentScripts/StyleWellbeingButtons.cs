using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StyleWellbeingButtons
{
    public static string ImportSource()
    {
        AssetDatabase.ImportAsset("Assets/Scripts/UI/PainStressCheckUI.cs", ImportAssetOptions.ForceUpdate);
        return "Imported the updated wellbeing controller for scene wiring.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before authoring buttons.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var page = all.Single(t => t.name == "PainStressCheckPage");
        var sensor = all.Single(t => t.name == "SensorCalibrationPage");
        var reference = sensor.Find("DownPanel/BackButton").GetComponent<UnityEngine.UI.Button>();
        var referenceImage = reference.GetComponent<UnityEngine.UI.Image>();
        var referenceLabel = reference.GetComponentInChildren<TMP_Text>();
        var owner = page.GetComponents<Component>().Single(c => c != null && c.GetType().Name == "PainStressCheckUI");
        var data = new SerializedObject(owner);
        if (data.FindProperty("buttonStyleReference") == null)
            throw new InvalidOperationException("The updated controller is awaiting its normal import.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Match wellbeing buttons and enable zero defaults");
        try
        {
            foreach (var button in page.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var image = button.GetComponent<UnityEngine.UI.Image>();
                var label = button.GetComponentInChildren<TMP_Text>();
                Undo.RecordObjects(new UnityEngine.Object[] { button, image, label }, "Match existing menu button style");
                image.sprite = referenceImage.sprite;
                image.type = referenceImage.type;
                image.pixelsPerUnitMultiplier = referenceImage.pixelsPerUnitMultiplier;
                image.color = referenceImage.color;
                label.color = referenceLabel.color;
                button.colors = reference.colors;
                button.transition = reference.transition;
            }
            var normal = (UnityEngine.UI.Button)data.FindProperty("normalButton").objectReferenceValue;
            var reduced = (UnityEngine.UI.Button)data.FindProperty("reducedButton").objectReferenceValue;
            var next = (UnityEngine.UI.Button)data.FindProperty("continueButton").objectReferenceValue
                ?? page.Find("DownPanel/ContinueButton").GetComponent<UnityEngine.UI.Button>();
            var rest = page.GetComponentsInChildren<UnityEngine.UI.Button>(true).First(b =>
                Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == "RestAndReturn"));
            var selectedNormal = Indicator(normal.transform, referenceLabel.color);
            var selectedReduced = Indicator(reduced.transform, referenceLabel.color);
            selectedNormal.SetActive(true);
            selectedReduced.SetActive(false);
            normal.interactable = true;
            next.interactable = true;
            Undo.RecordObject(owner, "Preserve shared button style at runtime");
            data.FindProperty("buttonStyleReference").objectReferenceValue = reference;
            data.FindProperty("continueButton").objectReferenceValue = next;
            data.FindProperty("restBackground").objectReferenceValue = rest.GetComponent<UnityEngine.UI.Image>();
            data.FindProperty("restLabel").objectReferenceValue = rest.GetComponentInChildren<TMP_Text>();
            data.FindProperty("normalSelectedIndicator").objectReferenceValue = selectedNormal;
            data.FindProperty("reducedSelectedIndicator").objectReferenceValue = selectedReduced;
            data.ApplyModifiedProperties();
            rest.GetComponentInChildren<TMP_Text>().text = "Rest and Return";
            foreach (string field in new[] { "painValue", "stressValue" })
            {
                var label = (TMP_Text)data.FindProperty(field).objectReferenceValue;
                Undo.RecordObject(label, "Display zero default");
                label.text = "0 / 10";
            }
            var support = (TMP_Text)data.FindProperty("supportMessage").objectReferenceValue;
            Undo.RecordObject(support, "Explain default readiness");
            support.text = "Continue when you feel comfortable. You can rest instead.";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved matching menu-button colours, feedback selection underlines, and enabled Continue for default 0/0 ratings.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static GameObject Indicator(Transform parent, Color tint)
    {
        var found = parent.Find("SelectionUnderline");
        GameObject go;
        if (found != null) go = found.gameObject;
        else
        {
            go = new GameObject("SelectionUnderline", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            Undo.RegisterCreatedObjectUndo(go, "Create feedback selection indicator");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
        }
        var rect = (RectTransform)go.transform;
        var image = go.GetComponent<UnityEngine.UI.Image>();
        Undo.RecordObjects(new UnityEngine.Object[] { rect, image, go }, "Configure feedback selection indicator");
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(.5f, 0);
        rect.anchoredPosition = new Vector2(0, 7);
        rect.sizeDelta = new Vector2(-28, 3);
        image.color = tint;
        image.raycastTarget = false;
        return go;
    }
}
