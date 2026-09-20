using System;
using System.Collections.Generic;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    // Generates the versioned graybox contract used by the procedural track tests.
    public static class TrackSetupBuilder
    {
        private const string Root = "Assets/EndlessRollball";
        private const string TrackPrefabFolder = Root + "/Prefabs/Track";
        private const string MaterialFolder = Root + "/Materials";
        private const string MeshFolder = Root + "/Meshes";
        private const string ResourceTrackFolder = Root + "/Resources/Track";
        private const string CatalogPath = ResourceTrackFolder + "/TrackCatalog.asset";
        private const string GameplayScenePath = "Assets/Scenes/GameplayScene.unity";
        private const string GapRampMeshPath = MeshFolder + "/TRK_Gap_Ramp.asset";

        [InitializeOnLoadMethod]
        private static void ScheduleInitialBuild()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !NeedsAdjustmentBuild())
                {
                    return;
                }

                Build();
            };
        }

        private static bool NeedsAdjustmentBuild()
        {
            DifficultyProfile easy = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(
                ResourceTrackFolder + "/Difficulty_Easy.asset");
            GameObject movingSweep = AssetDatabase.LoadAssetAtPath<GameObject>(
                TrackPrefabFolder + "/TRK_Moving_Wall_Sweep.prefab");
            return easy == null
                || easy.TrackIdentifier != "easy-v2-24001"
                || movingSweep == null;
        }

        [MenuItem("Tools/Endless Rollball/Build Procedural Track Graybox")]
        public static void Build()
        {
            try
            {
                EnsureFolders();

                Material floorMaterial = CreateMaterial(
                    MaterialFolder + "/MAT_TrackFloor.mat",
                    new Color(0.12f, 0.32f, 0.48f));
                Material narrowMaterial = CreateMaterial(
                    MaterialFolder + "/MAT_TrackNarrow.mat",
                    new Color(0.16f, 0.43f, 0.42f));
                Material tunnelMaterial = CreateMaterial(
                    MaterialFolder + "/MAT_TrackTunnel.mat",
                    new Color(0.19f, 0.22f, 0.32f));
                Material obstacleMaterial = CreateMaterial(
                    MaterialFolder + "/MAT_Obstacle.mat",
                    new Color(0.78f, 0.23f, 0.18f));
                Material movingMaterial = CreateMaterial(
                    MaterialFolder + "/MAT_MovingObstacle.mat",
                    new Color(0.92f, 0.55f, 0.12f));
                Mesh gapRampMesh = CreateOrUpdateGapRampMesh();

                TrackSegment wide = CreateSegment(
                    "TRK_Straight_Wide",
                    TrackSegmentKind.Empty,
                    DifficultyModeMask.All,
                    TrackLaneMask.All,
                    false,
                    new Vector4(5f, 3f, 2f, 3f),
                    root => AddFloor(root, 9f, 0f, 24f, floorMaterial));

                TrackSegment narrow = CreateSegment(
                    "TRK_Straight_Narrow",
                    TrackSegmentKind.Narrow,
                    DifficultyModeMask.Normal | DifficultyModeMask.Hard | DifficultyModeMask.Endless,
                    TrackLaneMask.Centre,
                    false,
                    new Vector4(0f, 2f, 4f, 3f),
                    root => AddFloor(root, 6f, 0f, 24f, narrowMaterial));

                TrackSegment gap = CreateSegment(
                    "TRK_Gap",
                    TrackSegmentKind.Gap,
                    DifficultyModeMask.All,
                    TrackLaneMask.All,
                    true,
                    new Vector4(1f, 2f, 2f, 2f),
                    root =>
                    {
                        AddFloor(root, 9f, 0f, 7.5f, floorMaterial);
                        AddRamp(root, gapRampMesh, floorMaterial);
                        AddFloor(root, 9f, 16.5f, 7.5f, floorMaterial);
                    });

                TrackSegment tunnel = CreateSegment(
                    "TRK_Tunnel_Straight",
                    TrackSegmentKind.Tunnel,
                    DifficultyModeMask.All,
                    TrackLaneMask.All,
                    false,
                    new Vector4(1f, 1f, 1f, 1f),
                    root =>
                    {
                        AddFloor(root, 9f, 0f, 24f, tunnelMaterial);
                        AddCube(root, "Wall_Left", new Vector3(-4.75f, 3f, 12f), new Vector3(0.5f, 6f, 24f), tunnelMaterial);
                        AddCube(root, "Wall_Right", new Vector3(4.75f, 3f, 12f), new Vector3(0.5f, 6f, 24f), tunnelMaterial);
                        AddCube(root, "Roof", new Vector3(0f, 6.25f, 12f), new Vector3(10f, 0.5f, 24f), tunnelMaterial);
                    });

                TrackSegment wallLeft = CreateSegment(
                    "TRK_Straight_Wall_Left",
                    TrackSegmentKind.StaticObstacle,
                    DifficultyModeMask.All,
                    TrackLaneMask.Centre | TrackLaneMask.Right,
                    false,
                    new Vector4(2f, 3f, 4f, 3f),
                    root =>
                    {
                        AddFloor(root, 9f, 0f, 24f, floorMaterial);
                        AddCube(root, "OBS_Wall_Left", new Vector3(-3f, 1f, 12f), new Vector3(2.5f, 2f, 1f), obstacleMaterial);
                    });

                TrackSegment wallRight = CreateSegment(
                    "TRK_Straight_Wall_Right",
                    TrackSegmentKind.StaticObstacle,
                    DifficultyModeMask.All,
                    TrackLaneMask.Left | TrackLaneMask.Centre,
                    false,
                    new Vector4(2f, 3f, 4f, 3f),
                    root =>
                    {
                        AddFloor(root, 9f, 0f, 24f, floorMaterial);
                        AddCube(root, "OBS_Wall_Right", new Vector3(3f, 1f, 12f), new Vector3(2.5f, 2f, 1f), obstacleMaterial);
                    });

                TrackSegment moving = CreateSegment(
                    "TRK_Moving_Barrier",
                    TrackSegmentKind.MovingObstacle,
                    DifficultyModeMask.All,
                    TrackLaneMask.All,
                    false,
                    new Vector4(1f, 2f, 3f, 3f),
                    root =>
                    {
                        AddFloor(root, 9f, 0f, 24f, floorMaterial);
                        GameObject barrier = AddCube(
                            root,
                            "OBS_Moving_Barrier",
                            new Vector3(0f, 1f, 12f),
                            new Vector3(2.5f, 2f, 1f),
                            movingMaterial);
                        barrier.AddComponent<MovingObstacle>().ConfigureForPrefab(
                            new[] { new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f) },
                            1.5f,
                            WaypointTraversalMode.PingPong);
                    });

                TrackSegment movingWallSweep = CreateSegment(
                    "TRK_Moving_Wall_Sweep",
                    TrackSegmentKind.MovingObstacle,
                    DifficultyModeMask.Normal | DifficultyModeMask.Hard | DifficultyModeMask.Endless,
                    TrackLaneMask.All,
                    false,
                    new Vector4(0f, 2f, 3f, 3f),
                    root =>
                    {
                        AddFloor(root, 9f, 0f, 24f, floorMaterial);
                        GameObject wall = AddCube(
                            root,
                            "OBS_Moving_Wall_Wide",
                            new Vector3(0f, 1f, 12f),
                            new Vector3(4.5f, 2f, 1f),
                            movingMaterial);
                        wall.AddComponent<MovingObstacle>().ConfigureForPrefab(
                            new[] { new Vector3(-1.5f, 0f, 0f), new Vector3(1.5f, 0f, 0f) },
                            1.5f,
                            WaypointTraversalMode.PingPong);
                    });

                DifficultyProfile[] profiles =
                {
                    CreateProfile(DifficultyMode.Easy, true, 24001, "easy-v2-24001", 6f, 3f, 0.35f, 0.8f, 6f, 0.35f, 0.8f, 1000f),
                    CreateProfile(DifficultyMode.Normal, false, 0, "normal-v1", 7.5f, 3.5f, 0.6f, 1.5f, 7.5f, 0.6f, 1.5f, 1000f),
                    CreateProfile(DifficultyMode.Hard, false, 0, "hard-v1", 9f, 4f, 0.8f, 2.5f, 9f, 0.8f, 2.5f, 1000f),
                    CreateProfile(DifficultyMode.Endless, false, 0, "endless-v1", 7f, 3.5f, 0.55f, 1.5f, 10f, 0.85f, 3f, 1000f)
                };

                TrackCatalog catalog = LoadOrCreate<TrackCatalog>(CatalogPath);
                catalog.Configure(
                    wide,
                    new[] { wide, narrow, gap, tunnel, wallLeft, wallRight, moving, movingWallSweep });
                EditorUtility.SetDirty(catalog);

                WireGameplayScene(catalog, profiles);
                ConfigureBuildSettings();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (!catalog.IsValid(out string reason))
                {
                    throw new InvalidOperationException($"Generated catalog is invalid: {reason}");
                }

                TrackVisualIntegrator.TryIntegrateIfReady();

                Debug.Log("Endless Rollball procedural-track graybox, profiles, catalog, and scene wiring were generated successfully.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static TrackSegment CreateSegment(
            string name,
            TrackSegmentKind kind,
            DifficultyModeMask modes,
            TrackLaneMask safeLanes,
            bool requiredJump,
            Vector4 weights,
            Action<GameObject> buildGeometry)
        {
            GameObject root = new GameObject(name);

            Transform entry = new GameObject("Entry").transform;
            entry.SetParent(root.transform, false);
            entry.localPosition = Vector3.zero;

            Transform exit = new GameObject("Exit").transform;
            exit.SetParent(root.transform, false);
            exit.localPosition = new Vector3(0f, 0f, TrackSegment.ContractLength);

            buildGeometry(root);

            TrackSegment segment = root.AddComponent<TrackSegment>();
            segment.Configure(
                name,
                entry,
                exit,
                kind,
                modes,
                TrackLaneMask.All,
                safeLanes,
                requiredJump,
                weights.x,
                weights.y,
                weights.z,
                weights.w);

            string path = $"{TrackPrefabFolder}/{name}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return saved.GetComponent<TrackSegment>();
        }

        private static Mesh CreateOrUpdateGapRampMesh()
        {
            const int sections = 8;
            const float width = 9f;
            const float startZ = 7.5f;
            const float length = 3f;
            const float height = 0.35f;
            const float bottomY = -0.25f;

            Vector3[] vertices = new Vector3[(sections + 1) * 4];
            List<int> triangles = new List<int>(sections * 24 + 12);

            for (int index = 0; index <= sections; index++)
            {
                float t = index / (float)sections;
                float z = startZ + length * t;
                float topY = height * t * t;
                int vertex = index * 4;

                vertices[vertex] = new Vector3(-width * 0.5f, topY, z);
                vertices[vertex + 1] = new Vector3(width * 0.5f, topY, z);
                vertices[vertex + 2] = new Vector3(-width * 0.5f, bottomY, z);
                vertices[vertex + 3] = new Vector3(width * 0.5f, bottomY, z);
            }

            for (int index = 0; index < sections; index++)
            {
                int current = index * 4;
                int next = (index + 1) * 4;

                AddQuad(triangles, current, next, next + 1, current + 1);
                AddQuad(triangles, current + 2, current + 3, next + 3, next + 2);
                AddQuad(triangles, current, current + 2, next + 2, next);
                AddQuad(triangles, current + 1, next + 1, next + 3, current + 3);
            }

            AddQuad(triangles, 0, 1, 3, 2);
            int last = sections * 4;
            AddQuad(triangles, last, last + 2, last + 3, last + 1);

            Mesh generated = new Mesh
            {
                name = "TRK_Gap_Ramp"
            };
            generated.vertices = vertices;
            generated.triangles = triangles.ToArray();
            generated.RecalculateNormals();
            generated.RecalculateBounds();

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(GapRampMeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, GapRampMeshPath);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        private static void AddRamp(GameObject root, Mesh mesh, Material material)
        {
            GameObject ramp = new GameObject("JumpRamp");
            ramp.transform.SetParent(root.transform, false);

            MeshFilter filter = ramp.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = ramp.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            MeshCollider collider = ramp.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        private static void AddFloor(GameObject root, float width, float startZ, float length, Material material)
        {
            AddCube(
                root,
                "Platform",
                new Vector3(0f, -0.25f, startZ + length * 0.5f),
                new Vector3(width, 0.5f, length),
                material);
        }

        private static GameObject AddCube(
            GameObject root,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = localScale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cube;
        }

        private static DifficultyProfile CreateProfile(
            DifficultyMode mode,
            bool standardized,
            int seed,
            string identifier,
            float speed,
            float speedRange,
            float obstacleProbability,
            float movingSpeed,
            float maximumSpeed,
            float maximumObstacleProbability,
            float maximumMovingSpeed,
            float distanceToMaximum)
        {
            string path = $"{ResourceTrackFolder}/Difficulty_{mode}.asset";
            DifficultyProfile profile = LoadOrCreate<DifficultyProfile>(path);
            profile.Configure(
                mode,
                standardized,
                seed,
                identifier,
                speed,
                speedRange,
                obstacleProbability,
                movingSpeed,
                maximumSpeed,
                maximumObstacleProbability,
                maximumMovingSpeed,
                distanceToMaximum);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static Material CreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void WireGameplayScene(TrackCatalog catalog, DifficultyProfile[] profiles)
        {
            Scene scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);

            GameObject oldPlane = GameObject.Find("Plane");
            if (oldPlane != null && oldPlane.scene == scene)
            {
                UnityEngine.Object.DestroyImmediate(oldPlane);
            }

            GameObject previousSystem = GameObject.Find("TrackSystem");
            if (previousSystem != null && previousSystem.scene == scene)
            {
                UnityEngine.Object.DestroyImmediate(previousSystem);
            }

            GameObject sphere = GameObject.Find("Sphere");
            if (sphere == null)
            {
                throw new InvalidOperationException("GameplayScene must contain the existing Sphere player.");
            }

            BallMovement ballMovement = sphere.GetComponent<BallMovement>();
            if (ballMovement == null)
            {
                throw new InvalidOperationException("The Sphere must contain BallMovement.");
            }

            GameObject trackSystem = new GameObject("TrackSystem");
            GameObject pool = new GameObject("SegmentPool");
            pool.transform.SetParent(trackSystem.transform, false);

            DifficultyManager difficulty = trackSystem.AddComponent<DifficultyManager>();
            difficulty.ConfigureForScene(sphere.transform, ballMovement, profiles, DifficultyMode.Easy);

            TrackGenerator generator = trackSystem.AddComponent<TrackGenerator>();
            generator.ConfigureForScene(sphere.transform, catalog, difficulty, pool.transform);

            GameplayRunController runController = trackSystem.AddComponent<GameplayRunController>();
            runController.ConfigureForScene(ballMovement, generator, new Vector3(0f, 0.75f, 0f), -5f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void ConfigureBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(GameplayScenePath, true)
            };

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == GameplayScenePath)
                {
                    continue;
                }

                bool enabled = existing.path != "Assets/Scenes/SampleScene.unity" && existing.enabled;
                scenes.Add(new EditorBuildSettingsScene(existing.path, enabled));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "EndlessRollball");
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Meshes");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root + "/Prefabs", "Track");
            EnsureFolder(Root, "Resources");
            EnsureFolder(Root + "/Resources", "Track");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
