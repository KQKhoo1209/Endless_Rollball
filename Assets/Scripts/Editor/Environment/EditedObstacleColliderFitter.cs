using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessRollball.Editor
{
    // Narrow, explicit editor authoring. Existing collider owners/types are retained.
    internal static class EditedObstacleColliderFitter
    {
        const string Root = "Assets/EndlessRollballAssets/Obstacles/Shared/Prefabs/";
        const string Request = "Artifacts/ObstacleColliderRefinement/Fit.request";
        const string Token = "fit-edited-fixed-and-nav01-colliders-v1";
        const string Output = "Artifacts/ObstacleColliderRefinement";
        static double deadline;

        static readonly string[] Targets =
        {
            "FixedPosition/OBS_FIX_02_CrateWall.prefab",
            "FixedPosition/OBS_FIX_03_BarrelStack.prefab",
            "FixedPosition/OBS_FIX_04_MixedCargo.prefab",
            "FixedPosition/OBS_FIX_05_StoneColumn.prefab",
            "FixedPosition/OBS_FIX_07_LumberTower.prefab",
            "FixedPosition/OBS_FIX_08_ParkedCompactCar.prefab",
            "FixedPosition/OBS_FIX_09_ParkedSUV.prefab",
            "FixedPosition/OBS_FIX_10_DeliveryPickup.prefab",
            "NavigateLeftRight/OBS_NAV_01_SlidingCrates.prefab"
        };

        sealed class Plan
        {
            public string Path;
            public GameObject Root;
            public string ProtectedState;
            public readonly List<Fit> Fits = new List<Fit>();
        }
        sealed class Fit
        {
            public BoxCollider Collider;
            public Bounds Bounds;
            public FitRecord Record;
        }
        [Serializable] sealed class FitRecord
        {
            public string owner;
            public Vector3 oldCenter, oldSize, center, size;
            public bool changed, usesVisualFallback;
            public int rendererCount;
        }
        [Serializable] sealed class AssetRecord
        {
            public string path;
            public bool saved;
            public Vector3 colliderMin, colliderMax;
            public FitRecord[] colliders;
        }
        [Serializable] sealed class Report
        {
            public string scope = "Existing BoxCollider center/size only; no mesh, material, transform, hierarchy, Rigidbody, motion, gameplay or scene edit.";
            public string fit = "Each box encloses descendant mesh local bounds transformed into that collider owner's coordinates. Collider-only vehicle owners use the existing Visual hierarchy. Rounded shapes use box approximations, not exact mesh collision.";
            public string verification = "Editor authoring bounds/state checks only. No Play Mode, automated gameplay tests or profiling.";
            public string backup;
            public AssetRecord[] assets;
        }

        // Historical reference-fitting pass: automatic/menu entry points disabled.
        // References must not be targeted again; the correction tool reuses only math helpers.
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request) ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30) ||
                File.ReadAllText(Request).Trim() != Token) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += RunRequest;
        }

        static void RunRequest()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= RunRequest;
                File.WriteAllText(Request + ".blocked", "Editor remained busy. Use the manual fitting menu after reload.");
                return;
            }
            EditorApplication.update -= RunRequest;
            if (!File.Exists(Request)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                File.WriteAllText(Request + ".blocked", "Exit Play Mode and Prefab Mode, save edits, then use the manual fitting menu.");
                return;
            }
            try { Apply(); }
            catch (Exception error)
            {
                File.WriteAllText(Request + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play Mode and Prefab Mode first.");

            var plans = new List<Plan>();
            try
            {
                // Preflight every asset and calculate all shapes before any prefab write.
                foreach (string target in Targets)
                {
                    string path = Root + target;
                    if (!File.Exists(path)) throw new FileNotFoundException("Expected existing prefab: " + path);
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    var plan = new Plan { Path = path, Root = root };
                    plans.Add(plan);
                    if (root.transform.localScale != Vector3.one)
                        throw new InvalidOperationException("Preserve a unit outer root: " + path);
                    Collider[] shapes = root.GetComponentsInChildren<Collider>(true);
                    if (shapes.Length == 0 || shapes.Any(c => !(c is BoxCollider) || c.isTrigger || !c.enabled))
                        throw new InvalidOperationException("Expected existing enabled non-trigger boxes: " + path);

                    foreach (BoxCollider box in shapes.Cast<BoxCollider>())
                    {
                        Renderer[] renderers = MeshRenderers(box.transform);
                        bool fallback = renderers.Length == 0;
                        if (fallback)
                        {
                            Transform visual = root.transform.Find("Visual") ?? root.transform.Find("Mover/Visual");
                            if (visual == null) throw new InvalidOperationException("No visual target for " + box.name);
                            renderers = MeshRenderers(visual);
                        }
                        Bounds bounds = FitBounds(box.transform, renderers);
                        if (bounds.size.x <= 0f || bounds.size.y <= 0f || bounds.size.z <= 0f)
                            throw new InvalidOperationException("Degenerate fitted shape: " + box.name);
                        bool changed = Vector3.Distance(box.center, bounds.center) > 0.00001f ||
                            Vector3.Distance(box.size, bounds.size) > 0.00001f;
                        plan.Fits.Add(new Fit
                        {
                            Collider = box, Bounds = bounds,
                            Record = new FitRecord
                            {
                                owner = AnimationUtility.CalculateTransformPath(box.transform, root.transform),
                                oldCenter = box.center, oldSize = box.size,
                                center = bounds.center, size = bounds.size,
                                changed = changed, usesVisualFallback = fallback, rendererCount = renderers.Length
                            }
                        });
                    }
                    plan.ProtectedState = ProtectedState(root);
                }

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup = Output + "/Before_" + stamp;
                Directory.CreateDirectory(backup);
                foreach (Plan plan in plans)
                {
                    File.Copy(plan.Path, backup + "/" + Path.GetFileName(plan.Path));
                    File.Copy(plan.Path + ".meta", backup + "/" + Path.GetFileName(plan.Path) + ".meta");
                }
                if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + stamp);

                var records = new List<AssetRecord>();
                var text = new StringBuilder("EDITED_OBSTACLE_COLLIDERS_FITTED\nBackup: " + backup + "\n");
                foreach (Plan plan in plans)
                {
                    foreach (Fit fit in plan.Fits.Where(f => f.Record.changed))
                    {
                        fit.Collider.center = fit.Bounds.center;
                        fit.Collider.size = fit.Bounds.size;
                    }
                    if (ProtectedState(plan.Root) != plan.ProtectedState)
                        throw new InvalidOperationException("Unexpected non-collider state change in " + plan.Path);

                    foreach (Fit fit in plan.Fits)
                        if (Vector3.Distance(fit.Collider.center, fit.Bounds.center) > 0.00002f ||
                            Vector3.Distance(fit.Collider.size, fit.Bounds.size) > 0.00002f)
                            throw new InvalidOperationException("Authored box differs from fitted bounds.");

                    bool changed = plan.Fits.Any(f => f.Record.changed);
                    if (changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(plan.Root, plan.Path, out bool saved);
                        if (!saved) throw new IOException("Could not save " + plan.Path);
                    }
                    Bounds footprint = BoxBoundsInRoot(plan.Root, plan.Fits.Select(f => f.Collider));
                    records.Add(new AssetRecord
                    {
                        path = plan.Path, saved = changed, colliderMin = footprint.min,
                        colliderMax = footprint.max, colliders = plan.Fits.Select(f => f.Record).ToArray()
                    });
                    text.AppendLine(Path.GetFileName(plan.Path) + " | " + (changed ? "fitted" : "already fitted") +
                        " | boxes=" + plan.Fits.Count + " | root min=" + footprint.min.ToString("F4") +
                        " | root max=" + footprint.max.ToString("F4"));
                    foreach (Fit fit in plan.Fits)
                        text.AppendLine("  " + fit.Record.owner + " | center " + fit.Record.oldCenter.ToString("F4") +
                            " -> " + fit.Record.center.ToString("F4") + " | size " + fit.Record.oldSize.ToString("F4") +
                            " -> " + fit.Record.size.ToString("F4"));
                }
                text.AppendLine("Only existing box centre/size changed. Artwork/hierarchy/physics owners/movement preserved.");
                text.AppendLine("Box approximations, not exact mesh colliders. No Play Mode, gameplay tests or profiling.");
                File.WriteAllText(Output + "/ColliderFitReport.json", JsonUtility.ToJson(new Report { backup = backup, assets = records.ToArray() }, true));
                File.WriteAllText(Output + "/ColliderFitReport.txt", text.ToString());
                Debug.Log("EDITED_OBSTACLE_COLLIDERS_FITTED: " + Output + "/ColliderFitReport.txt");
            }
            finally
            {
                foreach (Plan plan in plans)
                    if (plan.Root != null) PrefabUtility.UnloadPrefabContents(plan.Root);
            }
        }

        internal static Renderer[] MeshRenderers(Transform owner) => owner.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is SkinnedMeshRenderer || (r is MeshRenderer && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null))
            .ToArray();

        internal static Bounds FitBounds(Transform owner, Renderer[] renderers)
        {
            if (renderers.Length == 0) throw new InvalidOperationException("No mesh bounds to fit: " + owner.name);
            bool first = true;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                Bounds meshBounds = renderer is SkinnedMeshRenderer skinned
                    ? skinned.localBounds : renderer.GetComponent<MeshFilter>().sharedMesh.bounds;
                Matrix4x4 matrix = owner.worldToLocalMatrix * renderer.localToWorldMatrix;
                foreach (Vector3 corner in Corners(meshBounds))
                {
                    Vector3 point = matrix.MultiplyPoint3x4(corner);
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        static IEnumerable<Vector3> Corners(Bounds bounds)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
        }

        internal static Bounds BoxBoundsInRoot(GameObject root, IEnumerable<BoxCollider> boxes)
        {
            bool first = true;
            Bounds bounds = default;
            foreach (BoxCollider box in boxes)
            foreach (Vector3 corner in Corners(new Bounds(box.center, box.size)))
            {
                Vector3 point = root.transform.InverseTransformPoint(box.transform.TransformPoint(corner));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
            return bounds;
        }

        static string ProtectedState(GameObject root)
        {
            var state = new StringBuilder();
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                state.AppendLine(AnimationUtility.CalculateTransformPath(transform, root.transform));
                state.AppendLine(EditorJsonUtility.ToJson(transform.gameObject));
                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (component == null) throw new InvalidOperationException("Missing script in " + transform.name);
                    if (!(component is Collider))
                        state.AppendLine(component.GetType().FullName + "|" + EditorJsonUtility.ToJson(component));
                }
            }
            return state.ToString();
        }
    }
}
