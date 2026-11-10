using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SetGuidanceHandRotation
{
    private const string ControllerPath = "Assets/EndlessRollballAssets/UI Asset/Animation/BallSteeringDemo.controller";
    private const string HandPath = "HandDemoArea/HandImage";

    private static AnimationClip Clip()
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller == null) throw new InvalidOperationException("The existing BallSteeringDemo controller was not found.");
        return controller.animationClips.First(c => c.name == "UI Steering Demo");
    }

    public static string ReadBindings()
    {
        var clip = Clip();
        var result = new StringBuilder(AssetDatabase.GetAssetPath(clip) + "\n");
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            result.AppendLine(binding.path + " | " + binding.type.Name + " | " + binding.propertyName + " | " +
                string.Join(", ", curve.keys.Select(k => k.time + ":" + k.value)));
        }
        return result.ToString();
    }

    public static string Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing the guidance clip.");
        var clip = Clip();
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var ballPosition = bindings.First(b => b.path == "BallDemoArea/BallImage" && b.propertyName == "m_AnchoredPosition.x");
        var source = AnimationUtility.GetEditorCurve(clip, ballPosition);
        float extent = source.keys.Max(k => Mathf.Abs(k.value));
        if (extent < .001f) throw new InvalidOperationException("The ball steering motion has no lateral travel to synchronize with.");
        string clipPath = AssetDatabase.GetAssetPath(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
        Directory.CreateDirectory("../.tmp/guidance-hand-y-restoration-before");
        File.Copy(clipPath, "../.tmp/guidance-hand-y-restoration-before/" + Path.GetFileName(clipPath), true);
        Undo.RegisterCompleteObjectUndo(clip, "Rotate guidance hand thirty degrees");
        var rotation = new AnimationCurve(source.keys.Select(k =>
            new Keyframe(k.time, -30f * Mathf.Clamp(k.value / extent, -1f, 1f), 0f, 0f)).ToArray());
        // Restore the user's preferred first version: +/-30 degrees around Y.
        var activeRotation = EditorCurveBinding.FloatCurve(HandPath, typeof(RectTransform), "localEulerAnglesRaw.y");
        foreach (string axis in new[] { "x", "z" })
            AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(HandPath, typeof(RectTransform), "localEulerAnglesRaw." + axis),
                new AnimationCurve(source.keys.Select(k => new Keyframe(k.time, 0f, 0f, 0f)).ToArray()));
        AnimationUtility.SetEditorCurve(clip, activeRotation, rotation);
        foreach (var binding in bindings.Where(b => b.path == HandPath && b.propertyName == "m_AnchoredPosition.x"))
        {
            var previous = AnimationUtility.GetEditorCurve(clip, binding);
            float centre = previous.keys[0].value;
            AnimationUtility.SetEditorCurve(clip, binding,
                new AnimationCurve(previous.keys.Select(k => new Keyframe(k.time, centre, 0f, 0f)).ToArray()));
        }
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
        return "Saved the existing UI Steering Demo hand curve (" + activeRotation.propertyName + "): neutral -> +30 degrees left -> neutral -> -30 degrees right -> neutral, with the original ball motion, holds and loop timing preserved.";
    }
}
