using System;
using System.Collections.Generic;
using System.Linq;
using EndlessRollball.Track;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EndlessRollball.Tests
{
    public sealed class TrackSelectionEngineTests
    {
        [Test]
        public void SameSeedProducesSameFiftySegmentSequence()
        {
            CollectionAssert.AreEqual(GenerateSequence(24001), GenerateSequence(24001));
        }

        [Test]
        public void DifferentSeedsProduceDifferentSequences()
        {
            CollectionAssert.AreNotEqual(GenerateSequence(24001), GenerateSequence(24002));
        }

        [Test]
        public void SelectionNeverProducesConsecutiveGapsOrNoReachableLane()
        {
            List<TrackCandidate> candidates = CreateCandidates();
            TrackSelectionState state = TrackSelectionState.Initial;
            System.Random random = new System.Random(17);

            for (int index = 0; index < 200; index++)
            {
                TrackSegmentKind previousKind = state.PreviousKind;
                TrackSelectionResult result = TrackSelectionEngine.Select(
                    candidates,
                    random,
                    state,
                    0.65f,
                    index < 2);

                Assert.That(result.Success, Is.True);
                Assert.That(result.NextState.ReachableLanes, Is.Not.EqualTo(TrackLaneMask.None));
                Assert.That(
                    previousKind == TrackSegmentKind.Gap && result.Candidate.Kind == TrackSegmentKind.Gap,
                    Is.False);
                state = result.NextState;
            }
        }

        [TestCase(0.35f)]
        [TestCase(0.60f)]
        [TestCase(0.80f)]
        [TestCase(0.55f)]
        [TestCase(0.85f)]
        public void FiveHundredSelectionsTrackConfiguredObstacleProbability(float probability)
        {
            List<TrackCandidate> candidates = CreateCandidates();
            TrackSelectionState state = TrackSelectionState.Initial;
            System.Random random = new System.Random(24001);
            int obstacleCount = 0;

            for (int index = 0; index < 500; index++)
            {
                TrackSelectionResult result = TrackSelectionEngine.Select(
                    candidates,
                    random,
                    state,
                    probability,
                    index < 2);
                Assert.That(result.Success, Is.True);
                obstacleCount += result.Candidate.IsObstacle ? 1 : 0;
                state = result.NextState;
            }

            float observed = obstacleCount / 500f;
            Assert.That(observed, Is.EqualTo(probability).Within(0.08f));
        }

        [Test]
        public void GeneratedGrayboxPrefabsFollowTheTwentyFourMetreContract()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { "Assets/EndlessRollball/Prefabs/Track" });

            Assert.That(guids.Length, Is.GreaterThanOrEqualTo(8));

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TrackSegment segment = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                    ?.GetComponent<TrackSegment>();

                Assert.That(segment, Is.Not.Null, path);
                Assert.That(segment.IsContractValid(out string reason), Is.True, $"{path}: {reason}");
                Assert.That(segment.Length, Is.EqualTo(24f).Within(0.0001f), path);
            }
        }

        [Test]
        public void RevisedDifficultyProfilesUseApprovedObstacleDensityAndEasyVersion()
        {
            AssertProfile(DifficultyMode.Easy, "easy-v2-24001", 0.35f, 0.35f, 0.8f, 0.8f);
            AssertProfile(DifficultyMode.Normal, "normal-v1", 0.6f, 0.6f, 1.5f, 1.5f);
            AssertProfile(DifficultyMode.Hard, "hard-v1", 0.8f, 0.8f, 2.5f, 2.5f);
            AssertProfile(DifficultyMode.Endless, "endless-v1", 0.55f, 0.85f, 1.5f, 3f);
        }

        [Test]
        public void GapPrefabContainsSixMetreVoidAndCurvedUnityCollider()
        {
            const string path = "Assets/EndlessRollball/Prefabs/Track/TRK_Gap.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            TrackSegment segment = prefab.GetComponent<TrackSegment>();
            Transform ramp = prefab.transform.Find("JumpRamp");

            Assert.That(segment, Is.Not.Null);
            Assert.That(segment.RequiredJump, Is.True);
            Assert.That(ramp, Is.Not.Null);

            Mesh rampMesh = ramp.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider collider = ramp.GetComponent<MeshCollider>();
            Assert.That(rampMesh, Is.Not.Null);
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.sharedMesh, Is.SameAs(rampMesh));
            Assert.That(rampMesh.bounds.min.z, Is.EqualTo(7.5f).Within(0.001f));
            Assert.That(rampMesh.bounds.max.z, Is.EqualTo(10.5f).Within(0.001f));
            Assert.That(rampMesh.bounds.max.y, Is.EqualTo(0.35f).Within(0.001f));

            Transform[] platforms = prefab.GetComponentsInChildren<Transform>(true)
                .Where(item => item.name == "Platform")
                .OrderBy(item => item.localPosition.z)
                .ToArray();
            Assert.That(platforms.Length, Is.EqualTo(2));
            Assert.That(platforms[0].localPosition.z, Is.EqualTo(3.75f).Within(0.001f));
            Assert.That(platforms[0].localScale.z, Is.EqualTo(7.5f).Within(0.001f));
            Assert.That(platforms[1].localPosition.z, Is.EqualTo(20.25f).Within(0.001f));
            Assert.That(platforms[1].localScale.z, Is.EqualTo(7.5f).Within(0.001f));
        }

        [Test]
        public void WaypointObstacleMovesAtConstantSpeedAndResetsWithoutDrift()
        {
            GameObject obstacleObject = new GameObject("WaypointObstacleTest");
            try
            {
                MovingObstacle obstacle = obstacleObject.AddComponent<MovingObstacle>();
                obstacle.ConfigureForPrefab(
                    new[] { new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f) },
                    1.5f,
                    WaypointTraversalMode.PingPong);

                Assert.That(obstacleObject.transform.localPosition.x, Is.EqualTo(-3f).Within(0.001f));
                obstacle.Advance(2f);
                Assert.That(obstacleObject.transform.localPosition.x, Is.EqualTo(0f).Within(0.001f));
                obstacle.Advance(2f);
                Assert.That(obstacleObject.transform.localPosition.x, Is.EqualTo(3f).Within(0.001f));
                obstacle.Advance(4f);
                Assert.That(obstacleObject.transform.localPosition.x, Is.EqualTo(-3f).Within(0.001f));

                obstacle.ResetMotion();
                Assert.That(obstacleObject.transform.localPosition.x, Is.EqualTo(-3f).Within(0.001f));
                Assert.That(obstacle.CurrentWaypointIndex, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obstacleObject);
            }
        }

        [Test]
        public void MovingPrefabsUseApprovedWaypointLayouts()
        {
            AssertMovingLayout(
                "TRK_Moving_Barrier",
                new Vector3(-3f, 0f, 0f),
                new Vector3(3f, 0f, 0f));
            AssertMovingLayout(
                "TRK_Moving_Wall_Sweep",
                new Vector3(-1.5f, 0f, 0f),
                new Vector3(1.5f, 0f, 0f));
        }

        [Test]
        public void BlenderSegmentModelsImportFromZeroToPositiveTwentyFourMetres()
        {
            string[] modelNames =
            {
                "TRK_Straight_Wide",
                "TRK_Straight_Narrow",
                "TRK_Gap",
                "TRK_Tunnel_Straight"
            };

            foreach (string modelName in modelNames)
            {
                string path = $"Assets/EndlessRollball/Art/Track/{modelName}.fbx";
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(model, Is.Not.Null, path);

                GameObject instance = UnityEngine.Object.Instantiate(model);
                try
                {
                    Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                    Assert.That(renderers, Is.Not.Empty, path);

                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers.Skip(1))
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }

                    Assert.That(bounds.min.z, Is.EqualTo(0f).Within(0.02f), path);
                    Assert.That(bounds.max.z, Is.EqualTo(24f).Within(0.02f), path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void VisualReplacementPreservesCollidersAndDisablesGrayboxRenderers()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { "Assets/EndlessRollball/Prefabs/Track" });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab.GetComponent<TrackSegment>(), Is.Not.Null, path);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Not.Empty, path);

                Transform visual = prefab.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "VisualMesh");
                Assert.That(visual, Is.Not.Null, path);
                Assert.That(visual.GetComponentsInChildren<Renderer>(true), Is.Not.Empty, path);
                Assert.That(
                    visual.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.enabled),
                    Is.True,
                    path);

                Renderer[] grayboxRenderers = prefab.GetComponentsInChildren<Renderer>(true)
                    .Where(renderer => !renderer.transform.IsChildOf(visual))
                    .Where(renderer => !IsObstacleVisual(renderer.transform))
                    .ToArray();
                Assert.That(grayboxRenderers.All(renderer => !renderer.enabled), Is.True, path);
            }
        }

        private static string[] GenerateSequence(int seed)
        {
            List<TrackCandidate> candidates = CreateCandidates();
            List<string> sequence = new List<string>();
            TrackSelectionState state = TrackSelectionState.Initial;
            System.Random random = new System.Random(seed);

            for (int index = 0; index < 50; index++)
            {
                TrackSelectionResult result = TrackSelectionEngine.Select(
                    candidates,
                    random,
                    state,
                    0.45f,
                    index < 2);

                Assert.That(result.Success, Is.True);
                sequence.Add(result.Candidate.Id);
                state = result.NextState;
            }

            return sequence.ToArray();
        }

        private static List<TrackCandidate> CreateCandidates()
        {
            return new[]
            {
                new TrackCandidate("wide", null, TrackSegmentKind.Empty, TrackLaneMask.All, TrackLaneMask.All, false, 5f),
                new TrackCandidate("tunnel", null, TrackSegmentKind.Tunnel, TrackLaneMask.All, TrackLaneMask.All, false, 1f),
                new TrackCandidate("narrow", null, TrackSegmentKind.Narrow, TrackLaneMask.All, TrackLaneMask.Centre, false, 2f),
                new TrackCandidate("gap", null, TrackSegmentKind.Gap, TrackLaneMask.All, TrackLaneMask.All, true, 2f),
                new TrackCandidate("wall-left", null, TrackSegmentKind.StaticObstacle, TrackLaneMask.All, TrackLaneMask.Centre | TrackLaneMask.Right, false, 3f),
                new TrackCandidate("wall-right", null, TrackSegmentKind.StaticObstacle, TrackLaneMask.All, TrackLaneMask.Left | TrackLaneMask.Centre, false, 3f),
                new TrackCandidate("moving", null, TrackSegmentKind.MovingObstacle, TrackLaneMask.All, TrackLaneMask.All, false, 2f),
                new TrackCandidate("moving-sweep", null, TrackSegmentKind.MovingObstacle, TrackLaneMask.All, TrackLaneMask.All, false, 2f)
            }.ToList();
        }

        private static void AssertProfile(
            DifficultyMode mode,
            string expectedIdentifier,
            float expectedStartProbability,
            float expectedEndProbability,
            float expectedStartMovingSpeed,
            float expectedEndMovingSpeed)
        {
            DifficultyProfile profile = AssetDatabase.LoadAssetAtPath<DifficultyProfile>(
                $"Assets/EndlessRollball/Resources/Track/Difficulty_{mode}.asset");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.TrackIdentifier, Is.EqualTo(expectedIdentifier));

            DifficultySnapshot start = profile.Evaluate(0f);
            DifficultySnapshot end = profile.Evaluate(1000f);
            Assert.That(start.ObstacleProbability, Is.EqualTo(expectedStartProbability).Within(0.0001f));
            Assert.That(end.ObstacleProbability, Is.EqualTo(expectedEndProbability).Within(0.0001f));
            Assert.That(start.MovingObstacleSpeed, Is.EqualTo(expectedStartMovingSpeed).Within(0.0001f));
            Assert.That(end.MovingObstacleSpeed, Is.EqualTo(expectedEndMovingSpeed).Within(0.0001f));
        }

        private static void AssertMovingLayout(
            string prefabName,
            Vector3 expectedFirst,
            Vector3 expectedSecond)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/EndlessRollball/Prefabs/Track/{prefabName}.prefab");
            MovingObstacle obstacle = prefab.GetComponentInChildren<MovingObstacle>(true);
            Assert.That(obstacle, Is.Not.Null, prefabName);
            Assert.That(obstacle.WaypointCount, Is.EqualTo(2), prefabName);
            Assert.That(obstacle.TraversalMode, Is.EqualTo(WaypointTraversalMode.PingPong), prefabName);
            Assert.That(obstacle.GetWaypointOffset(0), Is.EqualTo(expectedFirst), prefabName);
            Assert.That(obstacle.GetWaypointOffset(1), Is.EqualTo(expectedSecond), prefabName);
        }

        private static bool IsObstacleVisual(Transform item)
        {
            while (item != null)
            {
                if (item.name == "ObstacleVisualMesh")
                {
                    return true;
                }

                item = item.parent;
            }

            return false;
        }
    }
}
