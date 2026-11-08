using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WireSensorDeviceName
{
    public static string ImportSources()
    {
        foreach (string path in new[] {
            "Assets/Scripts/Input/Imu/IImuTransport.cs",
            "Assets/Scripts/Input/Imu/WindowsBleImuTransport.cs",
            "Assets/Scripts/Input/Imu/ImuRuntimeSession.cs",
            "Assets/Scripts/UI/SensorCalibrationUI.cs" })
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return "Imported the device-name source assets for scene wiring.";
    }

    public static string Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/MainMenuScene.unity" || Application.isPlaying)
            throw new InvalidOperationException("Open MainMenuScene in Edit Mode to wire the device label.");
        var page = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .First(t => t.name == "SensorCalibrationPage");
        var owner = page.GetComponents<Component>().First(c => c != null && c.GetType().Name == "SensorCalibrationUI");
        var label = page.Find("ConnectSensorPanel/SensorDevice").GetComponent<TMP_Text>();
        var data = new SerializedObject(owner);
        var property = data.FindProperty("sensorDevice");
        if (property == null) throw new InvalidOperationException("The updated controller source is awaiting import.");
        Undo.RecordObjects(new UnityEngine.Object[] { owner, label }, "Use live sensor device name");
        property.objectReferenceValue = label;
        data.ApplyModifiedProperties();
        label.richText = false;
        label.text = "Sensor Device: ---";
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "Wired the live Sensor Device label and saved its disconnected placeholder as --- in MainMenuScene.";
    }
}
