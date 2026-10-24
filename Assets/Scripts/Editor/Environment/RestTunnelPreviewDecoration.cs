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
        const string KayModels = "Assets/EndlessRollballAssets/Dungeon/KayKit Dungeon Pack/KayKit_Dungeon_Pack_1.1_FREE/Assets/fbx(unity)/";

        [MenuItem("Tools/Endless Rollball/Environment/Add Tunnel Decorations And Centre Hanging Lights")]
        public static void Decorate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            DecorateFromRequest();
        }

        internal static void DecorateFromRequest()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode.");
            var root = scene.GetRootGameObjects().SingleOrDefault(item => item.name == RootName && item.activeSelf);
            if (!root) throw new InvalidOperationException("The active 24m tunnel preview is required.");
            if (root.transform.Find("07_Subtle_Dungeon_Decoration"))
                throw new InvalidOperationException("Tunnel dressing already exists; your edits were preserved.");
            foreach (var path in new[] { Ruins + "Pot1.fbx", Ruins + "Pot2.fbx", Ruins + "Bush_1x1.fbx", KayModels + "barrel_small.fbx" })
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) throw new FileNotFoundException(path);

            var backup = "Artifacts/EnvironmentPreviewBackups/RestTunnel_Dressing_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview_SavedBefore.unity");
            // This also preserves any current user edits before the scoped dressing change.
            EditorSceneManager.SaveScene(scene, backup + "/EnvironmentPreview_LiveBefore.unity", true);
            EnsureFolder(MaterialFolder);
            var trim = Material("Stone_SoftSandstone", new Color(.57f, .54f, .47f));
            var metal = Material("Lantern_DarkBronze", new Color(.22f, .24f, .23f));
            var diffuser = Material("Lantern_SoftAmber", new Color(.86f, .77f, .57f));
            diffuser.EnableKeyword("_EMISSION");
            diffuser.SetColor("_EmissionColor", new Color(.8f, .64f, .35f));
            var clay = Material("Decoration_MutedClay", new Color(.46f, .42f, .34f));
            var wood = Material("Decoration_MutedWood", new Color(.33f, .30f, .25f));
            var green = Material("Decoration_SageGreen", new Color(.28f, .37f, .27f));
            Transform hanging = null, dressing = null;
            var previousLights = root.transform.Find("04_Steady_Recessed_Lights");
            var previousActive = previousLights && previousLights.gameObject.activeSelf;
            try
            {
                hanging = Group("04_Centre_Hanging_Lanterns", root.transform);
                dressing = Group("07_Subtle_Dungeon_Decoration", root.transform);
                var shell = root.transform.Find("01_Imported_Hollow_Cylinder");
                var roofHeight = Bounds(shell.gameObject).max.y - root.transform.position.y;
                // Lowest lantern point is >=6.4m; supports actually reach the sleeve crown.
                var lanternY = Mathf.Max(6.85f, roofHeight - 1.25f);
                foreach (var z in new[] { 4f, 12f, 20f })
                {
                    var lamp = Group("Hanging_Lantern_Z" + z, hanging);
                    var roofAnchorY = roofHeight - .06f;
                    var topY = lanternY + .45f;
                    Box("Ceiling_Anchor", lamp, new Vector3(0, roofAnchorY, z), new Vector3(.48f, .12f, .48f), metal);
                    Rod("Suspension_Rod", lamp, new Vector3(0, (roofAnchorY + topY) * .5f, z),
                        .07f, Mathf.Max(.05f, roofAnchorY - topY), metal);
                    Box("Top_Cap", lamp, new Vector3(0, lanternY + .35f, z), new Vector3(.88f, .12f, .88f), metal);
                    Box("Steady_Diffuser", lamp, new Vector3(0, lanternY, z), new Vector3(.63f, .58f, .63f), diffuser);
                    Box("Bottom_Cap", lamp, new Vector3(0, lanternY - .35f, z), new Vector3(.88f, .12f, .88f), metal);
                    foreach (var x in new[] { -.36f, .36f }) foreach (var depth in new[] { -.36f, .36f })
                        Box("Lantern_Corner_Frame_" + x + "_" + depth, lamp, new Vector3(x, lanternY, z + depth),
                            new Vector3(.065f, .64f, .065f), metal);
                    Point("Steady_Warm_Light", lamp, new Vector3(0, lanternY - .48f, z), new Color(1, .87f, .66f), 12, 14);
                }

                var urns = Group("01_Stone_And_Clay_Urns", dressing);
                AddPot("Left_Entrance_Urn", urns, -6.0f, 3.0f, 1.35f, Ruins + "Pot2.fbx", clay, trim);
                AddPot("Right_Middle_Urn", urns, 6.15f, 10.5f, 1.15f, Ruins + "Pot1.fbx", clay, trim);
                AddPot("Left_Rear_Urn", urns, -6.25f, 18.4f, 1.45f, Ruins + "Pot1.fbx", clay, trim);
                var planters = Group("02_Low_Stone_Planters", dressing);
                foreach (var spot in new[] { new Vector3(6.15f, .14f, 5.8f), new Vector3(-6.1f, .14f, 12.8f), new Vector3(6.2f, .14f, 20.4f) })
                {
                    var item = Group("Planter_Z" + spot.z, planters);
                    Box("Stone_Container", item, spot + new Vector3(0, .2f, 0), new Vector3(1.25f, .4f, 1.3f), trim);
                    FitModel(Ruins + "Bush_1x1.fbx", item, "Low_Sage_Plant", spot + new Vector3(0, .4f, 0),
                        new Vector3(1.1f, .55f, 1.15f), green);
                }
                var storage = Group("03_Small_Storage_Accent", dressing);
                FitModel(KayModels + "barrel_small.fbx", storage, "Right_Storage_Barrel", new Vector3(6.2f, .11f, 15),
                    new Vector3(.85f, 1.05f, .85f), wood);
                FitModel(KayModels + "barrel_small.fbx", storage, "Right_Storage_Barrel_Small", new Vector3(6.65f, .11f, 16.1f),
                    new Vector3(.65f, .8f, .65f), wood);
                // No mirrored wall arrangement, banners, particles, props or colliders on the road.
                if (previousLights)
                {
                    previousLights.name = "04_Previous_Wall_Lights_Inactive";
                    previousLights.gameObject.SetActive(false);
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                File.AppendAllText(Output + "/CreationRecord.txt", "\nDressing revision: three static centreline hanging lanterns at Z4/12/20, minimum lantern bottom >=6.4m.\n" +
                    "Three plinth urns, three low planted stone containers and two small barrels, staggered on side ledges.\n" +
                    "Original wall lights preserved inactive; cylinder, road, entrance architecture and user edits retained.\n" +
                    "Backup: " + backup + "\nNo gameplay integration, Play Mode, tests or profiling.\n");
                Selection.activeGameObject = root;
                Debug.Log("REST_TUNNEL_DRESSING_SAVED: centre hanging lanterns and subtle side decorations. Backup: " + backup);
            }
            catch
            {
                if (hanging) Object.DestroyImmediate(hanging.gameObject);
                if (dressing) Object.DestroyImmediate(dressing.gameObject);
                if (previousLights)
                {
                    previousLights.name = "04_Steady_Recessed_Lights";
                    previousLights.gameObject.SetActive(previousActive);
                }
                throw;
            }
        }

        static void AddPot(string name, Transform parent, float x, float z, float height, string source, Material clay, Material stone)
        {
            var cluster = Group(name, parent);
            Box("Stone_Plinth", cluster, new Vector3(x, .25f, z), new Vector3(1.3f, .28f, 1.3f), stone);
            FitModel(source, cluster, "Urn", new Vector3(x, .39f, z), new Vector3(1.05f, height, 1.05f), clay);
        }

        static void Rod(string name, Transform parent, Vector3 center, float diameter, float height, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = center;
            item.transform.localScale = new Vector3(diameter, height * .5f, diameter);
            UseMaterial(item, material);
            StripPhysicsAndScripts(item);
        }
    }
}
