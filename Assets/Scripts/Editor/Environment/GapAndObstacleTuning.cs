using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    // Scoped authoring pass. Measures real mesh/collider bounds, not gameplay simulation.
    internal static class GapAndObstacleTuning
    {
        const string Output = "Artifacts/GapAndObstacleTuning";
        const string Request = Output + "/Apply.request";
        const string Token = "six-metre-gaps-denser-encounters-fit-edited-obstacles-v1";
        const string ScenePath = "Assets/Scenes/GameplayScene.unity";
        const string GapFolder = "Assets/EndlessRollballAssets/Environment Prefab/Roads/Gaps/";
        const string ObstacleFolder = "Assets/EndlessRollballAssets/Obstacles/Shared/Prefabs/";
        const float NewRun = 9f;
        const float Rise = .8f;
        static double deadline;

        [Serializable] sealed class Expected { public FileState[] files; }
        [Serializable] sealed class FileState { public string path, hash; }
        sealed class Plan
        {
            public string Path;
            public GameObject Root;
            public bool Changed;
            public Entry Entry;
        }
        [Serializable] sealed class Entry
        {
            public string path, operation;
            public bool saved;
            public Vector3 beforeMin, beforeMax, afterMin, afterMax, horizontalShift;
            public float horizontalScale = 1f, allowedMinX, allowedMaxX, allowedMinZ, allowedMaxZ;
            public int colliders;
        }
        [Serializable] sealed class Report
        {
            public string marker = "GAPS_AND_OBSTACLES_TUNED";
            public string backup;
            public Entry[] assets;
            public float gapMetres = 6f, encounterMultiplier = 1.5f, jumpFraction = .25f, gapShareWithinJump = .5f;
        }

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            // A stale request cannot unexpectedly author assets in a later editor session.
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
                File.WriteAllText(Request + ".blocked", "Editor busy; use the tuning menu after compilation.");
                return;
            }
            EditorApplication.update -= Tick;
            try { Apply(); }
            catch (Exception error) { File.WriteAllText(Request + ".failed", error.ToString()); Debug.LogException(error); }
        }

        [MenuItem("Tools/Endless Rollball/Track/Widen Gaps And Fit Edited Obstacles")]
        static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play/Prefab Mode and save your edits first.");
            Expected expected = JsonUtility.FromJson<Expected>(File.ReadAllText(Output + "/ExpectedFiles.json"));
            if (expected?.files == null || expected.files.Length != 21)
                throw new InvalidOperationException("Expected the inspected 20 prefabs and GameplayScene.");
            foreach (FileState file in expected.files)
            {
                if (file.path != ScenePath &&
                    !(file.path.StartsWith(GapFolder, StringComparison.Ordinal) ||
                    file.path.StartsWith(ObstacleFolder + "FixedPosition/", StringComparison.Ordinal) ||
                    file.path.StartsWith(ObstacleFolder + "NavigateLeftRight/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Unexpected target path in authoring manifest.");
                string hash = Hash(File.ReadAllBytes(file.path));
                if (hash != file.hash && file.path == ScenePath)
                    hash = Hash(Encoding.UTF8.GetBytes(Regex.Replace(File.ReadAllText(file.path),
                        @"(?m)^  encounterProbabilityMultiplier: 1\r?\n", "").Replace("\r", "")));
                if (hash != file.hash)
                    throw new InvalidOperationException("Newer edits preserved; re-inspect before tuning: " + file.path);
            }

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save GameplayScene before authoring.");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var plans = new List<Plan>();
            try
            {
                var obstacles = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ObstacleManager>(true)).ToArray();
                if (obstacles.Length != 1) throw new InvalidOperationException("Expected one existing ObstacleManager.");
                var manager = new SerializedObject(obstacles[0]);
                if (manager.FindProperty("encounterProbabilityMultiplier") == null)
                    throw new InvalidOperationException("Compile the updated runtime sources first.");

                // Preflight EVERY composition before saving any prefab or scene.
                foreach (FileState file in expected.files.Where(f => f.path.EndsWith(".prefab", StringComparison.Ordinal)))
                {
                    var plan = new Plan { Path = file.path, Root = PrefabUtility.LoadPrefabContents(file.path) };
                    plans.Add(plan);
                    NeedIdentity(plan.Root.transform);
                    if (plan.Path.StartsWith(GapFolder, StringComparison.Ordinal)) WidenGap(plan);
                    else FitObstacle(plan);
                }
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup = Output + "/BeforeApply_" + stamp;
                foreach (FileState file in expected.files)
                {
                    string dest = backup + "/" + file.path;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(file.path, dest);
                    File.Copy(file.path + ".meta", dest + ".meta");
                }
                if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + stamp);
                foreach (Plan plan in plans.Where(p => p.Changed))
                {
                    PrefabUtility.SaveAsPrefabAsset(plan.Root, plan.Path, out bool saved);
                    if (!saved) throw new IOException("Could not save " + plan.Path + "; see backups.");
                    plan.Entry.saved = true;
                }
                manager.FindProperty("encounterProbabilityMultiplier").floatValue = 1.5f;
                manager.FindProperty("jumpFraction").floatValue = .25f;
                manager.FindProperty("gapShareWithinJump").floatValue = .5f;
                manager.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save tuned GameplayScene.");
                var result = new Report { backup = backup, assets = plans.Select(p => p.Entry).ToArray() };
                File.WriteAllText(Output + "/TuningReport.json", JsonUtility.ToJson(result, true));
                var report = new StringBuilder("GAPS_AND_OBSTACLES_TUNED\nBackup: " + backup + "\n");
                foreach (Entry entry in result.assets)
                    report.AppendLine(Path.GetFileName(entry.path) + " | " + entry.operation + " | saved=" + entry.saved +
                        " | bounds " + entry.beforeMin.ToString("F4") + ".." + entry.beforeMax.ToString("F4") +
                        " -> " + entry.afterMin.ToString("F4") + ".." + entry.afterMax.ToString("F4") +
                        " | horizontal scale=" + entry.horizontalScale.ToString("F4"));
                report.AppendLine("All gaps: 6m opening Z=9..15; .8m lips; 24m seams; matching slopes/artwork/colliders. Street warning Y retained as approved.");
                report.AppendLine("Encounter multiplier=1.5, jump fraction=.25, gap share=.5. Existing difficulty assets, speed, lists and clear-track cooldown unchanged.");
                report.AppendLine("Fixed footprints fit side placement at +/-2.25 with .25m road-edge margin. Movers fit their COMPLETE existing waypoint sweeps within +/-4.25m.");
                report.AppendLine("Payload depth stays within the middle 12m of a 24m track; height, custom collider dimensions/owners and moving waypoints/physics retained.");
                report.AppendLine("Already-fitting assets were not saved. No new vehicles added to gameplay lists. No normal roads, scenery or EnvironmentPreview scene edited.");
                report.AppendLine("Editor authoring bounds audit only. No Play Mode, gameplay tests, jump-acceptance guarantee or profiling. User review is required, especially slower-speed jumps.");
                File.WriteAllText(Output + "/TuningReport.txt", report.ToString());
                Debug.Log("GAPS_AND_OBSTACLES_TUNED: " + Output + "/TuningReport.txt");
            }
            finally
            {
                foreach (Plan plan in plans) if (plan.Root != null) PrefabUtility.UnloadPrefabContents(plan.Root);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened && scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void WidenGap(Plan plan)
        {
            Transform root = plan.Root.transform;
            TrackSegment track = plan.Root.GetComponent<TrackSegment>();
            if (track == null || !track.IsContractValid(out _) || track.Kind != TrackSegmentKind.Gap || !track.RequiredJump ||
                plan.Root.GetComponentsInChildren<Collider>(true).Length != 4)
                throw new InvalidOperationException("Unexpected gap contract: " + plan.Path);
            string metadata = EditorJsonUtility.ToJson(track);
            Transform visual = root.Find("RoadVisual");
            Transform a = visual.Find("Road_Takeoff_0_to_10"), b = visual.Find("Road_Landing_14_to_24");
            Transform floorA = root.Find("Floor_Takeoff"), floorB = root.Find("Floor_Landing");
            if (a == null || b == null || floorA == null || floorB == null)
                throw new InvalidOperationException("Gap geometry was reorganized; preserve and review " + plan.Path);
            BoxCollider boxA = floorA.GetComponent<BoxCollider>(), boxB = floorB.GetComponent<BoxCollider>();
            float oldLength = Mathf.Sqrt(100.64f), length = Mathf.Sqrt(NewRun * NewRun + Rise * Rise);
            if (boxA == null || boxB == null || Mathf.Abs(boxA.size.z - oldLength) > .001f ||
                floorB.localPosition != new Vector3(0, Rise, 14f))
                throw new InvalidOperationException("Expected the approved 4m gap before widening.");
            Bounds before = Combined(root);
            float angle = Mathf.Atan2(Rise, NewRun) * Mathf.Rad2Deg;
            Quaternion up = Quaternion.Euler(-angle, 0, 0), down = Quaternion.Euler(angle, 0, 0);
            Vector3 landing = new Vector3(0, Rise, 15f);
            bool street = plan.Path.EndsWith("TRK_Street_GapSmall_24m.prefab", StringComparison.Ordinal);
            foreach (var bank in new[] { a, b })
            {
                float ratio = length / oldLength;
                if (street)
                {
                    Vector3 scale = bank.localScale; scale.z *= ratio; bank.localScale = scale;
                }
                else
                {
                    Transform[] tiles = bank.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Tile_", StringComparison.Ordinal)).ToArray();
                    int expected = plan.Path.Contains("Beach") ? 12 : 6;
                    if (tiles.Length != expected || bank.localScale != Vector3.one)
                        throw new InvalidOperationException("Preserve the approved three-row bank layout: " + plan.Path);
                    foreach (Transform tile in tiles)
                    {
                        Vector3 p = tile.localPosition; p.z *= ratio; tile.localPosition = p;
                        Vector3 s = tile.localScale; s.z *= ratio; tile.localScale = s;
                    }
                }
            }
            UpdateWarning(visual.Find("Warning_Takeoff_Edge"), a, Vector3.zero, up, oldLength, length, true, street);
            UpdateWarning(visual.Find("Warning_Landing_Edge"), b, landing, down, oldLength, length, false, street);
            a.name = "Road_Takeoff_0_to_9"; b.name = "Road_Landing_15_to_24";
            a.localPosition = Vector3.zero; a.localRotation = up;
            b.localPosition = landing; b.localRotation = down;
            SetFloor(floorA, boxA, Vector3.zero, up, length);
            SetFloor(floorB, boxB, landing, down, length);
            if (Vector3.Distance(floorA.TransformPoint(new Vector3(0, 0, length)), root.TransformPoint(new Vector3(0, Rise, 9))) > .001f ||
                Vector3.Distance(floorB.TransformPoint(new Vector3(0, 0, length)), root.TransformPoint(new Vector3(0, 0, 24))) > .001f ||
                EditorJsonUtility.ToJson(track) != metadata || !track.IsContractValid(out _))
                throw new InvalidOperationException("Widened gap endpoints/metadata differ from intended contract.");
            plan.Changed = true;
            Bounds after = Combined(root);
            plan.Entry = EntryFor(plan, before, after, "widened 4m -> 6m; .8m rise and warning style preserved");
        }

        static void UpdateWarning(Transform warning, Transform oldBank, Vector3 start, Quaternion rotation,
            float oldLength, float newLength, bool takeoff, bool street)
        {
            if (warning == null) return; // Do not recreate user-removed decorations.
            Vector3 oldIdeal = oldBank.localPosition + oldBank.localRotation * new Vector3(0, .012f, takeoff ? oldLength - .2f : .2f);
            Vector3 next = start + rotation * new Vector3(0, .012f, takeoff ? newLength - .2f : .2f);
            next.x = warning.localPosition.x;
            next.y = street ? warning.localPosition.y : next.y + warning.localPosition.y - oldIdeal.y;
            warning.localPosition = next;
            warning.localRotation = rotation;
        }
        static void SetFloor(Transform floor, BoxCollider box, Vector3 start, Quaternion rotation, float length)
        {
            floor.localPosition = start; floor.localRotation = rotation; floor.localScale = Vector3.one;
            box.center = new Vector3(0, -.25f, length * .5f);
            box.size = new Vector3(box.size.x, box.size.y, length);
        }

        static void FitObstacle(Plan plan)
        {
            bool moving = plan.Path.Contains("/NavigateLeftRight/");
            Transform parent = moving ? plan.Root.transform.Find("Mover") : plan.Root.transform;
            if (parent == null) throw new InvalidOperationException("Missing Mover in " + plan.Path);
            NeedIdentity(parent);
            Collider[] boxes = plan.Root.GetComponentsInChildren<Collider>(true);
            if (boxes.Length == 0 || boxes.Any(c => !(c is BoxCollider) || !c.enabled || c.isTrigger))
                throw new InvalidOperationException("Expected existing enabled non-trigger box colliders: " + plan.Path);
            if (plan.Root.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh == null))
                throw new InvalidOperationException("Missing mesh in " + plan.Path);
            Bounds before = Combined(parent);
            Vector3 home = Vector3.zero;
            Vector3[] points = { Vector3.zero };
            SideToSideObstacleMotion motion = parent.GetComponent<SideToSideObstacleMotion>();
            string motionState = motion == null ? null : EditorJsonUtility.ToJson(motion);
            string bodyState = moving ? EditorJsonUtility.ToJson(parent.GetComponent<Rigidbody>()) : null;
            if (moving)
            {
                Rigidbody body = parent.GetComponent<Rigidbody>();
                if (motion == null || body == null || !body.isKinematic || body.useGravity)
                    throw new InvalidOperationException("Preserve the existing kinematic motion setup: " + plan.Path);
                var serialized = new SerializedObject(motion);
                home = serialized.FindProperty("localHomePosition").vector3Value;
                SerializedProperty wp = serialized.FindProperty("localWaypointOffsets");
                float half = serialized.FindProperty("halfTravel").floatValue;
                points = wp.arraySize >= 2
                    ? Enumerable.Range(0, wp.arraySize).Select(i => wp.GetArrayElementAtIndex(i).vector3Value).ToArray()
                    : new[] { Vector3.left * half, Vector3.right * half };
            }
            float minX = moving ? -4.25f - home.x - points.Min(p => p.x) : -2f;
            float maxX = moving ? 4.25f - home.x - points.Max(p => p.x) : 2f;
            float minZ = -6f - home.z - points.Min(p => p.z);
            float maxZ = 6f - home.z - points.Max(p => p.z);
            if (maxX <= minX || maxZ <= minZ || before.size.x <= 0 || before.size.z <= 0)
                throw new InvalidOperationException("Waypoints leave no room for the obstacle; preserve and review " + plan.Path);
            float scale = Mathf.Min(1f, (maxX - minX) / before.size.x, (maxZ - minZ) / before.size.z);
            float dx = Mathf.Clamp(0f, minX - before.min.x * scale, maxX - before.max.x * scale);
            float dz = Mathf.Clamp(0f, minZ - before.min.z * scale, maxZ - before.max.z * scale);
            plan.Changed = scale < .99999f || Mathf.Abs(dx) > .0001f || Mathf.Abs(dz) > .0001f;
            if (plan.Changed)
            {
                Transform[] groups = parent.Cast<Transform>().Where(t => t.GetComponentsInChildren<Renderer>(true).Length > 0 ||
                    t.GetComponentsInChildren<Collider>(true).Length > 0).ToArray();
                if (groups.Length == 0) throw new InvalidOperationException("No separable geometry roots: " + plan.Path);
                foreach (Transform group in groups)
                {
                    if (group.localRotation != Quaternion.identity)
                        throw new InvalidOperationException("Geometry-root rotation needs manual review: " + plan.Path);
                    Vector3 s = group.localScale; s.x *= scale; s.z *= scale; group.localScale = s;
                    Vector3 p = group.localPosition; p.x = p.x * scale + dx; p.z = p.z * scale + dz; group.localPosition = p;
                }
            }
            Bounds after = Combined(parent);
            if (after.min.x < minX - .005f || after.max.x > maxX + .005f ||
                after.min.z < minZ - .005f || after.max.z > maxZ + .005f ||
                Mathf.Abs(after.min.y - before.min.y) > .001f || Mathf.Abs(after.max.y - before.max.y) > .001f ||
                plan.Root.GetComponentsInChildren<Collider>(true).Length != boxes.Length ||
                (moving && (EditorJsonUtility.ToJson(motion) != motionState || EditorJsonUtility.ToJson(parent.GetComponent<Rigidbody>()) != bodyState)))
                throw new InvalidOperationException("Footprint/height/motion preservation audit failed: " + plan.Path);
            plan.Entry = EntryFor(plan, before, after, plan.Changed ? "horizontal fit; heights/collider shapes/motion preserved" : "already fits; untouched");
            plan.Entry.horizontalScale = scale;
            plan.Entry.horizontalShift = new Vector3(dx, 0, dz);
            plan.Entry.allowedMinX = minX; plan.Entry.allowedMaxX = maxX;
            plan.Entry.allowedMinZ = minZ; plan.Entry.allowedMaxZ = maxZ;
        }

        static Entry EntryFor(Plan p, Bounds before, Bounds after, string operation) => new Entry
        {
            path = p.Path, operation = operation, beforeMin = before.min, beforeMax = before.max,
            afterMin = after.min, afterMax = after.max, colliders = p.Root.GetComponentsInChildren<Collider>(true).Length
        };
        static Bounds Combined(Transform root)
        {
            Bounds b = EditedObstacleColliderFitter.FitBounds(root, EditedObstacleColliderFitter.MeshRenderers(root));
            BoxCollider[] boxes = root.GetComponentsInChildren<BoxCollider>(true);
            if (boxes.Length > 0) b.Encapsulate(EditedObstacleColliderFitter.BoxBoundsInRoot(root.gameObject, boxes));
            return b;
        }
        static void NeedIdentity(Transform root)
        {
            if (root.localPosition != Vector3.zero || root.localRotation != Quaternion.identity || root.localScale != Vector3.one)
                throw new InvalidOperationException("Expected identity root/Mover; user transform preserved: " + root.name);
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
    }
}
