using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    public static class StreetCitySceneryExtractor
    {
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string Destination = "Assets/EndlessRollballAssets/Environment Prefab/StreetCity";

        [MenuItem("Tools/Endless Rollball/Environment/Extract Three Street Scenery Prefabs %#&e")]
        public static void Extract()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            var source = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "StreetPreview_ShortCity_72m" && g.activeSelf);
            if (source == null) { Debug.LogWarning("Current 72m Street preview not found."); return; }
            Directory.CreateDirectory(Destination);
            AssetDatabase.Refresh();
            var paths = new List<string>();
            var report = new List<string> {
                "Extracted from: " + ScenePath,
                "Source hierarchy: StreetPreview_ShortCity_72m",
                "Scenery only. Logical length 24m; local entrance (0,0,0), forward +Z.",
                "Road Lane meshes, inactive content, cameras, lights, audio, scripts and physics excluded.",
                "Whole buildings/props assigned by bounds centre; any seam overhangs listed below.",
                "Existing scene layout preserved. No gameplay integration or testing performed."
            };
            var temporary = EditorSceneManager.NewPreviewScene();
            try
            {
                for (int section = 0; section < 3; section++)
                {
                    string name = "ENV_StreetCity_Scenery_" + (char)('A' + section) + "_24m";
                    var copy = UnityEngine.Object.Instantiate(source);
                    SceneManager.MoveGameObjectToScene(copy, temporary);
                    copy.name = name;
                    copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    copy.transform.localScale = Vector3.one;
                    float start = section * 24;
                    var overhang = new List<string>();
                    foreach (var child in copy.transform.Cast<Transform>().ToArray())
                        Prune(child.gameObject, start, start + 24, overhang);
                    foreach (var child in copy.transform.Cast<Transform>())
                        child.localPosition -= Vector3.forward * start;
                    StripNonVisualComponents(copy);
                    string path = Destination + "/" + name + ".prefab";
                    var saved = PrefabUtility.SaveAsPrefabAsset(copy, path);
                    if (saved == null) throw new IOException("Could not save " + path);
                    paths.Add(path);
                    report.Add("\n" + path + " | source interval " + start + ".." + (start + 24) + "m | renderers " + copy.GetComponentsInChildren<Renderer>().Length);
                    foreach (var item in overhang) report.Add("  Seam overhang: " + item);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(temporary); }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Artifacts/EnvironmentExtraction");
            File.WriteAllLines("Artifacts/EnvironmentExtraction/StreetCityExtraction_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt", report);
            Selection.objects = paths.Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p)).ToArray();
            EditorGUIUtility.PingObject(Selection.objects[0]);
            Debug.Log("STREET_SCENERY_EXTRACTED: " + string.Join("; ", paths) + ". Existing 72m scene preserved. No tests run.");
        }

        static bool Prune(GameObject go, float start, float end, List<string> overhang)
        {
            if (!go.activeSelf || go.name.StartsWith("Road Lane", StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object.DestroyImmediate(go);
                return false;
            }
            bool atomic = go.GetComponent<Renderer>() != null || PrefabUtility.IsAnyPrefabInstanceRoot(go);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (atomic && renderers.Length > 0)
            {
                var b = BoundsOf(renderers);
                bool surface = go.name.IndexOf("Sidewalk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    go.name.IndexOf("Foundation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    go.name.IndexOf("Building Floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    go.name.IndexOf("Platform", StringComparison.OrdinalIgnoreCase) >= 0;
                if (surface && b.size.z > 24.01f && (Mathf.Abs(go.transform.forward.z) > .99f || Mathf.Abs(go.transform.right.z) > .99f))
                {
                    float min = Mathf.Max(start, b.min.z), max = Mathf.Min(end, b.max.z);
                    if (max <= min) { UnityEngine.Object.DestroyImmediate(go); return false; }
                    var scale = go.transform.localScale;
                    if (Mathf.Abs(go.transform.forward.z) > .99f) scale.z *= (max - min) / b.size.z;
                    else scale.x *= (max - min) / b.size.z;
                    go.transform.localScale = scale;
                    var adjusted = BoundsOf(renderers);
                    go.transform.position += new Vector3(0, 0, (min + max) * .5f - adjusted.center.z);
                    return true;
                }
                if (b.center.z < start || b.center.z >= end)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                    return false;
                }
                if (b.min.z < start - .05f || b.max.z > end + .05f)
                    overhang.Add(go.name + " [local Z " + (b.min.z - start).ToString("F2") + ".." + (b.max.z - start).ToString("F2") + "]");
                return true;
            }
            foreach (var child in go.transform.Cast<Transform>().ToArray()) Prune(child.gameObject, start, end, overhang);
            if (go.transform.childCount == 0 && go.GetComponent<Renderer>() == null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                return false;
            }
            return true;
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            var b = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) b.Encapsulate(renderer.bounds);
            return b;
        }

        internal static void StripNonVisualComponents(GameObject root)
        {
            // Unpack only the temporary copy to allow removing components from nested imported prefabs.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                    PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component is Collider || component is Rigidbody || component is Joint || component is MonoBehaviour ||
                    component is Camera || component is Light || component is AudioSource || component is AudioListener)
                    UnityEngine.Object.DestroyImmediate(component);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }
    }
}
