using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EndlessRollball.Scenery;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    // Scene reference authoring, not a gameplay test or runtime initialization.
    internal static class SmallGapGameplayWiring
    {
        const string ScenePath = "Assets/Scenes/GameplayScene.unity";
        const string ExpectedSceneHash = "98BE8A25A96E01ACF56C99FA03F7AA8D0708D41FE318C798A1863210967FF324";
        const string Folder = "Assets/EndlessRollballAssets/Environment Prefab/Roads/";
        const string Output = "Artifacts/SmallGapGameplayIntegration";
        const string Request = Output + "/Wire.request";
        const string Token = "wire-approved-three-theme-gaps-v1";
        static double deadline;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            // Time-limited request; the manual menu remains available after expiry.
            if (Application.isBatchMode || !File.Exists(Request) || File.ReadAllText(Request).Trim() != Token ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= Tick;
                File.WriteAllText(Request + ".blocked", "Editor busy; use the wiring menu after compilation.");
                return;
            }
            EditorApplication.update -= Tick;
            try { Wire(); }
            catch (Exception error) { File.WriteAllText(Request + ".failed", error.ToString()); Debug.LogException(error); }
        }

        [MenuItem("Tools/Endless Rollball/Track/Wire Approved Small Gaps Into Gameplay")]
        static void Wire()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play/Prefab Mode and save your edits before wiring gaps.");
            using (var sha = SHA256.Create())
            {
                // Saving after compilation may serialize the four NEW empty fields.
                // Accept only those default additions, not any other unreviewed scene edit.
                string sceneText = Regex.Replace(File.ReadAllText(ScenePath),
                    @"(?m)^(?:    gapTrackPrefab: \{fileID: 0\}|  gapShareWithinJump: 0)\r?\n", "").Replace("\r", "");
                if (BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sceneText))).Replace("-", "") != ExpectedSceneHash)
                    throw new InvalidOperationException("GameplayScene changed since inspection; preserving it for review.");
            }

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForWiring = !scene.IsValid() || !scene.isLoaded;
            if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                throw new InvalidOperationException("Save GameplayScene edits before wiring gap references.");
            if (openedForWiring)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

            try
            {
                var managers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EnvironmentManager>(true)).ToArray();
                var obstacles = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ObstacleManager>(true)).ToArray();
                var generators = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TrackGenerator>(true)).ToArray();
                if (managers.Length != 1 || obstacles.Length != 1 || generators.Length != 1)
                    throw new InvalidOperationException("Expected the existing single environment/obstacle/track managers.");
                var environment = new SerializedObject(managers[0]);
                var obstacle = new SerializedObject(obstacles[0]);
                var generator = new SerializedObject(generators[0]);
                if (generator.FindProperty("environmentManager").objectReferenceValue != managers[0] ||
                    generator.FindProperty("obstacleManager").objectReferenceValue != obstacles[0] ||
                    !(generator.FindProperty("catalog").objectReferenceValue is TrackCatalog catalog) || !catalog.RequiresThemeRoadSelection)
                    throw new InvalidOperationException("Preserve/review existing generator manager and themed-catalog references.");

                string[] themes = { "Street", "Beach", "Dungeon" };
                string[] fields = { "street", "beach", "dungeon" };
                var gaps = new TrackSegment[3];
                for (int i = 0; i < themes.Length; i++)
                {
                    TrackSegment clear = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "TRK_" + themes[i] + "_Road_24m.prefab")?.GetComponent<TrackSegment>();
                    gaps[i] = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Gaps/TRK_" + themes[i] + "_GapSmall_24m.prefab")?.GetComponent<TrackSegment>();
                    SerializedProperty settings = environment.FindProperty(fields[i]);
                    if (clear == null || gaps[i] == null || !gaps[i].IsContractValid(out _) ||
                        gaps[i].Kind != TrackSegmentKind.Gap || !gaps[i].RequiredJump ||
                        gaps[i].EntryLanes != TrackLaneMask.All || gaps[i].SafeLanes != TrackLaneMask.All ||
                        gaps[i].GetComponentsInChildren<Collider>(true).Length != 4 ||
                        settings.FindPropertyRelative("roadTrackPrefab").objectReferenceValue != clear ||
                        settings.FindPropertyRelative("gapTrackPrefab").objectReferenceValue != null)
                        throw new InvalidOperationException("Unexpected existing road/gap setup for " + themes[i] + "; preserving it.");
                    foreach (DifficultyMode difficulty in Enum.GetValues(typeof(DifficultyMode)))
                        if (!gaps[i].Supports(difficulty)) throw new InvalidOperationException("Gap does not support " + difficulty);
                }
                if (obstacle.FindProperty("gapShareWithinJump").floatValue != 0f)
                    throw new InvalidOperationException("Gap share already edited; preserving the user setting.");

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup = Output + "/BeforeSceneWiring_" + stamp;
                Directory.CreateDirectory(backup);
                File.Copy(ScenePath, backup + "/GameplayScene.unity");
                File.Copy(ScenePath + ".meta", backup + "/GameplayScene.unity.meta");
                if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + stamp);
                for (int i = 0; i < themes.Length; i++)
                    environment.FindProperty(fields[i]).FindPropertyRelative("gapTrackPrefab").objectReferenceValue = gaps[i];
                obstacle.FindProperty("gapShareWithinJump").floatValue = .5f;
                environment.ApplyModifiedPropertiesWithoutUndo();
                obstacle.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save GameplayScene; see backup.");

                var report = new StringBuilder("SMALL_GAPS_GAMEPLAY_WIRED\nBackup: " + backup + "\n");
                for (int i = 0; i < themes.Length; i++) report.AppendLine(fields[i] + ".gapTrackPrefab = " + AssetDatabase.GetAssetPath(gaps[i]));
                report.AppendLine("Gap Share Within Jump = .5; existing jumpFraction=" + obstacle.FindProperty("jumpFraction").floatValue +
                    ", movingFraction=" + obstacle.FindProperty("movingFraction").floatValue + "; all obstacle lists/pool references retained.");
                report.AppendLine("Approved existing gap prefabs referenced, not duplicated or modified. Normal roads, TrackCatalog, scenery and preview scene not edited.");
                report.AppendLine("One shared encounter plan before track acquisition; one clear segment before/after challenges; startup/tunnel/finish exclusions retained.");
                report.AppendLine("Only scene wiring authored. No BeginRun invocation, track generation, Play Mode, automated tests, jump acceptance or profiling performed.");
                File.WriteAllText(Output + "/WiringReport.txt", report.ToString());
                Debug.Log("SMALL_GAPS_GAMEPLAY_WIRED: " + Output + "/WiringReport.txt");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                // Do not leave an extra GameplayScene loaded alongside EnvironmentPreview.
                if (openedForWiring && scene.IsValid() && scene.isLoaded && !scene.isDirty)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
