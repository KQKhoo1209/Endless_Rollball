using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    public static partial class EnvironmentVisualBuilder
    {
        private const string Root = "Assets/EndlessRollball";
        private const string ArtRoot = Root + "/Art/Environment";
        private const string MaterialRoot = Root + "/Materials/Environment";
        private const string PrefabRoot = Root + "/Prefabs/Environment";
        private const string TrackPrefabRoot = Root + "/Prefabs/Track";
        private const string PreviewScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        private const string CaptureFolder = "Artifacts/EnvironmentScreenshots";
        private const string SessionBuildKey = "EndlessRollball.EnvironmentVisualBuilder.Scheduled";
        private const string SkyboxSetupSessionKey = "EndlessRollball.EnvironmentSkyboxSetup.Version";
        private const int SkyboxSetupVersion = 1;

        private static readonly ThemeSpec[] Themes =
        {
            new ThemeSpec(
                "FS",
                "FuturisticStreet",
                "Preview_FuturisticStreet",
                new Color(0.015f, 0.03f, 0.07f),
                new Color(0.02f, 0.07f, 0.11f),
                new Color(0.10f, 0.42f, 0.48f),
                new Color(0.02f, 0.76f, 0.90f),
                new Color(0.08f, 0.16f, 0.23f),
                "Assets/EndlessRollball/Skybox/ENV_FS_Skybox_CitySurround_v02.png",
                new Color(0.5f, 0.5f, 0.5f),
                1.0f,
                "ER_ENV_FuturisticStreet_01.png"),
            new ThemeSpec(
                "WP",
                "WaterThemePark",
                "Preview_WaterThemePark",
                new Color(0.22f, 0.55f, 0.78f),
                new Color(0.35f, 0.68f, 0.82f),
                new Color(0.10f, 0.56f, 0.62f),
                new Color(0.97f, 0.76f, 0.18f),
                new Color(0.75f, 0.86f, 0.88f),
                "Assets/EndlessRollball/Skybox/ENV_WP_Skybox_TropicalPark_v02.png",
                new Color(0.5f, 0.5f, 0.5f),
                1.0f,
                "ER_ENV_WaterThemePark_01.png"),
            new ThemeSpec(
                "AD",
                "AncientDungeon",
                "Preview_AncientDungeon",
                new Color(0.025f, 0.018f, 0.015f),
                new Color(0.07f, 0.045f, 0.025f),
                new Color(0.35f, 0.27f, 0.18f),
                new Color(1.00f, 0.24f, 0.035f),
                new Color(0.18f, 0.13f, 0.09f),
                "Assets/EndlessRollball/Skybox/ENV_AD_Skybox_TreasureTemple_v02.png",
                new Color(0.5f, 0.5f, 0.5f),
                1.15f,
                "ER_ENV_AncientDungeon_01.png"),
            new ThemeSpec(
                "TR",
                "TransitionTunnel",
                "Preview_TransitionTunnel",
                new Color(0.015f, 0.025f, 0.055f),
                new Color(0.025f, 0.045f, 0.09f),
                new Color(0.18f, 0.24f, 0.32f),
                new Color(0.45f, 0.80f, 1.00f),
                new Color(0.09f, 0.14f, 0.22f),
                null,
                Color.white,
                1.0f,
                "ER_ENV_TransitionTunnel_01.png")
        };

        private static readonly string[] PreviewTrackSequence =
        {
            "TRK_Straight_Wide",
            "TRK_Straight_Wall_Left",
            "TRK_Straight_Wide",
            "TRK_Gap",
            "TRK_Straight_Wide",
            "TRK_Straight_Wall_Right",
            "TRK_Moving_Barrier",
            "TRK_Straight_Wide"
        };

        // Preview generation is manual-only: deleting a preview must not rebuild it
        // or replace the user's active scene on editor startup or script reload.
        private static void ScheduleInitialBuild()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode ||
                    SessionState.GetBool(SessionBuildKey, false) ||
                    !RequiredModelsExist() ||
                    OutputsExist())
                {
                    return;
                }

                SessionState.SetBool(SessionBuildKey, true);
                try
                {
                    BuildAllAndCapture();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        [InitializeOnLoadMethod]
        private static void ScheduleSkyboxSupportingAssets()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode ||
                SessionState.GetInt(SkyboxSetupSessionKey, 0) >= SkyboxSetupVersion)
            {
                return;
            }

            SessionState.SetInt(SkyboxSetupSessionKey, SkyboxSetupVersion);
            EditorApplication.delayCall += () =>
            {
                EnsureSkyboxMaterials();
                BuildDungeonShellPrefab();
                AssetDatabase.SaveAssets();
            };
        }

        [MenuItem("Tools/Endless Rollball/Environment/Build Visual Kits and Preview")]
        private static void BuildAll()
        {
            BuildVisualAssets();
            Debug.Log("Endless Rollball environment materials, visual-only prefabs, lighting rigs, VFX, and preview scene were generated.");
        }

        public static void BuildSkyboxPreviewForAutomation()
        {
            BuildVisualAssets();
            Debug.Log("Endless Rollball themed skyboxes, dungeon shell, and environment preview were generated.");
        }

        [MenuItem("Tools/Endless Rollball/Environment/Build and Capture Chapter 4 Images")]
        public static void BuildAllAndCapture()
        {
            BuildVisualAssets();
            CaptureAll();
            Debug.Log($"Endless Rollball environment build and four 1920x1080 captures completed in {CaptureFolder}.");
        }

        private static void BuildVisualAssets()
        {
            EnsureFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureModelImporters();
            Dictionary<string, Material> materials = CreateMaterials();
            EnsureSkyboxMaterials();

            foreach (ThemeSpec theme in Themes)
            {
                BuildThemePrefabs(theme, materials);
                BuildLightingRig(theme);
                BuildVfxPrefab(theme, materials);
            }

            BuildDungeonShellPrefab();

            CreatePreviewScene(materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            Dictionary<string, Material> result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

            Add(result, "FS_Dark", "FuturisticStreet", new Color(0.025f, 0.055f, 0.10f), 0.65f, 0.35f);
            Add(result, "FS_Mid", "FuturisticStreet", new Color(0.07f, 0.19f, 0.27f), 0.35f, 0.48f);
            Add(result, "FS_Teal", "FuturisticStreet", new Color(0.02f, 0.48f, 0.53f), 0.25f, 0.32f);
            Add(result, "FS_Accent", "FuturisticStreet", new Color(0.96f, 0.70f, 0.12f), 0.15f, 0.40f);
            Add(result, "FS_Emissive", "FuturisticStreet", new Color(0.02f, 0.75f, 0.92f), 0.10f, 0.20f, 3.0f);
            Add(result, "FS_Green", "FuturisticStreet", new Color(0.08f, 0.30f, 0.20f), 0.0f, 0.85f);

            Add(result, "WP_White", "WaterThemePark", new Color(0.82f, 0.90f, 0.92f), 0.0f, 0.55f);
            Add(result, "WP_Aqua", "WaterThemePark", new Color(0.02f, 0.63f, 0.67f), 0.10f, 0.32f);
            Add(result, "WP_Blue", "WaterThemePark", new Color(0.03f, 0.28f, 0.67f), 0.0f, 0.38f);
            Add(result, "WP_Coral", "WaterThemePark", new Color(0.95f, 0.28f, 0.22f), 0.0f, 0.50f);
            Add(result, "WP_Yellow", "WaterThemePark", new Color(1.00f, 0.76f, 0.12f), 0.0f, 0.45f, 1.2f);
            Add(result, "WP_Water", "WaterThemePark", new Color(0.02f, 0.48f, 0.76f), 0.05f, 0.15f, 0.4f);

            Add(result, "AD_Stone", "AncientDungeon", new Color(0.28f, 0.27f, 0.25f), 0.0f, 0.90f);
            Add(result, "AD_DarkStone", "AncientDungeon", new Color(0.10f, 0.09f, 0.085f), 0.0f, 0.95f);
            Add(result, "AD_WarmStone", "AncientDungeon", new Color(0.43f, 0.31f, 0.20f), 0.0f, 0.88f);
            Add(result, "AD_Metal", "AncientDungeon", new Color(0.12f, 0.11f, 0.10f), 0.75f, 0.48f);
            Add(result, "AD_Ember", "AncientDungeon", new Color(1.00f, 0.25f, 0.03f), 0.0f, 0.30f, 3.5f);
            Add(result, "AD_Moss", "AncientDungeon", new Color(0.16f, 0.25f, 0.12f), 0.0f, 1.0f);

            Add(result, "TR_Neutral", "TransitionTunnel", new Color(0.18f, 0.21f, 0.27f), 0.40f, 0.50f);
            Add(result, "TR_Dark", "TransitionTunnel", new Color(0.035f, 0.045f, 0.07f), 0.55f, 0.40f);
            Add(result, "TR_Emissive", "TransitionTunnel", new Color(0.45f, 0.80f, 1.00f), 0.10f, 0.20f, 3.0f);
            Add(result, "Shared_Route", "Shared", new Color(0.95f, 0.72f, 0.12f), 0.0f, 0.42f, 0.8f);
            Add(result, "Shared_HazardRed", "Shared", new Color(0.85f, 0.12f, 0.06f), 0.0f, 0.42f, 0.5f);
            Add(result, "Shared_HazardOrange", "Shared", new Color(1.00f, 0.42f, 0.04f), 0.0f, 0.38f, 0.6f);

            foreach (ThemeSpec theme in Themes)
            {
                Add(result, $"{theme.Code}_TrackSurface", theme.Folder, theme.SurfaceColor, 0.12f, 0.52f);
                Add(result, $"{theme.Code}_TrackEdge", theme.Folder, theme.EdgeColor, 0.05f, 0.28f, 1.5f);
                Add(result, $"{theme.Code}_TrackSupport", theme.Folder, theme.SupportColor, 0.35f, 0.62f);
                Add(result, $"{theme.Code}_TrackDecor", theme.Folder, Color.Lerp(theme.SurfaceColor, Color.white, 0.22f), 0.12f, 0.50f);
            }

            return result;
        }

        private static void EnsureSkyboxMaterials()
        {
            Shader shader = Shader.Find("Skybox/Panoramic");
            if (shader == null)
            {
                Debug.LogWarning("Skybox/Panoramic shader is unavailable; themed skyboxes were not created.");
                return;
            }

            foreach (ThemeSpec theme in Themes.Where(item => !string.IsNullOrEmpty(item.SkyboxTexturePath)))
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(theme.SkyboxTexturePath);
                if (texture == null)
                {
                    Debug.LogWarning($"Theme skybox texture is missing: {theme.SkyboxTexturePath}");
                    continue;
                }

                EnsureFolder(Root, "Skybox");
                string path = theme.SkyboxMaterialPath;
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = shader;
                }

                material.name = $"MAT_ENV_{theme.Code}_Skybox_v02";
                material.SetTexture("_MainTex", texture);
                SetIfPresent(material, "_Tint", theme.SkyboxTint);
                SetIfPresent(material, "_Exposure", theme.SkyboxExposure);
                SetIfPresent(material, "_Rotation", 0f);
                SetIfPresent(material, "_Mapping", 1f);
                SetIfPresent(material, "_ImageType", 0f);
                SetIfPresent(material, "_MirrorOnBack", 0f);
                EditorUtility.SetDirty(material);
            }
        }

        private static void BuildDungeonShellPrefab()
        {
            const string path = PrefabRoot + "/AncientDungeon/ENV_AD_DungeonShell.prefab";
            Material stone = AssetDatabase.LoadAssetAtPath<Material>(
                MaterialRoot + "/AncientDungeon/MAT_ENV_AD_DarkStone.mat");
            if (stone == null)
            {
                return;
            }

            EnsureFolder(PrefabRoot, "AncientDungeon");
            GameObject root = new GameObject("ENV_AD_DungeonShell");
            try
            {
                CreateDungeonShellPart(root.transform, "SM_ENV_AD_Shell_LeftWall",
                    new Vector3(-9.5f, 4f, 96f), new Vector3(5f, 8f, 192f), stone);
                CreateDungeonShellPart(root.transform, "SM_ENV_AD_Shell_RightWall",
                    new Vector3(9.5f, 4f, 96f), new Vector3(5f, 8f, 192f), stone);
                CreateDungeonShellPart(root.transform, "SM_ENV_AD_Shell_Ceiling",
                    new Vector3(0f, 9f, 96f), new Vector3(24f, 2f, 192f), stone);
                CreateDungeonShellPart(root.transform, "SM_ENV_AD_Shell_FarCap",
                    new Vector3(0f, 4f, 194f), new Vector3(24f, 8f, 4f), stone);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateDungeonShellPart(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static void Add(
            IDictionary<string, Material> materials,
            string key,
            string folder,
            Color color,
            float metallic,
            float roughness,
            float emission = 0.0f)
        {
            string targetFolder = $"{MaterialRoot}/{folder}";
            EnsureFolder(MaterialRoot, folder);
            string path = $"{targetFolder}/MAT_ENV_{key}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            material.enableInstancing = true;
            SetIfPresent(material, "_BaseColor", color);
            SetIfPresent(material, "_Metallic", metallic);
            SetIfPresent(material, "_Smoothness", 1.0f - roughness);
            if (emission > 0.0f)
            {
                Color emissionColor = color * emission;
                SetIfPresent(material, "_EmissionColor", emissionColor);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(material);
            materials[key] = material;
        }

        private static void SetIfPresent(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void BuildThemePrefabs(ThemeSpec theme, IReadOnlyDictionary<string, Material> materials)
        {
            string artFolder = $"{ArtRoot}/{theme.Folder}";
            string prefabFolder = $"{PrefabRoot}/{theme.Folder}";
            EnsureFolder(PrefabRoot, theme.Folder);
            string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { artFolder });

            foreach (string guid in modelGuids)
            {
                string modelPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null)
                {
                    continue;
                }

                GameObject root = new GameObject(model.name);
                try
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
                    if (instance == null)
                    {
                        throw new InvalidOperationException($"Could not instantiate {modelPath}.");
                    }

                    instance.name = "VisualMesh";
                    instance.transform.SetParent(root.transform, false);
                    ReassignMaterials(instance, materials);
                    ConfigureLodGroup(root);

                    if (root.GetComponentsInChildren<Collider>(true).Length > 0 ||
                        root.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                    {
                        throw new InvalidOperationException($"Environment model {modelPath} imported gameplay physics.");
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, $"{prefabFolder}/{model.name}.prefab");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void ReassignMaterials(GameObject root, IReadOnlyDictionary<string, Material> materials)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string objectName = renderer.gameObject.name;
                int separator = objectName.IndexOf("__", StringComparison.Ordinal);
                string key = separator > 0 ? objectName.Substring(0, separator) : string.Empty;
                if (materials.TryGetValue(key, out Material replacement))
                {
                    Material[] replacements = Enumerable.Repeat(replacement, Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
                    renderer.sharedMaterials = replacements;
                }

                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static void ConfigureLodGroup(GameObject root)
        {
            LODGroup[] importedGroups = root.GetComponentsInChildren<LODGroup>(true);
            if (importedGroups.Length > 0)
            {
                foreach (LODGroup importedGroup in importedGroups)
                {
                    importedGroup.fadeMode = LODFadeMode.CrossFade;
                    importedGroup.animateCrossFading = true;
                    importedGroup.RecalculateBounds();
                }

                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Renderer[] high = renderers.Where(item => item.gameObject.name.Contains("LOD0", StringComparison.Ordinal)).ToArray();
            Renderer[] low = renderers.Where(item => item.gameObject.name.Contains("LOD1", StringComparison.Ordinal)).ToArray();
            if (high.Length == 0 || low.Length == 0)
            {
                return;
            }

            LODGroup group = root.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.35f, high),
                new LOD(0.06f, low)
            });
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.RecalculateBounds();
        }

        private static void BuildLightingRig(ThemeSpec theme)
        {
            GameObject root = new GameObject($"ENV_{theme.Code}_LightingRig");
            try
            {
                GameObject key = new GameObject("Key Light");
                key.transform.SetParent(root.transform, false);
                key.transform.localRotation = Quaternion.Euler(theme.Code == "AD" ? 38f : 52f, theme.Code == "WP" ? -28f : 34f, 0f);
                Light light = key.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = theme.Code == "AD" ? new Color(1.0f, 0.54f, 0.26f) : Color.Lerp(Color.white, theme.EdgeColor, 0.18f);
                light.intensity = theme.Code == "AD" ? 1.1f : 1.25f;
                light.shadows = LightShadows.Soft;

                EnsureFolder(PrefabRoot, theme.Folder);
                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabRoot}/{theme.Folder}/ENV_{theme.Code}_LightingRig.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildVfxPrefab(ThemeSpec theme, IReadOnlyDictionary<string, Material> materials)
        {
            GameObject root = new GameObject($"ENV_{theme.Code}_AmbientVFX");
            try
            {
                ParticleSystem particles = root.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = particles.main;
                main.loop = true;
                main.playOnAwake = true;
                main.startLifetime = theme.Code == "WP" ? 2.2f : 5.0f;
                main.startSpeed = theme.Code == "AD" ? 0.25f : 0.10f;
                main.startSize = theme.Code == "WP" ? 0.10f : 0.06f;
                main.maxParticles = 160;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(theme.EdgeColor * 0.9f, Color.white);

                ParticleSystem.EmissionModule emission = particles.emission;
                emission.rateOverTime = theme.Code == "WP" ? 24f : 12f;
                ParticleSystem.ShapeModule shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(18f, 4f, 48f);

                ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = materials[theme.Code == "TR" ? "TR_Emissive" : $"{theme.Code}_TrackEdge"];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                EnsureFolder(PrefabRoot, theme.Folder);
                PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabRoot}/{theme.Folder}/ENV_{theme.Code}_AmbientVFX.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreatePreviewScene(IReadOnlyDictionary<string, Material> materials)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = CreateCamera();

            for (int index = 0; index < Themes.Length; index++)
            {
                ThemeSpec theme = Themes[index];
                GameObject stage = new GameObject(theme.RootName);
                stage.transform.position = new Vector3(index * 120f, 0f, 0f);
                BuildStage(stage.transform, theme, materials);
                stage.SetActive(index == 0);
            }

            camera.transform.position = new Vector3(0f, 7.5f, -12f);
            camera.transform.LookAt(new Vector3(0f, 1.4f, 42f));
            ApplyRenderSettings(Themes[0], camera);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, PreviewScenePath);
        }

        private static void BuildStage(
            Transform stage,
            ThemeSpec theme,
            IReadOnlyDictionary<string, Material> materials)
        {
            InstantiatePrefab($"{PrefabRoot}/{theme.Folder}/ENV_{theme.Code}_LightingRig.prefab", stage, Vector3.zero);
            GameObject vfx = InstantiatePrefab($"{PrefabRoot}/{theme.Folder}/ENV_{theme.Code}_AmbientVFX.prefab", stage, new Vector3(0f, 3f, 36f));
            if (vfx != null)
            {
                vfx.name = "Ambient VFX";
            }

            if (theme.Code == "TR")
            {
                BuildTunnelStage(stage, theme, materials);
                return;
            }

            if (theme.Code == "AD" && !theme.SkyboxTexturePath.EndsWith("_v02.png", StringComparison.Ordinal))
            {
                InstantiatePrefab($"{PrefabRoot}/AncientDungeon/ENV_AD_DungeonShell.prefab", stage, Vector3.zero);
            }

            string[] variants =
            {
                $"ENV_{theme.Code}_Roadside_A",
                $"ENV_{theme.Code}_Roadside_B",
                $"ENV_{theme.Code}_Roadside_C"
            };

            for (int index = 0; index < PreviewTrackSequence.Length; index++)
            {
                Vector3 position = new Vector3(0f, 0f, index * 24f);
                GameObject track = InstantiatePrefab($"{TrackPrefabRoot}/{PreviewTrackSequence[index]}.prefab", stage, position);
                ApplyTrackMaterials(track, theme, materials);
                if (theme.Code != "FS")
                {
                    InstantiatePrefab($"{PrefabRoot}/{theme.Folder}/{variants[index % variants.Length]}.prefab", stage, position);
                }
            }

            if (theme.Code == "FS")
            {
                EndlessRollball.Editor.Environment.BrokenRoadEncounterPreviewBuilder
                    .BuildImportedStreetScenery(stage);
            }
            else
            {
                InstantiatePrefab($"{PrefabRoot}/{theme.Folder}/ENV_{theme.Code}_Backdrop.prefab", stage, Vector3.zero);
            }
        }

        private static void BuildTunnelStage(
            Transform stage,
            ThemeSpec theme,
            IReadOnlyDictionary<string, Material> materials)
        {
            for (int index = 0; index < 8; index++)
            {
                Vector3 position = new Vector3(0f, 0f, index * 24f);
                GameObject track = InstantiatePrefab($"{TrackPrefabRoot}/TRK_Tunnel_Straight.prefab", stage, position);
                ApplyTrackMaterials(track, theme, materials);
                string decoration = index == 0 ? "ENV_TR_Entrance" : index == 7 ? "ENV_TR_Exit" : "ENV_TR_Middle";
                InstantiatePrefab($"{PrefabRoot}/{theme.Folder}/{decoration}.prefab", stage, position);
            }

            InstantiatePrefab($"{PrefabRoot}/{theme.Folder}/ENV_TR_PortalFrame.prefab", stage, Vector3.zero);
        }

        private static void ApplyTrackMaterials(
            GameObject track,
            ThemeSpec theme,
            IReadOnlyDictionary<string, Material> materials)
        {
            if (track == null)
            {
                return;
            }

            foreach (Renderer renderer in track.GetComponentsInChildren<Renderer>(true))
            {
                bool obstacle = IsUnderNamedParent(renderer.transform, "ObstacleVisualMesh") ||
                                renderer.gameObject.name.Contains("Obstacle", StringComparison.OrdinalIgnoreCase) ||
                                renderer.gameObject.name.Contains("Wall", StringComparison.OrdinalIgnoreCase) && theme.Code != "TR";
                string key;
                if (obstacle)
                {
                    key = renderer.gameObject.name.Contains("Moving", StringComparison.OrdinalIgnoreCase)
                        ? "Shared_HazardOrange"
                        : "Shared_HazardRed";
                }
                else if (renderer.gameObject.name.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
                         renderer.gameObject.name.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                         renderer.gameObject.name.Contains("Light", StringComparison.OrdinalIgnoreCase) ||
                         renderer.gameObject.name.Contains("Emitter", StringComparison.OrdinalIgnoreCase))
                {
                    key = $"{theme.Code}_TrackEdge";
                }
                else if (renderer.gameObject.name.Contains("Support", StringComparison.OrdinalIgnoreCase) ||
                         renderer.gameObject.name.Contains("Beam", StringComparison.OrdinalIgnoreCase) ||
                         renderer.gameObject.name.Contains("Leg", StringComparison.OrdinalIgnoreCase))
                {
                    key = $"{theme.Code}_TrackSupport";
                }
                else
                {
                    key = $"{theme.Code}_TrackSurface";
                }

                renderer.sharedMaterials = Enumerable.Repeat(materials[key], Math.Max(1, renderer.sharedMaterials.Length)).ToArray();
            }
        }

        private static bool IsUnderNamedParent(Transform item, string parentName)
        {
            while (item != null)
            {
                if (item.name == parentName)
                {
                    return true;
                }

                item = item.parent;
            }

            return false;
        }

        private static GameObject InstantiatePrefab(string path, Transform parent, Vector3 localPosition)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                throw new FileNotFoundException($"Required preview prefab was not generated: {path}");
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate {path}.");
            }

            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private static Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("EnvironmentPreviewCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 450f;
            camera.allowHDR = true;
            return camera;
        }

        private static void CaptureAll()
        {
            Scene scene = EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                throw new InvalidOperationException("EnvironmentPreview has no camera.");
            }

            Dictionary<string, GameObject> roots = scene.GetRootGameObjects()
                .Where(item => item.name.StartsWith("Preview_", StringComparison.Ordinal))
                .ToDictionary(item => item.name, item => item, StringComparer.Ordinal);
            string diskFolder = Path.GetFullPath(CaptureFolder);
            Directory.CreateDirectory(diskFolder);
            List<string> profileRows = new List<string>
            {
                "theme,resolution,active_track_segments,active_scenery_modules,frames,average_render_ms,estimated_fps"
            };

            foreach (ThemeSpec theme in Themes)
            {
                foreach (GameObject root in roots.Values)
                {
                    root.SetActive(root.name == theme.RootName);
                }

                GameObject active = roots[theme.RootName];
                Vector3 stagePosition = active.transform.position;
                camera.transform.position = stagePosition + new Vector3(0f, theme.Code == "TR" ? 3.2f : 7.5f, -12f);
                camera.transform.LookAt(stagePosition + new Vector3(0f, theme.Code == "TR" ? 2.0f : 1.4f, theme.Code == "TR" ? 32f : 42f));
                ApplyRenderSettings(theme, camera);
                RenderCamera(camera, Path.Combine(diskFolder, theme.CaptureName));
                (double averageMs, double estimatedFps) = ProfileCamera(camera, 60);
                profileRows.Add(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0},1920x1080,8,8,60,{1:F3},{2:F1}",
                    theme.Folder,
                    averageMs,
                    estimatedFps));
                Debug.Log($"Environment preview profile {theme.Folder}: {averageMs:F3} ms, estimated {estimatedFps:F1} FPS at 1920x1080.");
            }

            File.WriteAllLines(Path.GetFullPath("Artifacts/EnvironmentPerformance.csv"), profileRows);

            foreach (GameObject root in roots.Values)
            {
                root.SetActive(root.name == Themes[0].RootName);
            }

            ApplyRenderSettings(Themes[0], camera);
            camera.transform.position = new Vector3(0f, 7.5f, -12f);
            camera.transform.LookAt(new Vector3(0f, 1.4f, 42f));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ApplyRenderSettings(ThemeSpec theme, Camera camera)
        {
            camera.backgroundColor = theme.BackgroundColor;
            Material skybox = string.IsNullOrEmpty(theme.SkyboxTexturePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Material>(theme.SkyboxMaterialPath);
            camera.clearFlags = skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            RenderSettings.skybox = skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = theme.FogColor;
            RenderSettings.fogStartDistance = theme.Code == "TR" ? 35f : 90f;
            RenderSettings.fogEndDistance = theme.Code == "TR" ? 165f : 280f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(theme.BackgroundColor, Color.white, theme.Code == "WP" ? 0.55f : 0.18f);
            RenderSettings.ambientEquatorColor = Color.Lerp(theme.FogColor, theme.SurfaceColor, 0.35f);
            RenderSettings.ambientGroundColor = theme.BackgroundColor * 0.55f;
            RenderSettings.ambientIntensity = theme.Code == "AD" ? 0.75f : 1.0f;
            if (theme.SkyboxTexturePath != null && theme.SkyboxTexturePath.EndsWith("_v02.png", StringComparison.Ordinal))
            {
                RenderSettings.ambientSkyColor = theme.Code == "AD" ? new Color(.27f, .32f, .35f) : new Color(.57f, .67f, .78f);
                RenderSettings.ambientEquatorColor = theme.Code == "AD" ? new Color(.3f, .25f, .18f) : new Color(.38f, .48f, .6f);
                RenderSettings.fogColor = theme.Code == "AD" ? new Color(.12f, .18f, .2f) : new Color(.45f, .64f, .77f);
            }
        }

        private static void RenderCamera(Camera camera, string outputPath)
        {
            RenderTexture target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;

            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        private static (double AverageMilliseconds, double EstimatedFps) ProfileCamera(Camera camera, int frameCount)
        {
            RenderTexture target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            Texture2D syncPixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;

            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                for (int index = 0; index < 5; index++)
                {
                    camera.Render();
                }

                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int index = 0; index < frameCount; index++)
                {
                    camera.Render();
                }

                // A readback ensures the queued GPU work is complete before timing ends.
                syncPixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                syncPixel.Apply();
                stopwatch.Stop();
                double averageMilliseconds = stopwatch.Elapsed.TotalMilliseconds / frameCount;
                double estimatedFps = averageMilliseconds > 0.0 ? 1000.0 / averageMilliseconds : double.PositiveInfinity;
                return (averageMilliseconds, estimatedFps);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(syncPixel);
            }
        }

        private static void ConfigureModelImporters()
        {
            foreach (ThemeSpec theme in Themes)
            {
                string folder = $"{ArtRoot}/{theme.Folder}";
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                    {
                        continue;
                    }

                    bool changed = importer.importAnimation ||
                                   importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                                   !Mathf.Approximately(importer.globalScale, 1f);
                    importer.importAnimation = false;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.globalScale = 1f;
                    importer.isReadable = false;
                    importer.meshCompression = ModelImporterMeshCompression.Low;
                    importer.optimizeMeshPolygons = true;
                    importer.optimizeMeshVertices = true;
                    importer.generateSecondaryUV = true;
                    if (changed)
                    {
                        importer.SaveAndReimport();
                    }
                }
            }
        }

        private static bool RequiredModelsExist()
        {
            return Themes.All(theme =>
                Directory.Exists($"{ArtRoot}/{theme.Folder}") &&
                Directory.GetFiles($"{ArtRoot}/{theme.Folder}", "*.fbx").Length >= (theme.Code == "TR" ? 4 : 12));
        }

        private static bool OutputsExist()
        {
            return File.Exists(PreviewScenePath) && Themes.All(theme =>
                File.Exists(Path.Combine(CaptureFolder, theme.CaptureName)) &&
                (theme.Code == "TR" ||
                 File.Exists($"{PrefabRoot}/{theme.Folder}/ENV_{theme.Code}_Roadside_A.prefab")));
        }

        private static void EnsureFolders()
        {
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root + "/Materials", "Environment");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root + "/Prefabs", "Environment");
            foreach (ThemeSpec theme in Themes)
            {
                EnsureFolder(MaterialRoot, theme.Folder);
                EnsureFolder(PrefabRoot, theme.Folder);
            }
            EnsureFolder(MaterialRoot, "Shared");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private sealed class ThemeSpec
        {
            public ThemeSpec(
                string code,
                string folder,
                string rootName,
                Color backgroundColor,
                Color fogColor,
                Color surfaceColor,
                Color edgeColor,
                Color supportColor,
                string skyboxTexturePath,
                Color skyboxTint,
                float skyboxExposure,
                string captureName)
            {
                Code = code;
                Folder = folder;
                RootName = rootName;
                BackgroundColor = backgroundColor;
                FogColor = fogColor;
                SurfaceColor = surfaceColor;
                EdgeColor = edgeColor;
                SupportColor = supportColor;
                SkyboxTexturePath = skyboxTexturePath;
                SkyboxTint = skyboxTint;
                SkyboxExposure = skyboxExposure;
                CaptureName = captureName;
            }

            public string Code { get; }
            public string Folder { get; }
            public string RootName { get; }
            public Color BackgroundColor { get; }
            public Color FogColor { get; }
            public Color SurfaceColor { get; }
            public Color EdgeColor { get; }
            public Color SupportColor { get; }
            public string SkyboxTexturePath { get; }
            public Color SkyboxTint { get; }
            public float SkyboxExposure { get; }
            public string SkyboxMaterialPath =>
                $"{Root}/Skybox/MAT_ENV_{Code}_Skybox_v02.mat";
            public string CaptureName { get; }
        }
    }
}
