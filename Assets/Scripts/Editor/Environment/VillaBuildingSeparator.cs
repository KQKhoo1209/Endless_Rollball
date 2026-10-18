using System.IO;
using System.Text;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class VillaBuildingSeparator
{
    private const string Source = "Assets/EndlessRollballAssets/Villa/OMAHE+KAK+MIS.fbx";
    private const string Destination = "Assets/EndlessRollballAssets/Villa/Prefabs";

    [MenuItem("Tools/Endless Rollball/Villa/Separate Two Building Prefabs %#&u")]
    private static void Separate()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        if (!model) throw new System.InvalidOperationException("Villa source not found.");
        // The source buildings are centred at X -31.651 and -111.651.
        // Never split a renderer or a top-level construction group across buildings.
        const float divideX = -71.651f;
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = Object.Instantiate(model);
            SceneManager.MoveGameObjectToScene(instance, preview);
            var children = instance.transform.Cast<Transform>().ToArray();
            var assignment = new System.Collections.Generic.Dictionary<Transform, int>();
            foreach (var child in children)
            {
                var renderers = child.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                var bounds = BoundsOf(renderers);
                if (bounds.min.x < divideX && bounds.max.x > divideX)
                    throw new System.InvalidOperationException("Group crosses building boundary: " + child.name);
                assignment.Add(child, bounds.center.x > divideX ? 0 : 1);
            }
            var names = new[] { "ENV_BeachResort_Villa_A", "ENV_BeachResort_Villa_B" };
            foreach (var name in names)
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Destination + "/" + name + ".prefab"))
                    throw new System.InvalidOperationException("Existing prefab preserved: " + name);
            if (!AssetDatabase.IsValidFolder(Destination))
                AssetDatabase.CreateFolder("Assets/EndlessRollballAssets/Villa", "Prefabs");
            var report = new StringBuilder("Source: " + Source + "\nOriginal FBX and materials preserved. No scene or gameplay changes.\nOriginal imported scale retained. No gameplay tests run.\n");
            GameObject first = null;
            for (int building = 0; building < 2; building++)
            {
                var root = new GameObject(names[building]);
                SceneManager.MoveGameObjectToScene(root, preview);
                foreach (var pair in assignment.Where(p => p.Value == building))
                    pair.Key.SetParent(root.transform, true);
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new System.InvalidOperationException("Empty building.");
                var bounds = BoundsOf(renderers);
                var pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                // Shift the construction groups, not the source mesh data.
                foreach (Transform child in root.transform) child.position -= pivot;
                var path = Destination + "/" + names[building] + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (!prefab) throw new System.InvalidOperationException("Prefab save failed: " + path);
                if (first == null) first = prefab;
                report.AppendLine(path + " | renderers=" + renderers.Length + " | original pivot=" + pivot.ToString("F3") + " | size=" + bounds.size.ToString("F3"));
            }
            Directory.CreateDirectory("Artifacts/VillaSeparation");
            File.WriteAllText("Artifacts/VillaSeparation/separation.txt", report.ToString());
            Selection.activeObject = first;
            EditorGUIUtility.PingObject(first);
            Debug.Log("Created two independent Villa prefabs in " + Destination + ". Source and preview scene unchanged.");
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    private static Bounds BoundsOf(Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    [MenuItem("Tools/Endless Rollball/Villa/Inspect Imported Villa %#&i")]
    private static void Inspect()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        if (!model) throw new System.InvalidOperationException("Villa source not found.");
        var instance = Object.Instantiate(model);
        try
        {
            var report = new StringBuilder();
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                var renderer = t.GetComponent<Renderer>();
                var filter = t.GetComponent<MeshFilter>();
                report.AppendLine($"{AnimationUtility.CalculateTransformPath(t, instance.transform)} | pos={t.position:F3} | bounds={(renderer ? renderer.bounds.ToString("F3") : "-")} | mesh={(filter && filter.sharedMesh ? filter.sharedMesh.name + " vertices=" + filter.sharedMesh.vertexCount : "-")}");
            }
            Directory.CreateDirectory("Artifacts/VillaSeparation");
            File.WriteAllText("Artifacts/VillaSeparation/hierarchy.txt", report.ToString());
            Selection.activeObject = model;
            EditorGUIUtility.PingObject(model);
            Debug.Log("Villa hierarchy written to Artifacts/VillaSeparation/hierarchy.txt");
        }
        finally { Object.DestroyImmediate(instance); }
    }
}
