using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EndlessRollball.Track;
using UnityEditor;
using UnityEngine;

namespace EndlessRollball.Editor
{
    public static class TrackVisualIntegrator
    {
        private const string PrefabFolder = "Assets/EndlessRollball/Prefabs/Track";
        private const string ModelFolder = "Assets/EndlessRollball/Art/Track";

        private static readonly VisualBinding[] Bindings =
        {
            new VisualBinding("TRK_Straight_Wide", "TRK_Straight_Wide", null, null),
            new VisualBinding("TRK_Straight_Narrow", "TRK_Straight_Narrow", null, null),
            new VisualBinding("TRK_Gap", "TRK_Gap", null, null),
            new VisualBinding("TRK_Tunnel_Straight", "TRK_Tunnel_Straight", null, null),
            new VisualBinding("TRK_Straight_Wall_Left", "TRK_Straight_Wide", "OBS_Wall_Left", "OBS_Wall"),
            new VisualBinding("TRK_Straight_Wall_Right", "TRK_Straight_Wide", "OBS_Wall_Right", "OBS_Wall"),
            new VisualBinding("TRK_Moving_Barrier", "TRK_Straight_Wide", "OBS_Moving_Barrier", "OBS_Moving_Barrier"),
            new VisualBinding("TRK_Moving_Wall_Sweep", "TRK_Straight_Wide", "OBS_Moving_Wall_Wide", "OBS_Moving_Wall_Wide")
        };

        [InitializeOnLoadMethod]
        private static void ScheduleInitialIntegration()
        {
            EditorApplication.delayCall += () =>
            {
                if (AllRequiredModelsExist() && !HasIntegratedVisual("TRK_Straight_Wide"))
                {
                    Integrate();
                }
            };
        }

        public static bool TryIntegrateIfReady()
        {
            if (!AllRequiredModelsExist())
            {
                return false;
            }

            Integrate();
            return true;
        }

        [MenuItem("Tools/Endless Rollball/Integrate Track Visuals")]
        public static void Integrate()
        {
            ValidateSegmentModel("TRK_Straight_Wide", 24f);
            ValidateSegmentModel("TRK_Straight_Narrow", 24f);
            ValidateSegmentModel("TRK_Gap", 24f);
            ValidateSegmentModel("TRK_Tunnel_Straight", 24f);

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (VisualBinding binding in Bindings)
                {
                    Integrate(binding);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Endless Rollball Blender visuals were integrated without changing gameplay colliders or TrackSegment metadata.");
        }

        private static void Integrate(VisualBinding binding)
        {
            string prefabPath = $"{PrefabFolder}/{binding.PrefabName}.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);

            try
            {
                TrackSegment segment = root.GetComponent<TrackSegment>();
                if (segment == null)
                {
                    throw new InvalidOperationException($"{prefabPath} has no TrackSegment metadata.");
                }

                RemoveExistingVisuals(root);

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = false;
                }

                AddModel(root.transform, binding.SegmentModelName, "VisualMesh");

                if (!string.IsNullOrEmpty(binding.ObstacleAnchorName))
                {
                    Transform anchor = root.GetComponentsInChildren<Transform>(true)
                        .FirstOrDefault(item => item.name == binding.ObstacleAnchorName);
                    if (anchor == null)
                    {
                        throw new InvalidOperationException(
                            $"{prefabPath} has no obstacle anchor named {binding.ObstacleAnchorName}.");
                    }

                    AddModel(anchor, binding.ObstacleModelName, "ObstacleVisualMesh");
                }

                if (!segment.IsContractValid(out string reason))
                {
                    throw new InvalidOperationException($"Visual integration changed the segment contract: {reason}");
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AddModel(Transform parent, string modelName, string instanceName)
        {
            string modelPath = $"{ModelFolder}/{modelName}.fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new FileNotFoundException($"Required Blender export was not imported: {modelPath}");
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate {modelPath}.");
            }

            instance.name = instanceName;
            instance.transform.localPosition = Vector3.zero;
            // Preserve the FBX importer's axis-conversion rotation and scale.
        }

        private static void RemoveExistingVisuals(GameObject root)
        {
            HashSet<string> visualNames = new HashSet<string>
            {
                "VisualMesh",
                "ObstacleVisualMesh"
            };

            foreach (Transform item in root.GetComponentsInChildren<Transform>(true)
                         .Where(item => item != root.transform && visualNames.Contains(item.name))
                         .ToArray())
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        private static void ValidateSegmentModel(string modelName, float expectedLength)
        {
            string modelPath = $"{ModelFolder}/{modelName}.fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                throw new FileNotFoundException($"Required Blender export was not imported: {modelPath}");
            }

            GameObject instance = UnityEngine.Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.position = Vector3.zero;

            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    throw new InvalidOperationException($"{modelPath} contains no renderable meshes.");
                }

                Bounds bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1))
                {
                    bounds.Encapsulate(renderer.bounds);
                }

                if (Mathf.Abs(bounds.min.z) > 0.02f ||
                    Mathf.Abs(bounds.max.z - expectedLength) > 0.02f)
                {
                    throw new InvalidOperationException(
                        $"{modelName} must face Unity +Z from 0 to {expectedLength}m, " +
                        $"but imported bounds are {bounds.min.z:F3} to {bounds.max.z:F3}.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static bool AllRequiredModelsExist()
        {
            return Bindings
                .SelectMany(binding => new[] { binding.SegmentModelName, binding.ObstacleModelName })
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .All(name => File.Exists($"{ModelFolder}/{name}.fbx"));
        }

        private static bool HasIntegratedVisual(string prefabName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{PrefabFolder}/{prefabName}.prefab");
            return prefab != null && prefab.GetComponentsInChildren<Transform>(true)
                .Any(item => item.name == "VisualMesh");
        }

        private readonly struct VisualBinding
        {
            public readonly string PrefabName;
            public readonly string SegmentModelName;
            public readonly string ObstacleAnchorName;
            public readonly string ObstacleModelName;

            public VisualBinding(
                string prefabName,
                string segmentModelName,
                string obstacleAnchorName,
                string obstacleModelName)
            {
                PrefabName = prefabName;
                SegmentModelName = segmentModelName;
                ObstacleAnchorName = obstacleAnchorName;
                ObstacleModelName = obstacleModelName;
            }
        }
    }
}
