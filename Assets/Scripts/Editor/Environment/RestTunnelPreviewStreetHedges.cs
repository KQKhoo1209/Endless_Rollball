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
    public static partial class RestTunnelPreviewBuilder
    {
        const string StreetReferences = "Assets/EndlessRollballAssets/Environment Prefab/StreetCity/";

        [MenuItem("Tools/Endless Rollball/Environment/Replace Tunnel Nature With StreetCity Hedges")]
        public static void StreetHedges()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            StreetHedgesFromRequest();
        }

        internal static void StreetHedgesFromRequest()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode.");
            var root = scene.GetRootGameObjects().SingleOrDefault(item => item.name == RootName && item.activeSelf);
            if (!root) throw new InvalidOperationException("The active 24m tunnel preview is required.");
            if (root.transform.Find("07_StreetStyle_Hedges"))
                throw new InvalidOperationException("Street-style hedge revision already exists; manual edits were preserved.");

            // Read the actual approved scenery, not raw pack defaults: keep its mesh,
            // material assignments, atlas and shader response without recolouring them.
            var references = new[] { "A", "B", "C", "D", "E" }
                .Select(letter => AssetDatabase.LoadAssetAtPath<GameObject>(StreetReferences + "ENV_StreetCity_Scenery_" + letter + "_24m.prefab"))
                .Where(item => item).ToArray();
            var sources = new Dictionary<string, Transform>();
            foreach (var name in new[] { "Natures_Grass Fence", "Natures_Bush_01", "Natures_Bush_02", "Natures_Bush_03" })
            {
                var source = references.SelectMany(item => item.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(item => item.name == name && item.GetComponentInChildren<Renderer>(true));
                if (!source) throw new InvalidOperationException("Approved StreetCity nature source missing: " + name);
                sources.Add(name, source);
            }

            var backup = "Artifacts/EnvironmentPreviewBackups/RestTunnel_StreetHedges_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview_SavedBefore.unity");
            EditorSceneManager.SaveScene(scene, backup + "/EnvironmentPreview_LiveBefore.unity", true);
            var previous = root.transform.Find("07_Nature_And_Rock_Gardens");
            var oldDressing = root.transform.Find("07_Subtle_Dungeon_Decoration");
            Transform nature = null;
            var report = new StringBuilder();
            try
            {
                nature = Group("07_StreetStyle_Hedges", root.transform);
                var left = Group("01_Left_Long_Hedges", nature);
                var right = Group("02_Right_Long_Hedges", nature);
                var accents = Group("03_Rounded_Bush_Accents", nature);
                var hedge = sources["Natures_Grass Fence"];
                var bushNames = new[] { "Natures_Bush_01", "Natures_Bush_02", "Natures_Bush_03" };
                for (var bank = 0; bank < 6; bank++)
                {
                    var side = bank < 3 ? -1 : 1;
                    var slot = bank % 3;
                    var z = side < 0 ? new[] { 3.4f, 11.3f, 19.5f }[slot] : new[] { 5.1f, 13.4f, 21f }[slot];
                    var length = side < 0 ? new[] { 4.6f, 6f, 5.6f }[slot] : new[] { 6f, 5.2f, 3.6f }[slot];
                    var x = side * 6.45f;
                    var bankRoot = Group("Hedge_Bed_" + (side < 0 ? "Left_" : "Right_") + (slot + 1), side < 0 ? left : right);
                    ApprovedPlant(hedge, bankRoot, "Long_StreetCity_Hedge", new Vector3(x, .11f, z),
                        new Vector3(1.22f, .92f + slot * .075f, length), true);
                    // Compact end bushes make the long pieces read as planted beds,
                    // not as an unbroken stretched wall. No pots, grass scatter or rocks.
                    foreach (var end in new[] { -1, 1 })
                        ApprovedPlant(sources[bushNames[(bank + (end > 0 ? 1 : 0)) % 3]], bankRoot,
                            "Rounded_End_Bush_" + end, new Vector3(x + side * .1f, .11f, z + end * (length * .5f - .25f)),
                            new Vector3(1.4f, .78f + bank % 3 * .09f, .95f), false);
                }
                var accentSpots = new[] {
                    new Vector3(-6.48f, .11f, 7.7f), new Vector3(6.48f, .11f, 9.2f),
                    new Vector3(-6.5f, .11f, 15.7f), new Vector3(6.5f, .11f, 17.9f)
                };
                for (var i = 0; i < accentSpots.Length; i++)
                    ApprovedPlant(sources[bushNames[(i + 1) % 3]], accents, "StreetCity_Accent_Bush_" + (i + 1),
                        accentSpots[i], new Vector3(1.25f, .75f + i % 2 * .15f, 1.35f), false);

                foreach (var pair in sources)
                {
                    report.AppendLine(pair.Key + " from " + AssetDatabase.GetAssetPath(pair.Value.transform.root.gameObject));
                    foreach (var renderer in pair.Value.GetComponentsInChildren<Renderer>(true))
                        foreach (var material in renderer.sharedMaterials)
                            report.AppendLine("  approved material: " + AssetDatabase.GetAssetPath(material));
                }
                // Only the rejected tunnel planting is removed. The approved city
                // prefabs and unrelated scene stages are read-only sources.
                if (previous) Object.DestroyImmediate(previous.gameObject);
                if (oldDressing) Object.DestroyImmediate(oldDressing.gameObject);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                File.WriteAllText(Output + "/StreetCityHedgeSources.txt", report.ToString());
                File.AppendAllText(Output + "/CreationRecord.txt", "\nStreetCity hedge revision: removed the rejected nature/rock garden group.\n" +
                    "Reused approved StreetCity Grass Fence and Bush01/02/03 meshes + existing materials; no new foliage shader/texture.\n" +
                    "Six staggered long hedge beds, twelve rounded end bushes and four isolated bush accents.\n" +
                    "9m route +1m buffer retained; hanging lights, tunnel, architecture, camera and gameplay unchanged.\n" +
                    "Backup: " + backup + "\nNo Play Mode, automated tests or profiling.\n");
                Selection.activeGameObject = nature.gameObject;
                Debug.Log("REST_TUNNEL_STREET_HEDGES_SAVED: approved StreetCity bushes replaced the rejected tunnel nature. Backup: " + backup);
            }
            catch
            {
                if (nature) Object.DestroyImmediate(nature.gameObject);
                throw;
            }
        }

        static void ApprovedPlant(Transform source, Transform parent, string name, Vector3 ground, Vector3 size, bool alignLongAxis)
        {
            var fitting = Group(name, parent);
            var plant = Object.Instantiate(source.gameObject, fitting);
            plant.name = source.name + "_From_Approved_StreetCity";
            plant.SetActive(true);
            plant.transform.localPosition = Vector3.zero;
            plant.transform.localRotation = source.localRotation;
            var b = Bounds(plant);
            if (alignLongAxis && b.size.x > b.size.z)
            {
                plant.transform.localRotation = Quaternion.Euler(0, 90, 0) * plant.transform.localRotation;
                b = Bounds(plant);
            }
            fitting.localScale = new Vector3(size.x / Mathf.Max(.0001f, b.size.x), size.y / Mathf.Max(.0001f, b.size.y),
                size.z / Mathf.Max(.0001f, b.size.z));
            b = Bounds(plant);
            fitting.position += ground - new Vector3(b.center.x, b.min.y, b.center.z);
            StripPhysicsAndScripts(plant);
        }
    }
}
