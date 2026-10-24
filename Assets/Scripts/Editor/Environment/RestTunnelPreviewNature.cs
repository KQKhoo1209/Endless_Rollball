using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    public static partial class RestTunnelPreviewBuilder
    {
        [MenuItem("Tools/Endless Rollball/Environment/Replace Tunnel Sidewalk Dressing With Nature")]
        public static void Naturalize()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            NaturalizeFromRequest();
        }

        internal static void NaturalizeFromRequest()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode.");
            var root = scene.GetRootGameObjects().SingleOrDefault(item => item.name == RootName && item.activeSelf);
            if (!root) throw new InvalidOperationException("The active 24m tunnel preview is required.");
            if (root.transform.Find("07_Nature_And_Rock_Gardens"))
                throw new InvalidOperationException("Nature revision already exists; manual edits were preserved.");
            var bushes = new[] { "Bush_Round.fbx", "Bush_2x1.fbx", "Bush_1x1.fbx", "Bush_Large.fbx", "Bush_2x2.fbx" };
            foreach (var path in bushes.Select(name => Ruins + name).Concat(new[] { Ruins + "Grass.fbx", KayModels + "rubble_half.fbx" }))
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) throw new FileNotFoundException(path);

            var backup = "Artifacts/EnvironmentPreviewBackups/RestTunnel_Nature_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview_SavedBefore.unity");
            EditorSceneManager.SaveScene(scene, backup + "/EnvironmentPreview_LiveBefore.unity", true);
            EnsureFolder(MaterialFolder);
            var greens = new[] {
                Material("Nature_Leaf_Forest", new Color(.20f, .34f, .22f)),
                Material("Nature_Leaf_Sage", new Color(.34f, .45f, .28f)),
                Material("Nature_Leaf_Moss", new Color(.29f, .40f, .20f))
            };
            var grass = Material("Nature_Grass_SoftGreen", new Color(.38f, .46f, .27f));
            var moss = Material("Nature_Ground_Moss", new Color(.25f, .31f, .21f));
            var rocks = new[] {
                Material("Nature_Rock_WarmGrey", new Color(.43f, .44f, .40f)),
                Material("Nature_Rock_LightGrey", new Color(.53f, .53f, .46f))
            };
            var previous = root.transform.Find("07_Subtle_Dungeon_Decoration");
            Transform nature = null;
            try
            {
                nature = Group("07_Nature_And_Rock_Gardens", root.transform);
                var ground = Group("01_Organic_Moss_Patches", nature);
                var left = Group("02_Left_Staggered_Planting", nature);
                var right = Group("03_Right_Staggered_Planting", nature);
                var edging = Group("04_Grass_And_Pebble_Accents", nature);
                var random = new System.Random(211026);
                var index = 0;
                foreach (var side in new[] { -1, 1 })
                {
                    var positions = side < 0 ? new[] { 2.5f, 6.6f, 11.1f, 16.2f, 21.1f }
                        : new[] { 4.4f, 8.8f, 13.3f, 18f, 22.1f };
                    foreach (var z in positions)
                    {
                        var x = side * 6.55f;
                        var cluster = Group("Garden_Pocket_" + index, side < 0 ? left : right);
                        // Organic low mounds, not containers. Max 2.1m width preserves a
                        // full metre beyond each edge of the 9m road (|x| >= 5.5).
                        MossPatch("Moss_Patch_" + index, ground, new Vector3(x, .14f, z),
                            new Vector3(1.9f + index % 2 * .15f, .09f, 2.3f + index % 3 * .15f), moss);
                        var main = Ruins + bushes[index % bushes.Length];
                        FitModel(main, cluster, "Layered_Main_Shrub", new Vector3(x + side * .13f, .18f, z + .14f),
                            new Vector3(1.2f + index % 2 * .13f, .85f + index % 3 * .18f, 1.25f), greens[index % 3]);
                        FitModel(Ruins + bushes[(index + 2) % bushes.Length], cluster, "Low_Foreground_Shrub",
                            new Vector3(x - side * .42f, .18f, z - .55f), new Vector3(.68f, .42f + index % 2 * .12f, .72f), greens[(index + 1) % 3]);
                        FitModel(Ruins + "Bush_Round.fbx", cluster, "Small_Back_Shrub", new Vector3(x + side * .46f, .18f, z + .58f),
                            new Vector3(.65f, .48f, .7f), greens[(index + 2) % 3]);
                        for (var tuft = 0; tuft < 5; tuft++)
                        {
                            var dx = (float)(random.NextDouble() - .5) * 1.4f;
                            var dz = (float)(random.NextDouble() - .5) * 1.9f;
                            FitModel(Ruins + "Grass.fbx", cluster, "Grass_Tuft_" + tuft,
                                new Vector3(x + dx, .18f, z + dz), new Vector3(.35f, .28f + tuft % 3 * .11f, .38f), grass);
                        }
                        for (var stone = 0; stone < 2; stone++)
                            FitModel(KayModels + "rubble_half.fbx", cluster, "Weathered_Rock_" + stone,
                                new Vector3(x + (stone == 0 ? -.57f : .55f), .15f, z + (stone == 0 ? .73f : -.8f)),
                                new Vector3(.58f + stone * .13f, .23f + index % 3 * .09f, .57f), rocks[(index + stone) % 2]);
                        index++;
                    }
                    // Small isolated tufts bridge garden pockets without a uniform hedge.
                    for (var tuft = 0; tuft < 8; tuft++)
                        FitModel(Ruins + "Grass.fbx", edging, "Ledge_Grass_" + side + "_" + tuft,
                            new Vector3(side * (5.92f + tuft % 2 * .28f), .16f, 1.25f + tuft * 3),
                            new Vector3(.35f, .25f + tuft % 3 * .07f, .38f), grass);
                }
                // Removing this specific old dressing removes all its urns, plinths,
                // container planters and wooden barrels, not unrelated preview roots.
                if (previous) Object.DestroyImmediate(previous.gameObject);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                File.AppendAllText(Output + "/CreationRecord.txt", "\nNature revision: removed old urn/plinth/planter/barrel dressing group; recoverable from scene backup.\n" +
                    "10 asymmetric planted pockets: 30 varied shrubs, 66 grass tufts, 20 low rock pieces, 10 organic moss patches.\n" +
                    "Nature retained outside road plus 1m buffer; hanging centre lights, camera, tunnel and road unchanged.\n" +
                    "Backup: " + backup + "\nNo gameplay integration, Play Mode, automated tests or profiling.\n");
                Selection.activeGameObject = nature.gameObject;
                Debug.Log("REST_TUNNEL_NATURE_SAVED: richer side gardens replaced vases and wooden storage. Backup: " + backup);
            }
            catch
            {
                if (nature) Object.DestroyImmediate(nature.gameObject);
                // The full live scene before replacement remains recoverable in backup.
                throw;
            }
        }

        static void MossPatch(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = center;
            item.transform.localScale = size;
            UseMaterial(item, material);
            StripPhysicsAndScripts(item);
        }
    }
}
