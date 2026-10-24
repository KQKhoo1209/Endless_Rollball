using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // Authoring only: no scene edits, runtime component, or gameplay-track changes.
    internal static class ThemedRoadSurfaceBuilder
    {
        const string Output = "Assets/EndlessRollballAssets/Environment Prefab/Roads";
        const string MaterialOutput = "Assets/EndlessRollballAssets/Materials/Track/RoadVisuals";
        const string Request = "Artifacts/ThemedRoadSurfaces/Create.request";
        const string Report = "Artifacts/ThemedRoadSurfaces/CreationReport.txt";
        const float Width = 9f;
        const float Length = 24f;
        static double deadline;

        static readonly string[] Names =
        {
            "RD_Street_RoadLane01_24m",
            "RD_Beach_WoodPlatform_24m",
            "RD_Dungeon_WoodLargeDark_24m"
        };

        static readonly string[] Sources =
        {
            "Assets/EndlessRollballAssets/Street/SimplePoly City - Low Poly Assets/Prefab/Roads/Road Lane_01.prefab",
            "Assets/EndlessRollballAssets/Waterpark/kenney_pirate-kit/Models/platform.fbx",
            "Assets/EndlessRollballAssets/Dungeon/KayKit Dungeon Pack/KayKit_Dungeon_Pack_1.1_FREE/Assets/fbx(unity)/floor_wood_large_dark.fbx"
        };

        [InitializeOnLoadMethod]
        static void ScheduleRequestedAuthoring()
        {
            if (Application.isBatchMode || !File.Exists(Request)) return;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30)) return;
            if (File.ReadAllText(Request).Trim() != "create-three-visual-only-road-prefabs-v1") return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += RunRequestedAuthoring;
        }

        static void RunRequestedAuthoring()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= RunRequestedAuthoring;
                return;
            }
            EditorApplication.update -= RunRequestedAuthoring;
            if (!File.Exists(Request)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.WriteAllText(Request + ".blocked", "Exit Play Mode and use the Create Three Themed Road Surface Prefabs menu.");
                return;
            }
            File.Move(Request, Request + ".consumed_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            try { Create(); }
            catch (Exception error)
            {
                File.WriteAllText(Request + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Endless Rollball/Track/Create Three Themed Road Surface Prefabs")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Create road assets in Edit Mode only.");
            foreach (string name in Names)
                if (File.Exists(Output + "/" + name + ".prefab"))
                    throw new IOException("Existing road prefab preserved: " + name);

            var models = Sources.Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)).ToArray();
            for (int i = 0; i < models.Length; i++)
                if (!models[i]) throw new FileNotFoundException("Missing source: " + Sources[i]);

            EnsureFolder(Output);
            var staging = EditorSceneManager.NewPreviewScene();
            var roots = new List<GameObject>();
            var converted = new Dictionary<Material, Material>();
            var report = new StringBuilder();
            report.AppendLine("Three visual-only themed road prefabs — asset authoring, not gameplay tests.");
            report.AppendLine("Root identity; +Z forward; entrance (0,0,0); width 9m; length 24m; visible top Y=0.");
            report.AppendLine("Repeated source meshes share their original mesh assets. No colliders, scripts, lights or rigidbodies.");
            report.AppendLine("No scenes, source assets, scenery prefabs or gameplay tracks changed. No Play Mode or profiling.");
            report.AppendLine("Straight-wide review surfaces ONLY. Do not place an uncut surface across TRK_Gap or a narrow track.");
            try
            {
                for (int theme = 0; theme < models.Length; theme++)
                {
                    var root = new GameObject(Names[theme]);
                    SceneManager.MoveGameObjectToScene(root, staging);
                    roots.Add(root);
                    var probe = (GameObject)PrefabUtility.InstantiatePrefab(models[theme], staging);
                    StripNonVisual(probe);
                    Bounds bounds = BoundsOf(probe);
                    if (bounds.size.x < .001f || bounds.size.z < .001f)
                        throw new InvalidOperationException("Source must have a horizontal surface: " + Sources[theme]);

                    // Keep the imported axis conversion. Fit repeated horizontal tiles, never the track owner.
                    int columns = Mathf.Clamp(Mathf.RoundToInt(Width / bounds.size.x), 1, 16);
                    int rows = Mathf.Clamp(Mathf.RoundToInt(Length / bounds.size.z), 1, 48);
                    float tileWidth = Width / columns;
                    float tileLength = Length / rows;
                    Vector3 scale = new Vector3(tileWidth / bounds.size.x, 1, tileLength / bounds.size.z);
                    Vector3 sourcePosition = probe.transform.position;
                    Quaternion sourceRotation = probe.transform.rotation;
                    Vector3 sourceScale = probe.transform.localScale;
                    ConvertLegacyMaterials(probe, converted);

                    for (int row = 0; row < rows; row++)
                    {
                        var rowGroup = new GameObject("Row_" + (row + 1).ToString("00")).transform;
                        rowGroup.SetParent(root.transform, false);
                        for (int column = 0; column < columns; column++)
                        {
                            var fit = new GameObject("Tile_" + (column + 1).ToString("00")).transform;
                            fit.SetParent(rowGroup, false);
                            fit.localPosition = new Vector3(-Width / 2 + (column + .5f) * tileWidth, 0, (row + .5f) * tileLength);
                            fit.localScale = scale;
                            // Duplicate scene hierarchy but retain source mesh/material references; no mesh baking.
                            var tile = Object.Instantiate(probe, fit, false);
                            tile.name = "Source_" + models[theme].name;
                            tile.transform.localPosition = sourcePosition - new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                            tile.transform.localRotation = sourceRotation;
                            tile.transform.localScale = sourceScale;
                        }
                    }
                    Object.DestroyImmediate(probe);
                    Bounds final = BoundsOf(root);
                    if (Mathf.Abs(final.size.x - Width) > .01f || Mathf.Abs(final.size.z - Length) > .01f || Mathf.Abs(final.max.y) > .01f)
                        throw new InvalidOperationException("Authoring bounds do not match the surface contract: " + root.name);
                    report.AppendLine();
                    report.AppendLine(root.name);
                    report.AppendLine("  Source: " + Sources[theme]);
                    report.AppendLine("  Original bounds: center=" + bounds.center.ToString("F4") + "; size=" + bounds.size.ToString("F4"));
                    report.AppendLine("  Tiles: " + columns + " across x " + rows + " along; fit scale=" + scale.ToString("F4"));
                    report.AppendLine("  Final bounds: min=" + final.min.ToString("F4") + "; max=" + final.max.ToString("F4"));
                }

                // All three compositions must exist before saving; never overwrite an existing asset.
                foreach (var root in roots)
                {
                    string path = Output + "/" + root.name + ".prefab";
                    if (File.Exists(path)) throw new IOException("Existing prefab preserved: " + path);
                    if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new IOException("Save failed: " + path);
                    report.AppendLine("SAVED " + path);
                }
                foreach (Material material in converted.Values) AssetDatabase.SaveAssetIfDirty(material);
                Directory.CreateDirectory(Path.GetDirectoryName(Report));
                File.WriteAllText(Report, report.ToString());
                Debug.Log("THEMED_ROAD_SURFACES_CREATED: " + Output + ". No scenes changed or gameplay tests run.");
            }
            finally { EditorSceneManager.ClosePreviewScene(staging); }
        }

        static void StripNonVisual(GameObject root)
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true).Reverse())
                if (component && !(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer))
                    Object.DestroyImmediate(component);
        }

        static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("No enabled mesh renderers: " + root.name);
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void ConvertLegacyMaterials(GameObject root, Dictionary<Material, Material> converted)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material original = materials[i];
                    if (!original) throw new InvalidOperationException("Missing source material on " + renderer.name);
                    if (original.shader && original.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)) continue;
                    if (!converted.TryGetValue(original, out Material material))
                    {
                        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                        if (!shader) throw new InvalidOperationException("URP/Lit shader is unavailable.");
                        string sourcePath = AssetDatabase.GetAssetPath(original);
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string guid, out long localId))
                            throw new InvalidOperationException("Cannot identify source material: " + sourcePath);
                        EnsureFolder(MaterialOutput);
                        string path = MaterialOutput + "/MAT_Road_" + guid + "_" + localId + ".mat";
                        if (File.Exists(path)) throw new IOException("Existing conversion preserved: " + path);
                        material = new Material(shader) { name = "MAT_Road_" + original.name };
                        string map = original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                        if (original.HasProperty(map))
                        {
                            material.SetTexture("_BaseMap", original.GetTexture(map));
                            material.SetTextureScale("_BaseMap", original.GetTextureScale(map));
                            material.SetTextureOffset("_BaseMap", original.GetTextureOffset(map));
                        }
                        string color = original.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                        if (original.HasProperty(color)) material.SetColor("_BaseColor", original.GetColor(color));
                        material.SetFloat("_Metallic", 0);
                        material.SetFloat("_Smoothness", .15f);
                        material.enableInstancing = true;
                        AssetDatabase.CreateAsset(material, path);
                        converted.Add(original, material);
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
        }
    }
}
