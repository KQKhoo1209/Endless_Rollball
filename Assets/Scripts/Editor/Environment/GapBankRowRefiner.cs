using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // One-off visual authoring only. Never runs in a player or during Play Mode.
    internal static class GapBankRowRefiner
    {
        const string Folder = "Assets/EndlessRollballAssets/Environment Prefab/Roads/Gaps/";
        const string Output = "Artifacts/GapBankRowRefinement";
        const string Request = Output + "/Apply.request";
        const string Token = "refine-beach-dungeon-three-row-banks-v1";
        static readonly string[] Themes = { "Beach", "Dungeon" };
        static readonly string[] ExpectedHashes = {
            "AEE3D60FCC5BC6F377750F8C87636882371AA0E7B8A08E9EAE11742B257F2ED8",
            "2BE32A015E12DFCD28E0E3002D6005337AD345C08940C1729C10A81EEE028169"
        };
        static double deadline;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request) ||
                File.ReadAllText(Request).Trim() != Token ||
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
                File.WriteAllText(Request + ".blocked", "Editor remained busy. Use the refinement menu after compilation.");
                return;
            }
            EditorApplication.update -= Tick;
            try { Refine(); }
            catch (Exception error)
            {
                File.WriteAllText(Request + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Endless Rollball/Track/Refine Beach And Dungeon Gap Banks To Three Rows")]
        static void Refine()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play/Prefab Mode and save your edits before refining banks.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scene edits before refining prefab assets.");
            for (int i = 0; i < Themes.Length; i++)
                using (var sha = SHA256.Create())
                    if (BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(PathFor(i)))).Replace("-", "") != ExpectedHashes[i])
                        throw new InvalidOperationException(Themes[i] + " gap was edited since inspection; preserving changes.");

            var roots = new GameObject[2];
            var collisionStates = new string[2];
            var trackStates = new string[2];
            var report = new StringBuilder("BEACH_DUNGEON_THREE_ROW_BANKS_SAVED\n");
            try
            {
                // Fully preflight both assets before writing either one.
                for (int i = 0; i < Themes.Length; i++)
                {
                    roots[i] = PrefabUtility.LoadPrefabContents(PathFor(i));
                    GameObject root = roots[i];
                    collisionStates[i] = CollisionState(root);
                    trackStates[i] = EditorJsonUtility.ToJson(root.GetComponent<TrackSegment>());
                    foreach (string bankName in new[] { "Road_Takeoff_0_to_10", "Road_Landing_14_to_24" })
                    {
                        Transform bank = root.transform.Find("RoadVisual/" + bankName);
                        if (bank == null || bank.childCount != 1 ||
                            Mathf.Abs(bank.localScale.z - Mathf.Sqrt(100.64f) / 24f) > .0001f)
                            throw new InvalidOperationException("Unexpected bank layout; preserving " + Themes[i]);
                        Transform visual = bank.GetChild(0);
                        Transform[] rows = visual.Cast<Transform>().OrderBy(t => t.name).ToArray();
                        int expectedRows = i == 0 ? 10 : 6;
                        int columns = i == 0 ? 4 : 2;
                        if (rows.Length != expectedRows || rows.Any(r => !r.name.StartsWith("Row_") ||
                            r.childCount != columns || r.localPosition != Vector3.zero ||
                            r.localRotation != Quaternion.identity || r.localScale != Vector3.one) ||
                            Mathf.Abs(visual.localScale.x - 1.2f) > .0001f || visual.localScale.y != 1 || visual.localScale.z != 1)
                            throw new InvalidOperationException("Unexpected source tile grid; preserving " + Themes[i]);

                        float slopeLength = Mathf.Sqrt(100.64f);
                        float newRowLength = slopeLength / 3f;
                        float oldRowLength = 24f / expectedRows;
                        bank.localScale = Vector3.one;
                        for (int row = 0; row < rows.Length; row++)
                        {
                            if (row >= 3) { Object.DestroyImmediate(rows[row].gameObject); continue; }
                            foreach (Transform tile in rows[row])
                            {
                                Vector3 position = tile.localPosition;
                                position.z = (row + .5f) * newRowLength;
                                tile.localPosition = position;
                                Vector3 scale = tile.localScale;
                                scale.z *= newRowLength / oldRowLength;
                                tile.localScale = scale;
                            }
                        }
                        // Asset-space mesh bounds: does not require read/write enabled on FBXs.
                        Bounds bounds = LocalMeshBounds(bank);
                        if (Mathf.Abs(bounds.size.x - 10.8f) > .015f || Mathf.Abs(bounds.min.z) > .015f ||
                            Mathf.Abs(bounds.max.z - slopeLength) > .015f || Mathf.Abs(bounds.max.y) > .015f)
                            throw new InvalidOperationException("Retiled artwork no longer fits its support bank.");
                        report.AppendLine(Themes[i] + " / " + bankName + ": " + expectedRows + " -> 3 rows x " + columns +
                            " columns; bank scale=1; row length=" + newRowLength.ToString("F6") +
                            "m; local bounds=" + bounds.ToString("F6") + "; source width scale retained=1.2.");
                    }
                    if (CollisionState(root) != collisionStates[i] ||
                        EditorJsonUtility.ToJson(root.GetComponent<TrackSegment>()) != trackStates[i] ||
                        !root.GetComponent<TrackSegment>().IsContractValid(out _))
                        throw new InvalidOperationException("Collision or track metadata unexpectedly changed.");
                }

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup = Output + "/Before_" + stamp;
                Directory.CreateDirectory(backup);
                for (int i = 0; i < Themes.Length; i++)
                {
                    File.Copy(PathFor(i), backup + "/" + Path.GetFileName(PathFor(i)));
                    File.Copy(PathFor(i) + ".meta", backup + "/" + Path.GetFileName(PathFor(i)) + ".meta");
                }
                if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + stamp);
                report.AppendLine("Backup: " + backup);
                for (int i = 0; i < Themes.Length; i++)
                {
                    PrefabUtility.SaveAsPrefabAsset(roots[i], PathFor(i), out bool saved);
                    if (!saved) throw new IOException("Could not save " + Themes[i] + "; see backup.");
                    report.AppendLine("Saved: " + PathFor(i));
                }
                report.AppendLine("Only Beach/Dungeon bank visuals retiled. Same mesh/material references, width and natural vertical thickness.");
                report.AppendLine("Collision and TrackSegment metadata unchanged. Four-metre opening, 0.8m raised lips and slopes preserved.");
                report.AppendLine("Street gap, normal road prefabs, scenes and runtime code not edited. Existing preview instances inherit the assets.");
                report.AppendLine("No Play Mode, gameplay tests or profiling performed. User visual/gameplay review remains required.");
                File.WriteAllText(Output + "/RefinementReport.txt", report.ToString());
                Debug.Log("BEACH_DUNGEON_THREE_ROW_BANKS_SAVED: " + Output + "/RefinementReport.txt");
            }
            finally
            {
                foreach (GameObject root in roots)
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static string PathFor(int index) => Folder + "TRK_" + Themes[index] + "_GapSmall_24m.prefab";

        static Bounds LocalMeshBounds(Transform root)
        {
            bool first = true;
            var result = new Bounds();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) throw new InvalidOperationException("Missing source mesh.");
                Bounds b = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 p = toRoot.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                    else result.Encapsulate(p);
                }
            }
            if (first) throw new InvalidOperationException("No bank meshes.");
            return result;
        }

        static string CollisionState(GameObject root)
        {
            return string.Join("\n", root.GetComponentsInChildren<Collider>(true).OrderBy(c => c.name).Select(c =>
                AnimationUtility.CalculateTransformPath(c.transform, root.transform) + "|" +
                EditorJsonUtility.ToJson(c) + "|" + c.transform.localPosition.ToString("F7") + "|" +
                c.transform.localRotation.ToString("F7") + "|" + c.transform.localScale.ToString("F7")));
        }
    }
}
