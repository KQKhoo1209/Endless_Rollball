using System;
using System.IO;
using System.Linq;
using System.Text;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessRollball.Editor
{
    // Editor-only, opt-in prefab authoring. Never changes or opens a scene.
    internal static class RoadSideBoundaryAuthoring
    {
        const string Request = "Artifacts/RoadSafetyFix/Apply.request";
        const string Report = "Artifacts/RoadSafetyFix/BoundaryAuthoringReport.txt";
        const string Folder = "Assets/EndlessRollballAssets/Environment Prefab/Roads";
        static readonly string[] Themes = { "Street", "Beach", "Dungeon" };
        static double deadline;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request)) return;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30)) return;
            if (File.ReadAllText(Request).Trim() != "apply-road-side-boundaries-v1") return;
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
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                File.WriteAllText(Request + ".blocked",
                    "Exit Play Mode and Prefab Mode, then use Tools/Endless Rollball/Track/Add Road Side Boundaries.");
                return;
            }
            try { Apply(); }
            catch (Exception error)
            {
                File.WriteAllText(Request + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Endless Rollball/Track/Add Road Side Boundaries")]
        static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play Mode and Prefab Mode first.");

            string[] paths = Themes.Select(theme => Folder + "/TRK_" + theme + "_Road_24m.prefab").ToArray();
            // Preflight all three before modifying any of them.
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var track = prefab != null ? prefab.GetComponent<TrackSegment>() : null;
                if (track == null || !track.IsContractValid(out _) ||
                    prefab.transform.localScale != Vector3.one)
                    throw new InvalidOperationException("Expected an existing unit-scale 24 m track: " + path);
                Transform floor = prefab.transform.Find("FloorCollider");
                var collider = floor != null ? floor.GetComponent<BoxCollider>() : null;
                if (collider == null || !collider.enabled || collider.isTrigger ||
                    collider.size != new Vector3(9f, 0.5f, 24f))
                    throw new InvalidOperationException("Existing floor was not the expected 9 x 24 m support: " + path);
            }

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string backup = "Artifacts/RoadSafetyFix/PrefabsBefore_" + stamp;
            Directory.CreateDirectory(backup);
            foreach (string path in paths)
            {
                File.Copy(path, backup + "/" + Path.GetFileName(path));
                File.Copy(path + ".meta", backup + "/" + Path.GetFileName(path) + ".meta");
            }
            if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + stamp);

            var report = new StringBuilder();
            report.AppendLine("ROAD_SIDE_BOUNDARIES_SAVED");
            report.AppendLine("Editor prefab authoring only. No Play Mode, gameplay tests or scene changes.");
            report.AppendLine("Backup: " + backup);
            foreach (string path in paths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    AddBoundary(root.transform, "SideBoundary_Left", -4.75f);
                    AddBoundary(root.transform, "SideBoundary_Right", 4.75f);
                    PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                    if (!saved) throw new IOException("Could not save " + path);
                    report.AppendLine(path + " | left=(-4.75,2,12), right=(4.75,2,12), size=(0.5,4,24)");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            report.AppendLine("Inner faces X=-4.5/+4.5; Y=0..4; Z=0..24. No end walls.");
            report.AppendLine("FloorCollider, RoadVisual, anchors and TrackSegment metadata preserved.");
            File.WriteAllText(Report, report.ToString());
            Debug.Log("ROAD_SIDE_BOUNDARIES_SAVED: " + Report);
        }

        static void AddBoundary(Transform root, string name, float x)
        {
            Transform boundary = root.Find(name);
            if (boundary == null)
            {
                boundary = new GameObject(name).transform;
                boundary.SetParent(root, false);
            }
            if (boundary.GetComponentsInChildren<Renderer>(true).Length != 0 ||
                boundary.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                throw new InvalidOperationException("Existing boundary contains unexpected art/physics: " + name);
            boundary.gameObject.layer = root.gameObject.layer;
            boundary.gameObject.SetActive(true);
            boundary.localPosition = new Vector3(x, 2f, 12f);
            boundary.localRotation = Quaternion.identity;
            boundary.localScale = Vector3.one;
            var collider = boundary.GetComponent<BoxCollider>();
            if (collider == null) collider = boundary.gameObject.AddComponent<BoxCollider>();
            collider.enabled = true;
            collider.isTrigger = false;
            collider.center = Vector3.zero;
            collider.size = new Vector3(0.5f, 4f, 24f);
        }
    }
}
