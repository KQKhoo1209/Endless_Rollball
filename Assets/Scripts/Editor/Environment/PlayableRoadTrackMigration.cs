using System;
using System.IO;
using System.Linq;
using System.Text;
using EndlessRollball.Scenery;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    internal static class PlayableRoadTrackMigration
    {
        const string GameplayPath = "Assets/Scenes/GameplayScene.unity";
        const string PrefabFolder = "Assets/EndlessRollballAssets/Environment Prefab/Roads/Playable";
        internal const string CatalogPath = "Assets/EndlessRollballAssets/Resources/Track/TrackCatalog_PlayableRoads.asset";
        const string Request = "Artifacts/PlayableRoadMigration/Create.request";
        const string Report = "Artifacts/PlayableRoadMigration/MigrationReport.txt";
        static double deadline;

        static readonly string[] Themes = { "Street", "Beach", "Dungeon" };
        static readonly string[] ThemeFields = { "street", "beach", "dungeon" };
        static readonly string[] VisualGuids =
        {
            "d45e9149966587e44972a37fa064d4c5",
            "97a963abddc2c8d4286bd97151479ca6",
            "c9748fa7e4c6c6745a769623d10924e9"
        };

        internal static bool IsActive
        {
            get
            {
                TrackCatalog catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(CatalogPath);
                return catalog != null && catalog.RequiresThemeRoadSelection;
            }
        }

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request)) return;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30)) return;
            if (File.ReadAllText(Request).Trim() != "migrate-three-playable-roads-v1") return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += RunRequest;
        }

        static void RunRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= RunRequest;
                return;
            }
            EditorApplication.update -= RunRequest;
            if (!File.Exists(Request)) return;
            Scene gameplay = SceneManager.GetSceneByPath(GameplayPath);
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                (gameplay.IsValid() && gameplay.isLoaded && gameplay.isDirty))
            {
                File.WriteAllText(Request + ".blocked",
                    "Exit Play Mode and save your GameplayScene edits. Then use Tools/Endless Rollball/Track/Migrate To Playable Theme Roads.");
                return;
            }
            File.Move(Request, Request + ".consumed_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            try { Migrate(); }
            catch (Exception error)
            {
                File.WriteAllText(Request + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Endless Rollball/Track/Migrate To Playable Theme Roads")]
        public static void Migrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before migrating road assets.");
            Scene gameplay = SceneManager.GetSceneByPath(GameplayPath);
            bool openedGameplay = !gameplay.IsValid() || !gameplay.isLoaded;
            if (!openedGameplay && gameplay.isDirty)
                throw new InvalidOperationException("Save your GameplayScene edits first; unsaved edits were preserved.");
            if (File.Exists(CatalogPath))
                throw new IOException("Existing playable-road catalogue preserved; migration is create-once.");
            foreach (string theme in Themes)
                if (File.Exists(PrefabPath(theme)))
                    throw new IOException("Existing playable road preserved: " + PrefabPath(theme));

            var sources = VisualGuids.Select(guid =>
                AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            for (int i = 0; i < sources.Length; i++)
                if (!sources[i]) throw new FileNotFoundException("Missing approved " + Themes[i] + " road visual.");

            Scene previousActive = SceneManager.GetActiveScene();
            Scene staging = default;
            try
            {
                if (openedGameplay) gameplay = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Additive);
                var managers = gameplay.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<EnvironmentManager>(true)).ToArray();
                var generators = gameplay.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<TrackGenerator>(true)).ToArray();
                if (managers.Length != 1 || generators.Length != 1)
                    throw new InvalidOperationException("Expected exactly one EnvironmentManager and TrackGenerator in GameplayScene.");

                string backup = "Artifacts/PlayableRoadMigration/SceneBefore_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                Directory.CreateDirectory(backup);
                File.Copy(GameplayPath, backup + "/GameplayScene.unity");
                EnsureFolder(PrefabFolder);
                staging = EditorSceneManager.NewPreviewScene();
                var roots = new GameObject[3];
                var report = new StringBuilder();
                report.AppendLine("Playable themed roads v1 — straight-only migration, no gameplay tests.");
                report.AppendLine("Old gameplay prefabs/catalogue, visual sources and EnvironmentPreview are preserved.");
                report.AppendLine("No overlay road spawning. New obstacles/gaps/narrow tracks are deferred.");
                report.AppendLine("Tunnel slots use the approaching theme's complete road. Scenery/fade schedule is unchanged.");
                report.AppendLine("Scene backup: " + backup);

                for (int i = 0; i < roots.Length; i++)
                {
                    var root = new GameObject("TRK_" + Themes[i] + "_Road_24m");
                    SceneManager.MoveGameObjectToScene(root, staging);
                    roots[i] = root;
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(sources[i], staging);
                    visual.name = "RoadVisual";
                    visual.transform.SetParent(root.transform, false);
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    visual.transform.localScale = Vector3.one;
                    if (visual.GetComponentsInChildren<Component>(true).Any(component =>
                        component && !(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer)))
                        throw new InvalidOperationException("Approved road source is no longer visual-only: " + sources[i].name);

                    var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length == 0) throw new InvalidOperationException("Road source has no mesh renderers.");
                    Bounds bounds = renderers[0].bounds;
                    foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    if (Mathf.Abs(bounds.min.x + 4.5f) > .01f || Mathf.Abs(bounds.max.x - 4.5f) > .01f ||
                        Mathf.Abs(bounds.min.z) > .01f || Mathf.Abs(bounds.max.z - 24) > .01f || Mathf.Abs(bounds.max.y) > .01f)
                        throw new InvalidOperationException("Road source no longer matches the approved 9x24m floor contract: " + sources[i].name);

                    var entry = Child("Entry", root.transform, Vector3.zero);
                    var exit = Child("Exit", root.transform, new Vector3(0, 0, 24));
                    var floor = Child("FloorCollider", root.transform, Vector3.zero).gameObject;
                    BoxCollider collider = floor.AddComponent<BoxCollider>();
                    collider.center = new Vector3(0, -.25f, 12);
                    collider.size = new Vector3(9, .5f, 24);
                    collider.isTrigger = false;
                    TrackSegment segment = root.AddComponent<TrackSegment>();
                    segment.Configure(root.name, entry, exit, TrackSegmentKind.Empty,
                        DifficultyModeMask.All, TrackLaneMask.All, TrackLaneMask.All, false, 1, 1, 1, 1);
                    var serialized = new SerializedObject(segment);
                    serialized.FindProperty("presentationRoot").objectReferenceValue = visual;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    if (!segment.IsContractValid(out string reason)) throw new InvalidOperationException(reason);
                    report.AppendLine(root.name + ": 9m x 24m, top Y=0, floor center (0,-0.25,12), size (9,0.5,24), one BoxCollider.");
                    report.AppendLine("  Source: " + AssetDatabase.GetAssetPath(sources[i]));
                    report.AppendLine("  Visual bounds: min=" + bounds.min.ToString("F4") + ", max=" + bounds.max.ToString("F4"));
                }

                var tracks = new TrackSegment[3];
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(roots[i], PrefabPath(Themes[i]));
                    if (!saved) throw new IOException("Could not save " + PrefabPath(Themes[i]));
                    tracks[i] = saved.GetComponent<TrackSegment>();
                }
                var catalog = ScriptableObject.CreateInstance<TrackCatalog>();
                catalog.name = "TrackCatalog_PlayableRoads";
                catalog.Configure(tracks[0], tracks);
                var serializedCatalog = new SerializedObject(catalog);
                serializedCatalog.FindProperty("requiresThemeRoadSelection").boolValue = true;
                serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                AssetDatabase.SaveAssetIfDirty(catalog);

                var serializedManager = new SerializedObject(managers[0]);
                for (int i = 0; i < tracks.Length; i++)
                    serializedManager.FindProperty(ThemeFields[i]).FindPropertyRelative("roadTrackPrefab").objectReferenceValue = tracks[i];
                serializedManager.ApplyModifiedPropertiesWithoutUndo();
                var serializedGenerator = new SerializedObject(generators[0]);
                serializedGenerator.FindProperty("catalog").objectReferenceValue = catalog;
                serializedGenerator.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(gameplay);
                if (!EditorSceneManager.SaveScene(gameplay, GameplayPath))
                    throw new IOException("GameplayScene could not be saved; created assets were preserved.");

                report.AppendLine("SAVED " + CatalogPath + " (clear fallback Street, three entries, scheduled-theme selection required).");
                report.AppendLine("SAVED " + GameplayPath + " (three playable-road references and generator catalogue assignment).");
                report.AppendLine("Authoring bounds/contracts checked; compilation and Play Mode acceptance are separate. No tests/profiling run.");
                Directory.CreateDirectory(Path.GetDirectoryName(Report));
                File.WriteAllText(Report, report.ToString());
                Debug.Log("PLAYABLE_ROAD_MIGRATION_SAVED: three complete tracks and GameplayScene wiring; no gameplay tests run.");
            }
            finally
            {
                if (staging.IsValid()) EditorSceneManager.ClosePreviewScene(staging);
                if (openedGameplay && gameplay.IsValid() && gameplay.isLoaded && !gameplay.isDirty)
                    EditorSceneManager.CloseScene(gameplay, true);
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            }
        }

        static string PrefabPath(string theme) => PrefabFolder + "/TRK_" + theme + "_Road_24m.prefab";

        static Transform Child(string name, Transform parent, Vector3 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = position;
            return child;
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
