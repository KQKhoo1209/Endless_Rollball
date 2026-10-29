using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EndlessRollball.Scenery;
using EndlessRollball.Track;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Editor-only authoring references. This asset must remain outside Resources/build scenes.
public sealed class RuntimeAssetPreparation : ScriptableObject
{
    [Serializable] public sealed class PrefabEntry { public GameObject source; public string outputPath; }
    [Serializable] public sealed class PatternEntry { public ObstaclePattern source; public string outputPath; }
    [Serializable] public sealed class ThemeEntry
    {
        public EnvironmentThemeConfig settings;
        public GameObject[] sources;
        public Material skybox;
        public string resourceName;
    }
    public PrefabEntry[] prefabs;
    public PatternEntry[] patterns;
    public ThemeEntry[] themes;

    private static bool Protected(Transform t) =>
        t.GetComponentsInParent<MonoBehaviour>(true).Any(component => !(component is CityBusStopSlots)) ||
        t.GetComponentsInParent<Animator>(true).Length > 0 ||
        t.GetComponentsInParent<Animation>(true).Length > 0;

    private static HashSet<Object> ReferencedObjects(GameObject root)
    {
        var references = new HashSet<Object>();
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) throw new InvalidOperationException("Missing script in " + root.name);
            if (component is Transform) continue; // Parent/child structure is handled separately.
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null &&
                    property.propertyPath != "m_GameObject")
                    references.Add(property.objectReferenceValue);
        }
        return references;
    }

    private static void Lean(GameObject root)
    {
        // Unpack only temporary loaded contents; original authoring assets remain unchanged.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != null && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var references = ReferencedObjects(root);
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.enabled || Protected(renderer.transform) || references.Contains(renderer)) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && references.Contains(filter)) continue;
            Object.DestroyImmediate(renderer);
            if (filter != null) Object.DestroyImmediate(filter);
        }
        // Leave referenced anchors and all collider/behaviour ancestors untouched.
        foreach (Transform t in Enumerable.Reverse(root.GetComponentsInChildren<Transform>(true)))
            if (t != null && t != root.transform && t.childCount == 0 && !Protected(t) &&
                !references.Contains(t) && !references.Contains(t.gameObject) && t.GetComponents<Component>().Length == 1)
                Object.DestroyImmediate(t.gameObject);
    }

    private static string ColliderSignature(GameObject root)
    {
        // Retained colliders and their world-in-prefab transforms must be byte-identical.
        return string.Join("\n", root.GetComponentsInChildren<Collider>(true).Select(c =>
            EditorJsonUtility.ToJson(c) + "|" + (root.transform.worldToLocalMatrix * c.transform.localToWorldMatrix).ToString("R")));
    }

    [Serializable] private sealed class Row
    {
        public string sourceGuid, runtimeGuid, outputPath;
        public long sourceFileId, runtimeFileId;
        public int objectsBefore, objectsAfter, renderersBefore, renderersAfter, removedMeshRenderers;
    }
    [Serializable] private sealed class Report { public List<Row> prefabs = new List<Row>(); }

    public void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Runtime asset preparation is Edit Mode only.");
        var prepared = new List<(PrefabEntry entry, GameObject root, Row row)>();
        var map = new Dictionary<GameObject, GameObject>();
        var report = new Report();
        try
        {
            var sourceSet = new HashSet<GameObject>(prefabs.Select(p => p.source));
            var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PrefabEntry entry in prefabs)
                if (entry.source == null || !outputs.Add(entry.outputPath))
                    throw new InvalidOperationException("Missing source or duplicate runtime output.");
            foreach (PatternEntry entry in patterns)
            {
                if (entry.source == null || !entry.outputPath.StartsWith("Assets/EndlessRollballAssets/Optimization/Runtime/Patterns/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Missing pattern or invalid runtime output.");
                foreach (var placement in entry.source.Placements)
                    if (placement.prefab == null || !sourceSet.Contains(placement.prefab))
                        throw new InvalidOperationException("Missing/unmapped pattern payload: " + entry.source.name);
            }
            foreach (ThemeEntry theme in themes)
                if (theme.settings == null || theme.sources == null || theme.sources.Length == 0 ||
                    theme.sources.Any(p => p == null || !sourceSet.Contains(p)) ||
                    (theme.settings.Theme != EnvironmentTheme.Dungeon && theme.skybox == null) ||
                    string.IsNullOrWhiteSpace(theme.resourceName) || theme.resourceName.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
                    throw new InvalidOperationException("Missing/invalid theme source in preparation manifest.");
            // Validate/prepare ALL hierarchies before saving any runtime prefab.
            foreach (PrefabEntry entry in prefabs)
            {
                if (entry.source == null || !entry.outputPath.StartsWith("Assets/EndlessRollballAssets/Optimization/Runtime/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Invalid source/output in runtime preparation manifest.");
                GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(entry.source));
                var row = new Row { outputPath = entry.outputPath,
                    objectsBefore = root.GetComponentsInChildren<Transform>(true).Length,
                    renderersBefore = root.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled) };
                prepared.Add((entry, root, row));
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(entry.source, out row.sourceGuid, out row.sourceFileId);
                // Unpack may change serialization bookkeeping, so capture collision after unpacking.
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t != null && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                        PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                string collision = ColliderSignature(root);
                int before = root.GetComponentsInChildren<MeshRenderer>(true).Length;
                Lean(root);
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled && (renderer.GetComponent<MeshFilter>()?.sharedMesh == null ||
                        renderer.sharedMaterials.Any(m => m == null)))
                        throw new InvalidOperationException("Missing mesh/material: " + entry.source.name);
                row.removedMeshRenderers = before - root.GetComponentsInChildren<MeshRenderer>(true).Length;
                row.objectsAfter = root.GetComponentsInChildren<Transform>(true).Length;
                row.renderersAfter = root.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled);
                if (row.renderersAfter != row.renderersBefore || ColliderSignature(root) != collision)
                    throw new InvalidOperationException("Conversion changed visuals/collision: " + entry.source.name);
            }
            foreach (var item in prepared)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.entry.outputPath));
                GameObject output = PrefabUtility.SaveAsPrefabAsset(item.root, item.entry.outputPath);
                if (output == null) throw new InvalidOperationException("Could not save " + item.entry.outputPath);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(output, out item.row.runtimeGuid, out item.row.runtimeFileId);
                map.Add(item.entry.source, output);
                report.prefabs.Add(item.row);
            }
            foreach (PatternEntry entry in patterns)
            {
                if (entry.source == null) throw new InvalidOperationException("Missing pattern source.");
                Directory.CreateDirectory(Path.GetDirectoryName(entry.outputPath));
                var output = AssetDatabase.LoadAssetAtPath<ObstaclePattern>(entry.outputPath);
                if (output == null) { output = CreateInstance<ObstaclePattern>(); AssetDatabase.CreateAsset(output, entry.outputPath); }
                EditorUtility.CopySerialized(entry.source, output);
                foreach (var placement in output.Placements)
                    if (placement.prefab != null && !map.ContainsKey(placement.prefab)) throw new InvalidOperationException("Unmapped pattern prefab.");
                for (int i = 0; i < output.Placements.Length; i++)
                {
                    var placement = output.Placements[i];
                    if (placement.prefab != null) placement.prefab = map[placement.prefab];
                    output.Placements[i] = placement;
                }
                EditorUtility.SetDirty(output);
            }
            foreach (ThemeEntry theme in themes)
            {
                string path = "Assets/EndlessRollballAssets/Resources/EnvironmentScenery/" + theme.resourceName + ".asset";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var set = AssetDatabase.LoadAssetAtPath<EnvironmentScenerySet>(path);
                if (set == null) { set = CreateInstance<EnvironmentScenerySet>(); AssetDatabase.CreateAsset(set, path); }
                set.prefabs = theme.sources.Select(p => map[p]).ToArray();
                set.skybox = theme.skybox;
                EditorUtility.SetDirty(set);
                theme.settings.sceneryResourcePath = "EnvironmentScenery/" + theme.resourceName;
                EditorUtility.SetDirty(theme.settings);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Artifacts/Optimization");
            File.WriteAllText("Artifacts/Optimization/runtime-assets-report.json", JsonUtility.ToJson(report, true));
            Debug.Log($"Runtime asset preparation complete: {report.prefabs.Count} prefabs; " +
                $"objects {report.prefabs.Sum(r => r.objectsBefore)} -> {report.prefabs.Sum(r => r.objectsAfter)}; " +
                $"removed source mesh renderers {report.prefabs.Sum(r => r.removedMeshRenderers)}.");
        }
        finally
        {
            foreach (var item in prepared) PrefabUtility.UnloadPrefabContents(item.root);
        }
    }
}

[CustomEditor(typeof(RuntimeAssetPreparation))]
public sealed class RuntimeAssetPreparationInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Explicit Edit Mode conversion. Original prefabs remain editable. Regenerate after source changes; no automatic hooks.", MessageType.Info);
        if (GUILayout.Button("Regenerate Runtime Assets"))
        {
            try { ((RuntimeAssetPreparation)target).Generate(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
        DrawDefaultInspector();
    }
}
