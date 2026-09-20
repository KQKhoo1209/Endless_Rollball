using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed class TrackGenerator : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private TrackCatalog catalog;
        [SerializeField] private DifficultyManager difficultyManager;
        [SerializeField] private Transform poolRoot;
        [SerializeField] private float startZ = -12f;
        [SerializeField, Min(TrackSegment.ContractLength)] private float aheadDistance = 144f;
        [SerializeField, Min(0f)] private float recycleBehindDistance = 24f;
        [SerializeField, Min(0)] private int safeStartingSegments = 2;

        private readonly Queue<ActiveSegment> activeSegments = new Queue<ActiveSegment>();
        private readonly Dictionary<TrackSegment, Queue<TrackSegment>> pools =
            new Dictionary<TrackSegment, Queue<TrackSegment>>();
        private readonly List<string> generatedSequence = new List<string>();

        private System.Random random;
        private List<TrackCandidate> candidates;
        private TrackSelectionState selectionState;
        private TrackRunConfig currentRun;
        private float nextSpawnZ;
        private int spawnedCount;
        private bool initialized;

        public int ActiveSegmentCount => activeSegments.Count;
        public IReadOnlyList<string> GeneratedSequence => generatedSequence;
        public int CurrentSeed => currentRun.Seed;
        public string TrackIdentifier => currentRun.TrackIdentifier;

        private void Start()
        {
            if (!initialized && difficultyManager != null && difficultyManager.IsConfigured)
            {
                Initialize(difficultyManager.CurrentRun);
            }
        }

        private void Update()
        {
            if (!initialized || player == null)
            {
                return;
            }

            Tick();
        }

        public void Initialize(TrackRunConfig runConfig)
        {
            if (catalog == null)
            {
                Debug.LogError("Track catalog is invalid: no catalog is assigned.", this);
                initialized = false;
                return;
            }

            if (!catalog.IsValid(out string reason))
            {
                Debug.LogError($"Track catalog is invalid: {reason}", this);
                initialized = false;
                return;
            }

            currentRun = runConfig;
            candidates = catalog.BuildCandidates(runConfig.Mode);
            initialized = true;
            ResetRun();
        }

        public void ResetRun()
        {
            if (!initialized)
            {
                if (difficultyManager == null || !difficultyManager.IsConfigured)
                {
                    return;
                }

                Initialize(difficultyManager.CurrentRun);
                return;
            }

            ReturnAllSegments();
            random = new System.Random(currentRun.Seed);
            selectionState = TrackSelectionState.Initial;
            generatedSequence.Clear();
            nextSpawnZ = startZ;
            spawnedCount = 0;
            EnsureTrackAhead();
        }

        public void Reconfigure(DifficultyMode mode, int? seedOverride = null)
        {
            if (difficultyManager == null)
            {
                return;
            }

            Initialize(difficultyManager.Configure(mode, seedOverride));
        }

        public void Tick()
        {
            RecyclePassedSegments();
            EnsureTrackAhead();
        }

        public void ConfigureForScene(
            Transform configuredPlayer,
            TrackCatalog configuredCatalog,
            DifficultyManager configuredDifficultyManager,
            Transform configuredPoolRoot)
        {
            player = configuredPlayer;
            catalog = configuredCatalog;
            difficultyManager = configuredDifficultyManager;
            poolRoot = configuredPoolRoot;
        }

        private void EnsureTrackAhead()
        {
            float playerZ = player == null ? 0f : player.position.z;

            while (nextSpawnZ < playerZ + aheadDistance)
            {
                SpawnNextSegment(spawnedCount < safeStartingSegments);
            }
        }

        private void SpawnNextSegment(bool forceSafeStart)
        {
            DifficultySnapshot snapshot = difficultyManager != null
                ? difficultyManager.UpdateDistance(Mathf.Max(0f, player == null ? 0f : player.position.z))
                : new DifficultySnapshot(currentRun.Mode, 6f, 3f, 0.25f, 0f, 0f);

            TrackSelectionResult selection = TrackSelectionEngine.Select(
                candidates,
                random,
                selectionState,
                snapshot.ObstacleProbability,
                forceSafeStart);

            TrackSegment prefab;
            if (selection.Success && selection.Candidate.Prefab != null)
            {
                prefab = selection.Candidate.Prefab;
                selectionState = selection.NextState;
            }
            else
            {
                prefab = catalog.FallbackSegment;
                TrackCandidate fallback = TrackCandidate.FromSegment(prefab, currentRun.Mode);
                TrackLaneMask reachable = TrackLaneMaskUtility.ExpandAdjacent(selectionState.ReachableLanes)
                    & fallback.EntryLanes
                    & fallback.SafeLanes;
                selectionState = new TrackSelectionState(
                    reachable == TrackLaneMask.None ? TrackLaneMask.All : reachable,
                    fallback.Kind);
                Debug.LogWarning("No compatible weighted segment was available; using the safe fallback.", this);
            }

            TrackSegment instance = Acquire(prefab);
            instance.transform.SetPositionAndRotation(new Vector3(0f, 0f, nextSpawnZ), Quaternion.identity);
            instance.Activate(snapshot);

            float endZ = nextSpawnZ + TrackSegment.ContractLength;
            activeSegments.Enqueue(new ActiveSegment(prefab, instance, endZ));
            generatedSequence.Add(prefab.SegmentId);
            nextSpawnZ = endZ;
            spawnedCount++;
        }

        private void RecyclePassedSegments()
        {
            if (player == null)
            {
                return;
            }

            float recycleLine = player.position.z - recycleBehindDistance;
            while (activeSegments.Count > 0 && activeSegments.Peek().EndZ < recycleLine)
            {
                Return(activeSegments.Dequeue());
            }
        }

        private TrackSegment Acquire(TrackSegment prefab)
        {
            if (!pools.TryGetValue(prefab, out Queue<TrackSegment> pool))
            {
                pool = new Queue<TrackSegment>();
                pools.Add(prefab, pool);
            }

            if (pool.Count > 0)
            {
                return pool.Dequeue();
            }

            TrackSegment instance = Instantiate(prefab, poolRoot == null ? transform : poolRoot);
            instance.name = $"{prefab.name}_Pooled";
            return instance;
        }

        private void Return(ActiveSegment active)
        {
            active.Instance.Deactivate();
            active.Instance.transform.SetParent(poolRoot == null ? transform : poolRoot, false);

            if (!pools.TryGetValue(active.Prefab, out Queue<TrackSegment> pool))
            {
                pool = new Queue<TrackSegment>();
                pools.Add(active.Prefab, pool);
            }

            pool.Enqueue(active.Instance);
        }

        private void ReturnAllSegments()
        {
            while (activeSegments.Count > 0)
            {
                Return(activeSegments.Dequeue());
            }
        }

        private sealed class ActiveSegment
        {
            public ActiveSegment(TrackSegment prefab, TrackSegment instance, float endZ)
            {
                Prefab = prefab;
                Instance = instance;
                EndZ = endZ;
            }

            public TrackSegment Prefab { get; }
            public TrackSegment Instance { get; }
            public float EndZ { get; }
        }
    }
}
