using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EndlessRollball.EnvironmentVisuals;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor.Environment
{
    public static class BrokenRoadEncounterPreviewBuilder
    {
        private const string ScenePath = "Assets/Scenes/BrokenRoadEncounterPreview.unity";
        private const string VisualPrefabPath =
            "Assets/EndlessRollball/Prefabs/Environment/FuturisticStreet/ENV_FS_BrokenRoadVisual.prefab";
        private const string GapPrefabPath = "Assets/EndlessRollball/Prefabs/Track/TRK_Gap.prefab";
        private const string WidePrefabPath = "Assets/EndlessRollball/Prefabs/Track/TRK_Straight_Wide.prefab";
        private const string MaterialFolder =
            "Assets/EndlessRollball/Materials/Environment/FuturisticStreet/BrokenRoad";
        private const string TextureFolder =
            "Assets/EndlessRollball/Art/Environment/FuturisticStreet/BrokenRoad";
        private const string ImportedMaterialFolder =
            "Assets/EndlessRollball/Materials/Environment/FuturisticStreet/Imported";
        private const string ImportedCityModelPath =
            "Assets/EndlessRollball/Prefabs/Environment/Street/source/city.fbx";
        private const string ImportedSmallTreesModelPath =
            "Assets/EndlessRollball/Prefabs/Environment/Trees/small-trees/source/Small Trees.fbx";
        private const string ImportedPalmTreesModelPath =
            "Assets/EndlessRollball/Prefabs/Environment/Trees/palm-trees/source/PalmTrees.fbx";
        private const string CaptureFolder = "Artifacts/BrokenRoadEncounter";
        private const string SessionBuildVersionKey = "EndlessRollball.BrokenRoadPreview.BuildVersion";
        private const int CurrentBuildVersion = 6;

        private static readonly Color Asphalt = Html("#444B50");
        private static readonly Color AsphaltDark = Html("#23282C");
        private static readonly Color ExposedRoad = Html("#6B5543");
        private static readonly Color Teal = Html("#1596A8");
        private static readonly Color Blue = Html("#2F78B7");
        private static readonly Color Yellow = Html("#F4C542");
        private static readonly Color Hazard = Html("#F05A35");

        // Preview generation is manual-only: never switch scenes on editor startup
        // or script reload. Use the Tools menu to build a preview explicitly.
        private static void ScheduleInitialBuild()
        {
            bool currentVersionBuilt = SessionState.GetInt(SessionBuildVersionKey, 0) >= CurrentBuildVersion;
            if (Application.isBatchMode || currentVersionBuilt)
            {
                return;
            }

            SessionState.SetInt(SessionBuildVersionKey, CurrentBuildVersion);
            EditorApplication.delayCall += BuildInitialPreviewWhenSafe;
        }

        [MenuItem("Tools/Endless Rollball/Broken Road/Build Preview")]
        public static void BuildPreview()
        {
            EnsureFolders();
            MaterialSet materials = CreateMaterials();
            Dictionary<Material, Material> importedMaterials = CreateImportedUrpMaterials();
            CreateVisualPrefab(materials);
            CreatePreviewScene(importedMaterials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Broken-road graybox preview built without modifying GameplayScene or the TRK_Gap prefab.");
        }

        [MenuItem("Tools/Endless Rollball/Broken Road/Build And Capture Review")]
        public static void BuildAndCaptureReview()
        {
            BuildPreview();
            CaptureReview();
        }

        [MenuItem("Tools/Endless Rollball/Broken Road/Capture Review")]
        public static void CaptureReview()
        {
            if (!File.Exists(Path.GetFullPath(ScenePath)))
            {
                BuildPreview();
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BrokenRoadEncounterController controller = UnityEngine.Object.FindAnyObjectByType<BrokenRoadEncounterController>();
            Camera camera = UnityEngine.Object.FindAnyObjectByType<Camera>();

            if (!scene.IsValid() || controller == null || camera == null)
            {
                throw new InvalidOperationException("Broken-road preview scene is incomplete.");
            }

            string diskFolder = Path.GetFullPath(CaptureFolder);
            Directory.CreateDirectory(diskFolder);

            CaptureState(controller, camera, BrokenRoadEncounterState.Armed, 1.2f,
                Path.Combine(diskFolder, "ER_FS_BrokenRoad_01_Armed.png"));
            CaptureState(controller, camera, BrokenRoadEncounterState.Exploding, 0.18f,
                Path.Combine(diskFolder, "ER_FS_BrokenRoad_02_Explosion.png"));
            CaptureState(controller, camera, BrokenRoadEncounterState.GapRevealed, 0.7f,
                Path.Combine(diskFolder, "ER_FS_BrokenRoad_03_Revealed.png"));
            CaptureState(controller, camera, BrokenRoadEncounterState.Burning, 1.8f,
                Path.Combine(diskFolder, "ER_FS_BrokenRoad_04_Burning.png"));

            controller.ApplyReviewState(BrokenRoadEncounterState.Armed, 1.2f);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.Refresh();
            Debug.Log($"Broken-road review captures written to {diskFolder}.");
        }

        private static void BuildInitialPreviewWhenSafe()
        {
            if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += BuildInitialPreviewWhenSafe;
                return;
            }

            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty)
            {
                Debug.LogWarning(
                    "Broken-road preview assets are ready to build, but the current scene has unsaved changes. " +
                    "Save it, then use Tools/Endless Rollball/Broken Road/Build And Capture Review.");
                return;
            }

            try
            {
                BuildAndCaptureReview();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void CreatePreviewScene(IReadOnlyDictionary<Material, Material> importedMaterials)
        {
            GameObject gapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GapPrefabPath);
            GameObject widePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WidePrefabPath);
            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefabPath);

            if (gapPrefab == null || widePrefab == null || visualPrefab == null)
            {
                throw new InvalidOperationException("Required track or broken-road visual prefab is missing.");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject trackRoot = new GameObject("BrokenRoadPreviewTrack");
            InstantiateTrack(widePrefab, trackRoot.transform, -24f, "Approach_Recovery");
            InstantiateTrack(widePrefab, trackRoot.transform, 0f, "Approach_Warning");
            GameObject gapInstance = InstantiateTrack(gapPrefab, trackRoot.transform, 24f, "Authoritative_TRK_Gap");
            InstantiateTrack(widePrefab, trackRoot.transform, 48f, "Landing_Recovery");
            CreateImportedStreetScenery(null, importedMaterials);

            TrackSegment gap = gapInstance.GetComponent<TrackSegment>();
            if (gap == null || gap.SegmentId != "TRK_Gap" || !gap.RequiredJump)
            {
                throw new InvalidOperationException("Preview did not instantiate the validated TRK_Gap contract.");
            }

            GameObject encounterRoot = new GameObject("BrokenRoadEncounterPrototype");
            encounterRoot.transform.position = new Vector3(0f, 0f, 24f);

            BoxCollider trigger = encounterRoot.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.center = new Vector3(0f, 2.5f, -14.5f);
            trigger.size = new Vector3(9f, 5f, 4f);

            GameObject visual = PrefabUtility.InstantiatePrefab(visualPrefab) as GameObject;
            if (visual == null)
            {
                throw new InvalidOperationException("Failed to instantiate broken-road visual prefab.");
            }

            visual.name = "ENV_FS_BrokenRoadVisual";
            visual.transform.SetParent(encounterRoot.transform, false);

            Transform[] covers = visual.GetComponentsInChildren<Transform>(true)
                .Where(item => item.name.StartsWith("CoverSlab_", StringComparison.Ordinal))
                .OrderBy(item => item.name)
                .ToArray();
            ParticleSystem[] warnings = FindParticles(visual, "FX_WARN_");
            ParticleSystem[] bursts = FindParticles(visual, "FX_BURST_");
            ParticleSystem[] burning = FindParticles(visual, "FX_BURN_");

            Vector3[] offsets =
            {
                new Vector3(-2.2f, -3.4f, 0.8f),
                new Vector3(-0.7f, -4.2f, -0.3f),
                new Vector3(0.8f, -4.0f, 0.25f),
                new Vector3(2.3f, -3.2f, -0.7f)
            };
            Vector3[] rotations =
            {
                new Vector3(25f, -18f, 55f),
                new Vector3(-20f, 10f, -35f),
                new Vector3(30f, -8f, 42f),
                new Vector3(-24f, 20f, -58f)
            };

            BrokenRoadEncounterController controller = encounterRoot.AddComponent<BrokenRoadEncounterController>();
            controller.ConfigureForPrototype(
                trigger,
                covers,
                offsets,
                rotations,
                0.6f,
                warnings,
                bursts,
                burning);

            CreatePlayer();
            CreateReviewCamera();
            CreateLighting();
            CreateSceneLabels();

            if (controller.GetWarningSeconds() < BrokenRoadEncounterController.MinimumWarningSeconds)
            {
                throw new InvalidOperationException("Approach trigger does not provide the required warning time.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void CreateVisualPrefab(MaterialSet materials)
        {
            GameObject root = new GameObject("ENV_FS_BrokenRoadVisual");

            try
            {
                GameObject coverRoot = new GameObject("VisualRoadCover");
                coverRoot.transform.SetParent(root.transform, false);

                for (int index = 0; index < 4; index++)
                {
                    float x = -3.3f + index * 2.2f;
                    GameObject slab = CreateVisualCube(
                        $"CoverSlab_{index + 1:00}",
                        coverRoot.transform,
                        new Vector3(x, 0.13f, 12f),
                        new Vector3(2.12f, 0.28f, 9f),
                        materials.Asphalt);
                    slab.transform.localRotation = Quaternion.Euler(
                        index % 2 == 0 ? 0.35f : -0.35f,
                        0f,
                        index % 2 == 0 ? 0.45f : -0.45f);

                    if (index == 0 || index == 3)
                    {
                        float edgeX = index == 0 ? -0.94f : 0.94f;
                        CreateVisualCube(
                            "YellowRouteEdge",
                            slab.transform,
                            new Vector3(edgeX, 0.56f, 0f),
                            new Vector3(0.12f, 0.05f, 0.98f),
                            materials.Yellow,
                            coordinatesAreNormalized: true);
                    }
                }

                CreateBrokenEdges(root.transform, materials);
                CreateDamagedCar(root.transform, materials);
                CreateWarningHardware(root.transform, materials);
                CreateParticles(root.transform, materials);

                if (root.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    root.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                    root.GetComponentsInChildren<BrokenRoadEncounterController>(true).Length != 0)
                {
                    throw new InvalidOperationException("Broken-road visual prefab contains gameplay components.");
                }

                PrefabUtility.SaveAsPrefabAsset(root, VisualPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateBrokenEdges(Transform parent, MaterialSet materials)
        {
            GameObject edgeRoot = new GameObject("BrokenAsphaltEdges");
            edgeRoot.transform.SetParent(parent, false);

            float[] xPositions = { -3.6f, -1.8f, 0f, 1.8f, 3.6f };
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? 10.55f : 16.45f;
                for (int index = 0; index < xPositions.Length; index++)
                {
                    float jag = ((index + side) % 2 == 0 ? 0.22f : -0.18f);
                    GameObject asphaltPiece = CreateVisualCube(
                        $"BrokenLip_{side}_{index}",
                        edgeRoot.transform,
                        new Vector3(xPositions[index], -0.04f, z + jag),
                        new Vector3(1.72f, 0.25f, 0.55f),
                        materials.AsphaltDark);
                    asphaltPiece.transform.localRotation = Quaternion.Euler(0f, (index - 2) * 3f, jag * 12f);

                    CreateVisualCube(
                        $"ExposedLayer_{side}_{index}",
                        edgeRoot.transform,
                        new Vector3(xPositions[index], -0.28f, z + (side == 0 ? 0.18f : -0.18f)),
                        new Vector3(1.72f, 0.22f, 0.45f),
                        materials.ExposedRoad);
                }
            }
        }

        private static void CreateDamagedCar(Transform parent, MaterialSet materials)
        {
            GameObject car = new GameObject("DamagedFuturisticCar");
            car.transform.SetParent(parent, false);
            car.transform.localPosition = new Vector3(6.85f, 0.52f, 13.3f);
            car.transform.localRotation = Quaternion.Euler(0f, -18f, -5f);

            CreateVisualCube("CarBody", car.transform, Vector3.zero, new Vector3(2.25f, 0.65f, 4.2f), materials.Teal);
            GameObject cabin = CreateVisualCube(
                "CrushedCabin",
                car.transform,
                new Vector3(0f, 0.62f, -0.15f),
                new Vector3(1.8f, 0.72f, 2.1f),
                materials.Blue);
            cabin.transform.localRotation = Quaternion.Euler(0f, 0f, -6f);
            CreateVisualCube(
                "ScorchPanel",
                car.transform,
                new Vector3(-0.55f, 0.18f, 0.65f),
                new Vector3(0.9f, 0.12f, 1.25f),
                materials.AsphaltDark);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    GameObject wheel = CreateVisualCylinder(
                        $"Wheel_{side}_{axle}",
                        car.transform,
                        new Vector3(side * 1.08f, -0.2f, axle * 1.32f),
                        new Vector3(0.43f, 0.2f, 0.43f),
                        materials.AsphaltDark);
                    wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            }

            CreateVisualCube(
                "HazardLightLeft",
                car.transform,
                new Vector3(-0.62f, 0.02f, -2.12f),
                new Vector3(0.35f, 0.18f, 0.08f),
                materials.Hazard);
            CreateVisualCube(
                "HazardLightRight",
                car.transform,
                new Vector3(0.62f, 0.02f, -2.12f),
                new Vector3(0.35f, 0.18f, 0.08f),
                materials.Hazard);
        }

        private static void CreateWarningHardware(Transform parent, MaterialSet materials)
        {
            GameObject warnings = new GameObject("WarningHardware");
            warnings.transform.SetParent(parent, false);

            foreach (float x in new[] { -5.8f, 5.8f })
            {
                CreateVisualCylinder(
                    $"WarningPost_{x}",
                    warnings.transform,
                    new Vector3(x, 0.6f, -14.5f),
                    new Vector3(0.14f, 0.6f, 0.14f),
                    materials.AsphaltDark);
                CreateVisualCylinder(
                    $"WarningBeacon_{x}",
                    warnings.transform,
                    new Vector3(x, 1.28f, -14.5f),
                    new Vector3(0.28f, 0.18f, 0.28f),
                    materials.Hazard);
                CreateVisualCube(
                    $"WarningChevron_{x}",
                    warnings.transform,
                    new Vector3(x, 0.72f, -14.5f),
                    new Vector3(0.55f, 0.28f, 0.08f),
                    materials.Yellow);
            }
        }

        private static void CreateParticles(Transform parent, MaterialSet materials)
        {
            Vector3 carFx = new Vector3(6.15f, 1.35f, 13.4f);
            Vector3 roadFx = new Vector3(3.9f, 0.55f, 13.5f);

            CreateParticleSystem("FX_WARN_Fire", parent, carFx, materials.FireParticle,
                true, 12, 0.55f, 0.9f, 0.42f, 7f, 0, ParticleSystemShapeType.Cone);
            CreateParticleSystem("FX_WARN_Smoke", parent, carFx + Vector3.up * 0.35f, materials.SmokeParticle,
                true, 18, 1.8f, 0.65f, 0.85f, 5f, 0, ParticleSystemShapeType.Cone);

            CreateParticleSystem("FX_BURST_Flash", parent, roadFx + Vector3.up * 0.8f, materials.FlashParticle,
                false, 4, 0.16f, 0.2f, 2.8f, 0f, 4, ParticleSystemShapeType.Sphere);
            ParticleSystem fireball = CreateParticleSystem(
                "FX_BURST_Fireball", parent, roadFx + Vector3.up * 0.8f, materials.FireAnimationParticle,
                false, 28, 0.55f, 3.2f, 1.25f, -0.2f, 28, ParticleSystemShapeType.Sphere);
            ParticleSystem.TextureSheetAnimationModule sheet = fireball.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 4;
            sheet.numTilesY = 1;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 1f);

            CreateParticleSystem("FX_BURST_Sparks", parent, roadFx + Vector3.up * 0.65f, materials.SparkParticle,
                false, 55, 0.8f, 7.5f, 0.09f, 1.4f, 55, ParticleSystemShapeType.Cone,
                ParticleSystemRenderMode.Stretch);
            CreateParticleSystem("FX_BURST_Dust", parent, roadFx, materials.DustParticle,
                false, 40, 1.25f, 2.2f, 1.1f, 0.3f, 40, ParticleSystemShapeType.Hemisphere);
            CreateParticleSystem("FX_BURST_Debris", parent, roadFx + Vector3.up * 0.35f, materials.AsphaltDark,
                false, 24, 1.1f, 5.2f, 0.22f, 2.2f, 24, ParticleSystemShapeType.Hemisphere,
                ParticleSystemRenderMode.Mesh, materials.DebrisMesh);

            CreateParticleSystem("FX_BURN_Fire", parent, carFx, materials.FireParticle,
                true, 25, 0.65f, 1.05f, 0.55f, 7f, 0, ParticleSystemShapeType.Cone);
            CreateParticleSystem("FX_BURN_Smoke", parent, carFx + Vector3.up * 0.5f, materials.SmokeParticle,
                true, 30, 2.4f, 0.8f, 1.05f, 6f, 0, ParticleSystemShapeType.Cone);
        }

        private static ParticleSystem CreateParticleSystem(
            string name,
            Transform parent,
            Vector3 localPosition,
            Material material,
            bool loop,
            int maxParticles,
            float lifetime,
            float speed,
            float size,
            float gravity,
            short burstCount,
            ParticleSystemShapeType shapeType,
            ParticleSystemRenderMode renderMode = ParticleSystemRenderMode.Billboard,
            Mesh mesh = null)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            ParticleSystem particles = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.loop = loop;
            main.duration = loop ? 2f : 1.5f;
            main.playOnAwake = loop;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime * 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.65f, speed * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.65f, size * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = gravity;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = true;
            if (loop)
            {
                emission.rateOverTime = Mathf.Max(1f, maxParticles / Mathf.Max(1f, lifetime * 2f));
            }
            else
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });
            }

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = shapeType;
            shape.radius = shapeType == ParticleSystemShapeType.Cone ? 0.35f : 0.75f;
            shape.angle = 24f;
            shape.rotation = name.Contains("Sparks", StringComparison.Ordinal)
                ? new Vector3(-70f, 0f, 0f)
                : Vector3.zero;

            ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.75f, 0.68f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1.25f));

            ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = renderMode;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            if (renderMode == ParticleSystemRenderMode.Stretch)
            {
                renderer.lengthScale = 2.5f;
                renderer.velocityScale = 0.15f;
            }
            else if (renderMode == ParticleSystemRenderMode.Mesh)
            {
                renderer.mesh = mesh;
            }

            return particles;
        }

        private static MaterialSet CreateMaterials()
        {
            Texture2D atlas = CreateOrUpdateAtlas();
            Mesh debrisMesh = CreateOrUpdateDebrisMesh();

            Material asphalt = CreateLitMaterial("MAT_ENV_FS_Broken_Asphalt", Asphalt);
            Material asphaltDark = CreateLitMaterial("MAT_ENV_FS_Broken_AsphaltDark", AsphaltDark);
            Material exposed = CreateLitMaterial("MAT_ENV_FS_Broken_ExposedLayer", ExposedRoad);
            Material teal = CreateLitMaterial("MAT_ENV_FS_Broken_CarTeal", Teal);
            Material blue = CreateLitMaterial("MAT_ENV_FS_Broken_CarBlue", Blue);
            Material yellow = CreateLitMaterial("MAT_ENV_FS_Broken_RouteYellow", Yellow, true);
            Material hazard = CreateLitMaterial("MAT_ENV_FS_Broken_Hazard", Hazard, true);

            return new MaterialSet(
                asphalt,
                asphaltDark,
                exposed,
                teal,
                blue,
                yellow,
                hazard,
                CreateParticleMaterial("MAT_ENV_FS_FX_Flash", atlas, 0, new Color(1f, 0.88f, 0.38f, 1f), true),
                CreateParticleMaterial("MAT_ENV_FS_FX_Fire", atlas, 1, new Color(1f, 0.34f, 0.04f, 1f), true),
                CreateParticleMaterial("MAT_ENV_FS_FX_FireAnimation", atlas, -1, Color.white, true),
                CreateParticleMaterial("MAT_ENV_FS_FX_Smoke", atlas, 3, new Color(0.22f, 0.25f, 0.27f, 0.8f), false),
                CreateParticleMaterial("MAT_ENV_FS_FX_Dust", atlas, 2, new Color(0.45f, 0.34f, 0.24f, 0.7f), false),
                CreateParticleMaterial("MAT_ENV_FS_FX_Spark", atlas, 0, new Color(1f, 0.42f, 0.04f, 1f), true),
                debrisMesh);
        }

        private static Dictionary<Material, Material> CreateImportedUrpMaterials()
        {
            Dictionary<Material, Material> replacements = new Dictionary<Material, Material>();
            AddImportedMaterialSet(
                ImportedCityModelPath,
                "City",
                replacements);
            AddImportedMaterialSet(
                ImportedSmallTreesModelPath,
                "SmallTrees",
                replacements);
            AddImportedMaterialSet(
                ImportedPalmTreesModelPath,
                "PalmTrees",
                replacements);
            return replacements;
        }

        private static void AddImportedMaterialSet(
            string modelPath,
            string prefix,
            IDictionary<Material, Material> replacements)
        {
            foreach (Material source in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>())
            {
                string safeName = SanitizeAssetName(source.name);
                string materialName = $"MAT_FS_Imported_{prefix}_{safeName}";
                string materialPath = $"{ImportedMaterialFolder}/{materialName}.mat";
                Material replacement = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

                if (replacement == null)
                {
                    replacement = new Material(shader) { name = materialName };
                    AssetDatabase.CreateAsset(replacement, materialPath);
                }
                else if (shader != null)
                {
                    replacement.shader = shader;
                }

                CopyImportedMaterialProperties(source, replacement, prefix);
                EditorUtility.SetDirty(replacement);
                replacements[source] = replacement;
            }
        }

        private static void CopyImportedMaterialProperties(Material source, Material destination, string prefix)
        {
            Texture baseTexture = GetFirstTexture(source, "_BaseMap", "_MainTex");
            Color baseColor = GetFirstColor(source, "_BaseColor", "_Color", Color.white);
            SetColor(destination, "_BaseColor", baseColor);
            SetColor(destination, "_Color", baseColor);

            if (destination.HasProperty("_BaseMap"))
            {
                destination.SetTexture("_BaseMap", baseTexture);
            }
            if (destination.HasProperty("_MainTex"))
            {
                destination.SetTexture("_MainTex", baseTexture);
            }

            if (source.HasProperty("_MainTex"))
            {
                Vector2 scale = source.GetTextureScale("_MainTex");
                Vector2 offset = source.GetTextureOffset("_MainTex");
                if (destination.HasProperty("_BaseMap"))
                {
                    destination.SetTextureScale("_BaseMap", scale);
                    destination.SetTextureOffset("_BaseMap", offset);
                }
                if (destination.HasProperty("_MainTex"))
                {
                    destination.SetTextureScale("_MainTex", scale);
                    destination.SetTextureOffset("_MainTex", offset);
                }
            }

            Texture normal = GetFirstTexture(source, "_BumpMap", "_NormalMap");
            if (normal != null && destination.HasProperty("_BumpMap"))
            {
                destination.SetTexture("_BumpMap", normal);
                destination.EnableKeyword("_NORMALMAP");
            }

            if (destination.HasProperty("_Smoothness"))
            {
                float smoothness = source.HasProperty("_Glossiness")
                    ? source.GetFloat("_Glossiness")
                    : 0.18f;
                destination.SetFloat("_Smoothness", Mathf.Clamp(smoothness, 0.05f, 0.55f));
            }

            bool foliage = prefix.Contains("Trees", StringComparison.Ordinal) ||
                source.name.Contains("leaf", StringComparison.OrdinalIgnoreCase) ||
                source.name.Contains("leave", StringComparison.OrdinalIgnoreCase);
            ConfigureAlphaClip(destination, foliage);
        }

        private static Texture GetFirstTexture(Material material, params string[] properties)
        {
            foreach (string property in properties)
            {
                if (material.HasProperty(property))
                {
                    Texture texture = material.GetTexture(property);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }

            return null;
        }

        private static Color GetFirstColor(
            Material material,
            string firstProperty,
            string secondProperty,
            Color fallback)
        {
            if (material.HasProperty(firstProperty))
            {
                return material.GetColor(firstProperty);
            }
            if (material.HasProperty(secondProperty))
            {
                return material.GetColor(secondProperty);
            }

            return fallback;
        }

        private static void ConfigureAlphaClip(Material material, bool enabled)
        {
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", enabled ? 1f : 0f);
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.35f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", enabled ? 0f : 2f);

            if (enabled)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.doubleSidedGI = true;
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.Geometry;
                material.doubleSidedGI = false;
            }
        }

        private static string SanitizeAssetName(string value)
        {
            char[] characters = value
                .Select(character => char.IsLetterOrDigit(character) ? character : '_')
                .ToArray();
            string result = new string(characters).Trim('_');
            return string.IsNullOrEmpty(result) ? "Material" : result;
        }

        private static Texture2D CreateOrUpdateAtlas()
        {
            const int cellSize = 64;
            const int cells = 4;
            string path = $"{TextureFolder}/T_ENV_FS_ExplosionAtlas.png";
            Texture2D texture = new Texture2D(cellSize * cells, cellSize, TextureFormat.RGBA32, false, false);
            Color[] pixels = new Color[cellSize * cells * cellSize];

            for (int frame = 0; frame < cells; frame++)
            {
                for (int y = 0; y < cellSize; y++)
                {
                    for (int x = 0; x < cellSize; x++)
                    {
                        float nx = (x + 0.5f) / cellSize * 2f - 1f;
                        float ny = (y + 0.5f) / cellSize * 2f - 1f;
                        float angle = Mathf.Atan2(ny, nx);
                        float radius = Mathf.Sqrt(nx * nx + ny * ny);
                        float wobble = 0.08f * Mathf.Sin(angle * (5f + frame) + frame * 1.7f)
                            + 0.04f * Mathf.Sin((nx * 13f + ny * 17f) + frame);
                        float softness = Mathf.Lerp(0.48f, 0.92f, frame / 3f);
                        float alpha = Mathf.Clamp01((softness + wobble - radius) * 4.5f);
                        alpha *= alpha;
                        Color tint = Color.Lerp(
                            new Color(1f, 0.92f, 0.55f, 1f),
                            new Color(1f, 0.18f, 0.02f, 1f),
                            Mathf.Clamp01(radius + frame * 0.12f));
                        tint.a = alpha;
                        pixels[y * cellSize * cells + frame * cellSize + x] = tint;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Mesh CreateOrUpdateDebrisMesh()
        {
            string path = $"{TextureFolder}/MESH_ENV_FS_AsphaltDebris.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "MESH_ENV_FS_AsphaltDebris" };
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(-0.6f, -0.22f, -0.5f), new Vector3(0.55f, -0.18f, -0.42f),
                new Vector3(0.48f, -0.15f, 0.58f), new Vector3(-0.52f, -0.2f, 0.45f),
                new Vector3(-0.42f, 0.25f, -0.36f), new Vector3(0.4f, 0.18f, -0.3f),
                new Vector3(0.35f, 0.22f, 0.4f), new Vector3(-0.38f, 0.3f, 0.32f)
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material CreateLitMaterial(string name, Color color, bool emission = false)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            SetColor(material, "_BaseColor", color);
            SetColor(material, "_Color", color);
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", name.Contains("Car", StringComparison.Ordinal) ? 0.55f : 0.18f);
            }

            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                SetColor(material, "_EmissionColor", color * 2.2f);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateParticleMaterial(
            string name,
            Texture2D atlas,
            int atlasCell,
            Color tint,
            bool additive)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit");

            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            SetColor(material, "_BaseColor", tint);
            SetColor(material, "_Color", tint);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", atlas);
                material.SetTextureScale("_BaseMap", atlasCell < 0 ? Vector2.one : new Vector2(0.25f, 1f));
                material.SetTextureOffset("_BaseMap", atlasCell < 0 ? Vector2.zero : new Vector2(atlasCell * 0.25f, 0f));
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", atlas);
                material.SetTextureScale("_MainTex", atlasCell < 0 ? Vector2.one : new Vector2(0.25f, 1f));
                material.SetTextureOffset("_MainTex", atlasCell < 0 ? Vector2.zero : new Vector2(atlasCell * 0.25f, 0f));
            }

            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreatePlayer()
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            player.name = "Sphere_BrokenRoadPreview";
            player.transform.position = new Vector3(0f, 0.75f, -18f);
            player.transform.localScale = Vector3.one * 1.5f;

            Rigidbody body = player.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            player.AddComponent<KeyboardGameplayInput>();
            BallMovement movement = player.AddComponent<BallMovement>();
            movement.SetForwardSpeed(7.5f, 3.5f);
            return player;
        }

        private static Camera CreateReviewCamera()
        {
            GameObject player = GameObject.Find("Sphere_BrokenRoadPreview");
            GameObject cameraObject = new GameObject("CAM_FS_BrokenRoad_Gameplay");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.62f, 0.88f, 1f);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 350f;
            cameraObject.transform.position = new Vector3(0f, 5.2f, -26f);

            CameraFollow follow = cameraObject.AddComponent<CameraFollow>();
            SerializedObject serialized = new SerializedObject(follow);
            serialized.FindProperty("target").objectReferenceValue = player.transform;
            serialized.FindProperty("offset").vector3Value = new Vector3(0f, 4.5f, -8.5f);
            serialized.FindProperty("lookAheadDistance").floatValue = 8f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static void CreateLighting()
        {
            GameObject sunObject = new GameObject("LGT_FS_KeySun");
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.82f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sunObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            GameObject fillObject = new GameObject("LGT_FS_CoolFill");
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.35f, 0.62f, 1f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fillObject.transform.rotation = Quaternion.Euler(32f, 150f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.48f, 0.7f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.38f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.13f, 0.15f);
            RenderSettings.ambientIntensity = 0.9f;
        }

        private static void CreateSceneLabels()
        {
            GameObject labels = new GameObject("ReviewMoments");
            CreateLabel(labels.transform, "01_Armed_NormalRoad", new Vector3(0f, 0f, 24f));
            CreateLabel(labels.transform, "02_Explosion", new Vector3(0f, 0f, 24f));
            CreateLabel(labels.transform, "03_GapRevealed", new Vector3(0f, 0f, 24f));
            CreateLabel(labels.transform, "04_BurningAftermath", new Vector3(0f, 0f, 24f));
        }

        public static void BuildImportedStreetScenery(Transform parent)
        {
            EnsureFolders();
            Dictionary<Material, Material> importedMaterials = CreateImportedUrpMaterials();
            CreateImportedStreetScenery(parent, importedMaterials);
            AssetDatabase.SaveAssets();
        }

        private static void CreateImportedStreetScenery(
            Transform parent,
            IReadOnlyDictionary<Material, Material> importedMaterials)
        {
            GameObject cityModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                ImportedCityModelPath);
            GameObject smallTreesModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                ImportedSmallTreesModelPath);
            GameObject palmTreesModel = AssetDatabase.LoadAssetAtPath<GameObject>(
                ImportedPalmTreesModelPath);

            if (cityModel == null && smallTreesModel == null && palmTreesModel == null)
            {
                Debug.LogWarning("Imported Street libraries are unavailable; retaining the legacy preview scenery.");
                CreateLegacyFuturisticStreetScenery(parent);
                return;
            }

            GameObject sceneryRoot = new GameObject("FuturisticStreetScenery_ImportedAssets");
            if (parent != null)
            {
                sceneryRoot.transform.SetParent(parent, false);
            }
            Vector3 stageOrigin = parent != null ? parent.position : Vector3.zero;

            if (cityModel != null)
            {
                GameObject city = InstantiateVisualModel(
                    cityModel,
                    sceneryRoot.transform,
                    "ENV_FS_ImportedCityBuildings",
                    importedMaterials);
                FitInstanceToBounds(
                    city,
                    new Bounds(stageOrigin + new Vector3(0f, 12f, 24f), new Vector3(62f, 24f, 112f)));

                Bounds routeClearance = new Bounds(
                    stageOrigin + new Vector3(0f, 8f, 24f),
                    new Vector3(11f, 16f, 112f));
                int hiddenRenderers = HideRenderersIntersecting(city, routeClearance);
                Debug.Log($"Imported city scenery retained outside the route; {hiddenRenderers} crossing renderers hidden.");
            }

            if (smallTreesModel != null)
            {
                CreateVegetationCluster(smallTreesModel, sceneryRoot.transform, importedMaterials,
                    "ENV_FS_SmallTrees_LeftNear", stageOrigin + new Vector3(-11.5f, 0f, -8f), 7.5f, 10f);
                CreateVegetationCluster(smallTreesModel, sceneryRoot.transform, importedMaterials,
                    "ENV_FS_SmallTrees_RightMid", stageOrigin + new Vector3(11f, 0f, 27f), 6.5f, 9f);
                CreateVegetationCluster(smallTreesModel, sceneryRoot.transform, importedMaterials,
                    "ENV_FS_SmallTrees_LeftFar", stageOrigin + new Vector3(-12f, 0f, 61f), 8f, 11f);
            }

            if (palmTreesModel != null)
            {
                CreateVegetationCluster(palmTreesModel, sceneryRoot.transform, importedMaterials,
                    "ENV_FS_PalmTrees_RightNear", stageOrigin + new Vector3(13.5f, 0f, 5f), 8.5f, 11f);
                CreateVegetationCluster(palmTreesModel, sceneryRoot.transform, importedMaterials,
                    "ENV_FS_PalmTrees_RightFar", stageOrigin + new Vector3(14f, 0f, 51f), 9.5f, 12f);
            }
        }

        private static void CreateLegacyFuturisticStreetScenery(Transform parent)
        {
            const string folder = "Assets/EndlessRollball/Prefabs/Environment/FuturisticStreet";
            string[] moduleNames =
            {
                "ENV_FS_Roadside_A",
                "ENV_FS_Roadside_B",
                "ENV_FS_Roadside_C",
                "ENV_FS_Roadside_A"
            };
            float[] positions = { -24f, 0f, 24f, 48f };
            GameObject sceneryRoot = new GameObject("FuturisticStreetScenery_LegacyFallback");
            if (parent != null)
            {
                sceneryRoot.transform.SetParent(parent, false);
            }

            for (int index = 0; index < moduleNames.Length; index++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{folder}/{moduleNames[index]}.prefab");
                if (prefab == null)
                {
                    continue;
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, sceneryRoot.transform) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                instance.name = $"{moduleNames[index]}_{index + 1:00}";
                instance.transform.SetPositionAndRotation(
                    new Vector3(0f, 0f, positions[index]),
                    Quaternion.identity);
            }

            GameObject backdropPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{folder}/ENV_FS_Backdrop.prefab");
            if (backdropPrefab != null)
            {
                GameObject backdrop = PrefabUtility.InstantiatePrefab(backdropPrefab, sceneryRoot.transform) as GameObject;
                if (backdrop != null)
                {
                    backdrop.name = "ENV_FS_Backdrop_Review";
                    backdrop.transform.position = new Vector3(0f, 0f, 54f);
                }
            }
        }

        private static GameObject InstantiateVisualModel(
            GameObject source,
            Transform parent,
            string name,
            IReadOnlyDictionary<Material, Material> importedMaterials)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to instantiate imported environment asset {source.name}.");
            }

            instance.name = name;
            RemovePhysicsComponents(instance);
            ApplyImportedMaterialOverrides(instance, importedMaterials);
            return instance;
        }

        private static void CreateVegetationCluster(
            GameObject source,
            Transform parent,
            IReadOnlyDictionary<Material, Material> importedMaterials,
            string name,
            Vector3 groundPosition,
            float targetHeight,
            float maximumWidth)
        {
            GameObject instance = InstantiateVisualModel(source, parent, name, importedMaterials);
            Bounds bounds = CalculateRendererBounds(instance);
            if (bounds.size.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float scaleForHeight = targetHeight / Mathf.Max(0.001f, bounds.size.y);
            float scaleForWidth = maximumWidth / Mathf.Max(0.001f, Mathf.Max(bounds.size.x, bounds.size.z));
            float uniformScale = Mathf.Min(scaleForHeight, scaleForWidth);
            instance.transform.localScale *= uniformScale;

            bounds = CalculateRendererBounds(instance);
            Vector3 desiredCenter = new Vector3(
                groundPosition.x,
                groundPosition.y + bounds.extents.y,
                groundPosition.z);
            instance.transform.position += desiredCenter - bounds.center;
        }

        private static void FitInstanceToBounds(GameObject instance, Bounds target)
        {
            Bounds source = CalculateRendererBounds(instance);
            if (source.size.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float scale = Mathf.Min(
                target.size.x / Mathf.Max(0.001f, source.size.x),
                Mathf.Min(
                    target.size.y / Mathf.Max(0.001f, source.size.y),
                    target.size.z / Mathf.Max(0.001f, source.size.z)));
            instance.transform.localScale *= scale;
            source = CalculateRendererBounds(instance);
            instance.transform.position += target.center - source.center;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled)
                .ToArray();
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static int HideRenderersIntersecting(GameObject root, Bounds clearance)
        {
            int hidden = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.bounds.Intersects(clearance))
                {
                    continue;
                }

                renderer.enabled = false;
                hidden++;
            }

            return hidden;
        }

        private static void RemovePhysicsComponents(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            foreach (Rigidbody rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.DestroyImmediate(rigidbody);
            }
        }

        private static void ApplyImportedMaterialOverrides(
            GameObject root,
            IReadOnlyDictionary<Material, Material> importedMaterials)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    if (materials[index] != null && importedMaterials.TryGetValue(materials[index], out Material replacement))
                    {
                        materials[index] = replacement;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        private static void CreateLabel(Transform parent, string name, Vector3 position)
        {
            GameObject label = new GameObject(name);
            label.transform.SetParent(parent, false);
            label.transform.position = position;
        }

        private static GameObject InstantiateTrack(
            GameObject prefab,
            Transform parent,
            float z,
            string name)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to instantiate {prefab.name}.");
            }

            instance.name = name;
            instance.transform.SetPositionAndRotation(new Vector3(0f, 0f, z), Quaternion.identity);
            return instance;
        }

        private static ParticleSystem[] FindParticles(GameObject root, string prefix)
        {
            return root.GetComponentsInChildren<ParticleSystem>(true)
                .Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(item => item.name)
                .ToArray();
        }

        private static GameObject CreateVisualCube(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool coordinatesAreNormalized = false)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = coordinatesAreNormalized
                ? new Vector3(localPosition.x * parent.localScale.x, localPosition.y * 0.1f, localPosition.z * parent.localScale.z)
                : localPosition;
            cube.transform.localScale = coordinatesAreNormalized
                ? new Vector3(localScale.x / Mathf.Max(0.001f, parent.localScale.x), localScale.y, localScale.z)
                : localScale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static GameObject CreateVisualCylinder(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = localScale;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            return cylinder;
        }

        private static void CaptureState(
            BrokenRoadEncounterController controller,
            Camera camera,
            BrokenRoadEncounterState state,
            float simulationTime,
            string outputPath)
        {
            controller.ApplyReviewState(state, simulationTime);
            float cameraZ = state == BrokenRoadEncounterState.Armed ? 3f : 10f;
            Vector3 cameraPosition = new Vector3(0f, 4.8f, cameraZ);
            camera.transform.SetPositionAndRotation(
                cameraPosition,
                LookRotation(new Vector3(0f, 0.65f, 36f), cameraPosition));
            RenderTexture target = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;

            try
            {
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();

                Texture2D capture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0f, 0f, 1920f, 1080f), 0, 0);
                capture.Apply(false, false);
                File.WriteAllBytes(outputPath, capture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(capture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Quaternion LookRotation(Vector3 target, Vector3 position)
        {
            return Quaternion.LookRotation((target - position).normalized, Vector3.up);
        }

        private static void SetColor(Material material, string property, Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "EndlessRollball");
            EnsureFolder("Assets/EndlessRollball", "Art");
            EnsureFolder("Assets/EndlessRollball/Art", "Environment");
            EnsureFolder("Assets/EndlessRollball/Art/Environment", "FuturisticStreet");
            EnsureFolder("Assets/EndlessRollball/Art/Environment/FuturisticStreet", "BrokenRoad");
            EnsureFolder("Assets/EndlessRollball", "Materials");
            EnsureFolder("Assets/EndlessRollball/Materials", "Environment");
            EnsureFolder("Assets/EndlessRollball/Materials/Environment", "FuturisticStreet");
            EnsureFolder("Assets/EndlessRollball/Materials/Environment/FuturisticStreet", "BrokenRoad");
            EnsureFolder("Assets/EndlessRollball/Materials/Environment/FuturisticStreet", "Imported");
            EnsureFolder("Assets/EndlessRollball", "Prefabs");
            EnsureFolder("Assets/EndlessRollball/Prefabs", "Environment");
            EnsureFolder("Assets/EndlessRollball/Prefabs/Environment", "FuturisticStreet");
            EnsureFolder("Assets", "Scenes");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static Color Html(string html)
        {
            return ColorUtility.TryParseHtmlString(html, out Color color) ? color : Color.magenta;
        }

        private readonly struct MaterialSet
        {
            public MaterialSet(
                Material asphalt,
                Material asphaltDark,
                Material exposedRoad,
                Material teal,
                Material blue,
                Material yellow,
                Material hazard,
                Material flashParticle,
                Material fireParticle,
                Material fireAnimationParticle,
                Material smokeParticle,
                Material dustParticle,
                Material sparkParticle,
                Mesh debrisMesh)
            {
                Asphalt = asphalt;
                AsphaltDark = asphaltDark;
                ExposedRoad = exposedRoad;
                Teal = teal;
                Blue = blue;
                Yellow = yellow;
                Hazard = hazard;
                FlashParticle = flashParticle;
                FireParticle = fireParticle;
                FireAnimationParticle = fireAnimationParticle;
                SmokeParticle = smokeParticle;
                DustParticle = dustParticle;
                SparkParticle = sparkParticle;
                DebrisMesh = debrisMesh;
            }

            public Material Asphalt { get; }
            public Material AsphaltDark { get; }
            public Material ExposedRoad { get; }
            public Material Teal { get; }
            public Material Blue { get; }
            public Material Yellow { get; }
            public Material Hazard { get; }
            public Material FlashParticle { get; }
            public Material FireParticle { get; }
            public Material FireAnimationParticle { get; }
            public Material SmokeParticle { get; }
            public Material DustParticle { get; }
            public Material SparkParticle { get; }
            public Mesh DebrisMesh { get; }
        }
    }
}
