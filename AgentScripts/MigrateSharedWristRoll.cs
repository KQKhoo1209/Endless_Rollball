using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EndlessRollball.Imu;

if (Application.isPlaying) throw new InvalidOperationException("Leave Play Mode before saving input settings.");
if (typeof(ImuGameplayInput).GetField("invertRoll", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) == null)
    throw new InvalidOperationException("The updated shared-roll script is awaiting Unity's normal import. Switch to the Editor window first.");
var original = SceneManager.GetActiveScene();
var report = new System.Collections.Generic.List<string>();
foreach (string path in new[] { "Assets/Scenes/GameplayScene.unity", "Assets/Scenes/TutorialScene.unity" })
{
    var scene = SceneManager.GetSceneByPath(path);
    bool opened = !scene.IsValid() || !scene.isLoaded;
    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
    try
    {
        var inputs = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ImuGameplayInput>(true)).ToArray();
        if (inputs.Length != 1) throw new InvalidOperationException("Expected one player input in " + path);
        var input = inputs[0];
        var settings = new SerializedObject(input);
        settings.Update();
        bool invertRoll = settings.FindProperty("invertRoll").boolValue;
        bool invertJump = settings.FindProperty("invertJump").boolValue;
        // FormerlySerializedAs migrates invertSteering to the common roll direction.
        // Mark/save through the live Editor to remove the retired invertRotation field.
        EditorUtility.SetDirty(input);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.Add(path + ": shared roll inversion=" + invertRoll + ", jump inversion=" + invertJump + ". Saved.");
    }
    finally
    {
        if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        if (opened && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
    }
}
return report;
