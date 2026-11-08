using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StyleWellbeingSliders
{
    public static string SetDefaultValues()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before changing rating defaults.");
        var page = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Single(t => t.name == "PainStressCheckPage");
        var owner = page.GetComponents<Component>().Single(c => c != null && c.GetType().Name == "PainStressCheckUI");
        var data = new SerializedObject(owner);
        foreach (string field in new[] { "painValue", "stressValue" })
        {
            var text = (TMPro.TMP_Text)data.FindProperty(field).objectReferenceValue;
            Undo.RecordObject(text, "Show zero as default rating");
            text.text = "0 / 10";
        }
        foreach (string field in new[] { "painSlider", "stressSlider" })
        {
            var slider = (UnityEngine.UI.Slider)data.FindProperty(field).objectReferenceValue;
            Undo.RecordObject(slider, "Start rating slider at zero");
            slider.SetValueWithoutNotify(0);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Saved 0 / 10 as both displayed rating defaults.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/MainMenuScene.unity")
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode before changing slider visuals.");
        var page = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Single(t => t.name == "PainStressCheckPage");
        var owner = page.GetComponents<Component>().Single(c => c != null && c.GetType().Name == "PainStressCheckUI");
        var data = new SerializedObject(owner);
        var backgroundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        var fillSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var knobSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (backgroundSprite == null || fillSprite == null || knobSprite == null)
            throw new InvalidOperationException("Unity's standard slider resources are unavailable.");
        if (scene.isDirty) EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory("../.tmp/wellbeing-sliders-before");
        File.Copy(scene.path, "../.tmp/wellbeing-sliders-before/MainMenuScene.unity", true);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Use standard wellbeing slider visuals");
        try
        {
            foreach (string name in new[] { "painSlider", "stressSlider" })
            {
                var slider = (UnityEngine.UI.Slider)data.FindProperty(name).objectReferenceValue;
                if (slider == null || slider.fillRect == null || slider.handleRect == null)
                    throw new InvalidOperationException("The existing rating slider is missing a component: " + name);
                Transform track = slider.transform.Find("Track") ?? slider.transform.Find("Background");
                if (track == null) throw new InvalidOperationException("Rating slider track is missing.");
                var fill = slider.fillRect.GetComponent<UnityEngine.UI.Image>();
                var handle = slider.handleRect.GetComponent<UnityEngine.UI.Image>();
                var background = track.GetComponent<UnityEngine.UI.Image>();
                Undo.RecordObjects(new UnityEngine.Object[] { slider, background, fill, handle }, "Apply standard slider skin");
                background.sprite = backgroundSprite;
                background.type = UnityEngine.UI.Image.Type.Sliced;
                background.color = new Color(.72f, .75f, .71f);
                background.pixelsPerUnitMultiplier = 1;
                background.raycastTarget = false;
                fill.sprite = fillSprite;
                fill.type = UnityEngine.UI.Image.Type.Sliced;
                fill.color = new Color(.30f, .57f, .52f);
                fill.pixelsPerUnitMultiplier = 1;
                fill.raycastTarget = false;
                handle.sprite = knobSprite;
                handle.type = UnityEngine.UI.Image.Type.Simple;
                handle.preserveAspect = true;
                handle.color = Color.white;
                handle.pixelsPerUnitMultiplier = 1;
                handle.raycastTarget = true;
                SetRect((RectTransform)track, new Vector2(0, .35f), new Vector2(1, .65f), Vector2.zero, Vector2.zero);
                SetRect((RectTransform)slider.fillRect.parent, new Vector2(0, .35f), new Vector2(1, .65f), Vector2.zero, new Vector2(-44, 0));
                Undo.RecordObject(slider.fillRect, "Fit slider fill");
                slider.fillRect.sizeDelta = Vector2.zero;
                slider.fillRect.anchoredPosition = Vector2.zero;
                SetRect((RectTransform)slider.handleRect.parent, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-44, 0));
                Undo.RecordObject(slider.handleRect, "Use round draggable handle");
                slider.handleRect.sizeDelta = new Vector2(44, -6);
                slider.handleRect.anchoredPosition = Vector2.zero;
                slider.handleRect.pivot = new Vector2(.5f, .5f);
                slider.targetGraphic = handle;
                slider.minValue = 0;
                slider.maxValue = 10;
                slider.wholeNumbers = true;
                EditorUtility.SetDirty(slider);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(group);
            return "Saved both rating sliders with Unity's standard track, fill and round handle; existing callbacks and explicit-zero confirmation were preserved.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        Undo.RecordObject(rect, "Position slider parts");
        rect.anchorMin = min; rect.anchorMax = max;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
