using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // Edit-mode presentation only. Never edits gameplay tracks or the imported models.
    public static partial class RestTunnelPreviewBuilder
    {
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string RootName = "TunnelPreview_RestPassage_24m";
        const string CylinderPath = "Assets/EndlessRollballAssets/Tunnel/Hollow cylinder.obj";
        const string Ruins = "Assets/EndlessRollballAssets/Dungeon/Modular Ruins Pack/FBX/";
        const string MaterialFolder = "Assets/EndlessRollballAssets/Materials/Environment/RestTunnelPreview";
        const string Output = "Artifacts/RestTunnelPreview24m";

        public static void BuildBatch()
        {
            try { Build(); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        [MenuItem("Tools/Endless Rollball/Environment/Build Quiet Tunnel Preview 24m")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the tunnel in Edit Mode only.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            if (scene.GetRootGameObjects().Any(item => item.name == RootName))
                throw new InvalidOperationException("Tunnel preview already exists. Its manual edits were preserved.");

            var camera = scene.GetRootGameObjects()
                .Where(item => item.activeSelf)
                .SelectMany(item => item.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
            if (!camera) throw new InvalidOperationException("Existing EnvironmentPreview camera is required.");
            var cylinder = AssetDatabase.LoadAssetAtPath<GameObject>(CylinderPath);
            if (!cylinder) throw new FileNotFoundException("Imported hollow cylinder not found.", CylinderPath);
            foreach (var path in new[] { Ruins + "Arch_Round.fbx", Ruins + "Column_Round.fbx" })
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) throw new FileNotFoundException(path);

            var backup = "Artifacts/EnvironmentPreviewBackups/RestTunnel_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview_Before.unity");
            File.WriteAllText(backup + "/PresentationBefore.json", EditorJsonUtility.ToJson(camera, true));
            var oldRoots = scene.GetRootGameObjects();
            var oldStates = oldRoots.Select(item => item.activeSelf).ToArray();
            var oldCameraPosition = camera.transform.position;
            var oldCameraRotation = camera.transform.rotation;
            var oldCameraSettings = EditorJsonUtility.ToJson(camera);
            var previousSkybox = RenderSettings.skybox;
            var previousSun = RenderSettings.sun;
            var previousFog = RenderSettings.fog;
            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbientColor = RenderSettings.ambientLight;
            var previousAmbientIntensity = RenderSettings.ambientIntensity;

            EnsureFolder(MaterialFolder);
            var shellMat = Material("Shell_WarmGrey", new Color(.51f, .50f, .46f));
            var trimMat = Material("Stone_SoftSandstone", new Color(.57f, .54f, .47f));
            var roadMat = Material("Road_MatteSlate", new Color(.30f, .32f, .33f));
            var shoulderMat = Material("Shoulder_Stone", new Color(.43f, .43f, .39f));
            var guidanceMat = Material("Guidance_MutedYellow", new Color(.74f, .60f, .24f));
            var lampMat = Material("Lamp_WarmDiffuser", new Color(.83f, .76f, .59f));
            lampMat.EnableKeyword("_EMISSION");
            lampMat.SetColor("_EmissionColor", new Color(.65f, .53f, .32f));
            var root = new GameObject(RootName);
            var report = new StringBuilder();
            try
            {
                var shell = Group("01_Imported_Hollow_Cylinder", root.transform);
                var route = Group("02_Preview_Road_VisualOnly", root.transform);
                var portals = Group("03_Dungeon_Stone_Entrances", root.transform);
                var lamps = Group("04_Steady_Recessed_Lights", root.transform);
                var presentation = Group("05_ReviewOnly_Lighting_And_Scale", root.transform);
                var markers = Group("06_Entrance_And_Exit_Markers", root.transform);

                // OBJ cylinder is a single open sleeve. Detect its short axial dimension,
                // turn that axis to +Z, then fit a roomy elliptical envelope around a flat road.
                var fitting = Group("Cylinder_Envelope_15p6w_14h_24m", shell);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(cylinder, fitting);
                model.name = "Imported_Hollow_Cylinder_OpenEnded";
                var b = Bounds(model);
                var axis = b.size.x < b.size.y && b.size.x < b.size.z ? Vector3.right
                    : b.size.y < b.size.z ? Vector3.up : Vector3.forward;
                model.transform.rotation = Quaternion.FromToRotation(axis, Vector3.forward) * model.transform.rotation;
                b = Bounds(model);
                fitting.localScale = new Vector3(15.6f / b.size.x, 14f / b.size.y, 24f / b.size.z);
                b = Bounds(model);
                fitting.position += new Vector3(0, 1.2f, 12) - b.center;
                UseMaterial(model, shellMat);
                StripPhysicsAndScripts(model);
                report.AppendLine("Imported sleeve: " + CylinderPath + " | world bounds " + Bounds(model).ToString("F3"));

                // Everything under this root is a review visual, including this flat floor.
                // The sleeve's curved bottom lies below the platform, never on the running road.
                Box("Flat_9m_Road_24m", route, new Vector3(0, -.16f, 12), new Vector3(9, .3f, 24), roadMat);
                foreach (var side in new[] { -1, 1 })
                {
                    Box("Raised_Side_Ledge_" + side, route, new Vector3(side * 6.25f, -.04f, 12),
                        new Vector3(3.5f, .3f, 24), shoulderMat);
                    Box("Continuous_Yellow_Route_Edge_" + side, route, new Vector3(side * 4.43f, .002f, 12),
                        new Vector3(.065f, .02f, 24), guidanceMat);
                    // Low uninterrupted skirting replaces a visually busy row of repeating ribs.
                    Box("Low_Stone_Skirting_" + side, route, new Vector3(side * 7.35f, .34f, 12),
                        new Vector3(.35f, .45f, 24), trimMat);
                }

                foreach (var z in new[] { .42f, 23.58f })
                {
                    FitModel(Ruins + "Arch_Round.fbx", portals, "Stone_Opening_" + z.ToString("F0"),
                        new Vector3(0, .05f, z), new Vector3(16.4f, 8.9f, .8f), trimMat);
                    foreach (var side in new[] { -1, 1 })
                        FitModel(Ruins + "Column_Round.fbx", portals, "Portal_Base_" + side + "_" + z.ToString("F0"),
                            new Vector3(side * 7.22f, .11f, z), new Vector3(.85f, 3.4f, .85f), trimMat);
                }

                // Only four small static diffusers; staggered, without flashing or fire VFX.
                for (var i = 0; i < 4; i++)
                {
                    var side = i % 2 == 0 ? -1 : 1;
                    var z = 4 + i * 5.2f;
                    Box("Matte_Lamp_Housing_" + i, lamps, new Vector3(side * 7.16f, 2.9f, z),
                        new Vector3(.16f, .43f, .68f), trimMat);
                    Box("Steady_Warm_Diffuser_" + i, lamps, new Vector3(side * 7.04f, 2.9f, z),
                        new Vector3(.08f, .26f, .48f), lampMat);
                    Point("Broad_Warm_Light_" + i, lamps, new Vector3(side * 6.5f, 3, z),
                        new Color(1, .87f, .66f), 4, 12);
                }
                foreach (var z in new[] { 3f, 12f, 21f })
                    Point("Review_Soft_Fill_" + z, presentation, new Vector3(0, 5.5f, z),
                        new Color(.78f, .82f, .88f), 5, 16);
                var fill = Group("Review_Only_Directional_Fill", presentation);
                fill.rotation = Quaternion.Euler(40, -25, 0);
                var sun = fill.gameObject.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.color = new Color(1, .95f, .86f);
                sun.intensity = .65f;
                sun.shadows = LightShadows.None;

                var scaleBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                scaleBall.name = "ReviewOnly_1m_Sphere_Not_Player";
                scaleBall.transform.SetParent(presentation, false);
                scaleBall.transform.localPosition = new Vector3(0, .5f, 5.5f);
                UseMaterial(scaleBall, Material("ScaleSphere_Teal", new Color(.16f, .51f, .58f)));
                StripPhysicsAndScripts(scaleBall);
                Group("Entrance_Z0", markers).localPosition = Vector3.zero;
                Group("Midpoint_Z12", markers).localPosition = new Vector3(0, 0, 12);
                Group("Exit_Z24", markers).localPosition = new Vector3(0, 0, 24);

                foreach (var old in oldRoots)
                {
                    // Preserve the camera rig and event system, archive previous theme stages.
                    if (old == camera.transform.root.gameObject || old.name.Contains("EventSystem")) continue;
                    old.SetActive(false);
                }
                RenderSettings.skybox = null;
                RenderSettings.sun = sun;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.48f, .49f, .50f);
                RenderSettings.ambientIntensity = 1;
                camera.transform.SetPositionAndRotation(new Vector3(0, 3.25f, -5.5f), Quaternion.Euler(7, 0, 0));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.38f, .41f, .44f);
                camera.fieldOfView = 60;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 100;
                camera.allowHDR = true;
                // Existing volume settings and camera components are preserved, not replaced.
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Directory.CreateDirectory(Output);
                report.AppendLine("Preview only: 24m, open ends, 9m flat road, cylinder bottom below the platform.");
                report.AppendLine("Imported Quaternius Arch_Round + Column_Round used at openings only.");
                report.AppendLine("All previous scene stages preserved inactive. Backup: " + backup);
                report.AppendLine("No gameplay scene, track prefabs, physics, pooling or runtime integration changes.");
                report.AppendLine("No Play Mode, automated test suite or performance profiling run.");
                File.WriteAllText(Output + "/CreationRecord.txt", report.ToString());
                Selection.activeGameObject = root;
                if (SceneView.lastActiveSceneView)
                    SceneView.lastActiveSceneView.LookAt(new Vector3(0, 3, 12), Quaternion.Euler(9, 0, 0), 20);
                Debug.Log("REST_TUNNEL_PREVIEW_SAVED: " + RootName + " in " + ScenePath + ". Backup: " + backup);
            }
            catch
            {
                Object.DestroyImmediate(root);
                for (var i = 0; i < oldRoots.Length; i++) oldRoots[i].SetActive(oldStates[i]);
                EditorJsonUtility.FromJsonOverwrite(oldCameraSettings, camera);
                camera.transform.SetPositionAndRotation(oldCameraPosition, oldCameraRotation);
                RenderSettings.skybox = previousSkybox;
                RenderSettings.sun = previousSun;
                RenderSettings.fog = previousFog;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientColor;
                RenderSettings.ambientIntensity = previousAmbientIntensity;
                throw;
            }
        }

        static Transform Group(string name, Transform parent)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            return item.transform;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Material Material(string name, Color color)
        {
            var path = MaterialFolder + "/MAT_TR_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP Lit shader is required.");
            material = new Material(shader) { name = "MAT_TR_" + name, enableInstancing = true, doubleSidedGI = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .08f);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Cull", 0); // Imported sleeve must remain visible from inside.
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Bounds Bounds(GameObject item)
        {
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No render geometry: " + item.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void UseMaterial(GameObject item, Material material)
        {
            foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
        }

        static void StripPhysicsAndScripts(GameObject item)
        {
            foreach (var component in item.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(component);
            foreach (var component in item.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(component);
            foreach (var component in item.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(component);
            foreach (var component in item.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(component);
            foreach (var component in item.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(component);
        }

        static void Box(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = center;
            item.transform.localScale = size;
            UseMaterial(item, material);
            StripPhysicsAndScripts(item);
        }

        static void FitModel(string path, Transform parent, string name, Vector3 ground, Vector3 size, Material material)
        {
            var fitting = Group(name, parent);
            var item = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), fitting);
            var b = Bounds(item);
            fitting.localScale = new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z);
            b = Bounds(item);
            fitting.position += ground - new Vector3(b.center.x, b.min.y, b.center.z);
            UseMaterial(item, material);
            StripPhysicsAndScripts(item);
        }

        static void Point(string name, Transform parent, Vector3 position, Color color, float intensity, float range)
        {
            var point = Group(name, parent);
            point.localPosition = position;
            var light = point.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }
    }
}
