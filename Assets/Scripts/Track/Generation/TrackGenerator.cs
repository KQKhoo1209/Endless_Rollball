using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessRollball.Scenery;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private TrackCatalog catalog;
        [SerializeField] private DifficultyManager difficultyManager;
        [SerializeField] private Transform poolRoot;
        [SerializeField] private EnvironmentManager environmentManager;
        [SerializeField] private ObstacleManager obstacleManager;
        [SerializeField] private SessionTaskTracker taskTracker;
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
        public DifficultyMode CurrentDifficulty => currentRun.Mode;
        public string TrackIdentifier => effectiveTrackIdentifier ?? currentRun.TrackIdentifier;
        private string effectiveTrackIdentifier;

        public event Action RunResetting;
        public event Action RunPrepared;

        public bool IsRunInitialized => initialized;

        private bool generationPaused;

        public void SetGenerationPaused(bool paused)
        {
            generationPaused = paused;
            if (obstacleManager != null)
                obstacleManager.SetMotionPaused(paused);
        }

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
            initialized = true;
            ResetRun();
        }

        public void ResetRun()
        {
            if (!initialized)
            {
                if (difficultyManager == null || !difficultyManager.IsConfigured)
                    return;

                Initialize(difficultyManager.CurrentRun);
                return;
            }

            RunResetting?.Invoke();
            generationPaused = false;

            ReturnAllSegments();

            if (obstacleManager != null)
                obstacleManager.EndRun();
            taskTracker?.ResetRoute();

            random = new System.Random(currentRun.Seed);
            selectionState = TrackSelectionState.Initial;
            generatedSequence.Clear();
            nextSpawnZ = startZ;
            spawnedCount = 0;

            GameMode runMode =
                GameSessionSettings.SelectedMode ?? GameMode.Standard;

            environmentManager?.BeginRun(
                runMode, currentRun.Mode, currentRun.Seed, startZ);

            if (catalog.RequiresThemeRoadSelection)
            {
                string roadReason = "Assign the active EnvironmentManager for this themed-road catalogue.";
                if (environmentManager == null ||
                    !environmentManager.TryValidatePlayableRoadTracks(currentRun.Mode, out roadReason))
                {
                    Debug.LogError($"Playable road initialization failed: {roadReason}", this);
                    environmentManager?.EndRun();
                    initialized = false;
                    return;
                }
            }

            candidates = catalog.BuildCandidates(currentRun.Mode);
            effectiveTrackIdentifier = currentRun.TrackIdentifier;

            bool scheduled = environmentManager != null &&
                environmentManager.HasScheduledJourney;
            bool suppressRandomTunnels = runMode == GameMode.FastPaced ||
                (environmentManager != null &&
                 environmentManager.SuppressRandomTunnels);

            if (suppressRandomTunnels)
            {
                candidates.RemoveAll(
                    candidate => candidate.Kind == TrackSegmentKind.Tunnel);
            }

            if (!TryConfigureFiniteRoute(runMode))
            {
                environmentManager?.EndRun();
                initialized = false;
                return;
            }

            if (scheduled || hasFiniteRoute)
            {
                TrackSegment safeFloor = catalog.FallbackSegment;
                if (safeFloor == null ||
                    safeFloor.Kind != TrackSegmentKind.Empty ||
                    safeFloor.RequiredJump ||
                    safeFloor.EntryLanes != TrackLaneMask.All ||
                    safeFloor.SafeLanes != TrackLaneMask.All)
                {
                    Debug.LogError(
                        "Scheduled journeys and finite finish corridors require " +
                        "the catalogue's clear wide fallback with all lanes open.",
                        this);
                    environmentManager?.EndRun();
                    hasFiniteRoute = false;
                    initialized = false;
                    return;
                }
            }

            string environmentIdentifier = environmentManager != null
                ? environmentManager.JourneyIdentifier : string.Empty;
            if (!string.IsNullOrEmpty(environmentIdentifier))
            {
                effectiveTrackIdentifier =
                    $"{currentRun.TrackIdentifier}-{environmentIdentifier}";
            }
            else if (runMode == GameMode.FastPaced)
            {
                effectiveTrackIdentifier =
                    $"{currentRun.TrackIdentifier}-fastpaced-env5d-v1-no-tunnel";
            }

            if (hasFiniteRoute)
                effectiveTrackIdentifier += "-" + finiteRouteIdentifier;

            if (catalog.RequiresThemeRoadSelection)
                effectiveTrackIdentifier += "-playable-roads-v1-straight-only";

            if (obstacleManager != null &&
                obstacleManager.BeginRun(currentRun.Seed))
            {
                if (catalog.RequiresThemeRoadSelection && obstacleManager.GapTracksEnabled &&
                    !environmentManager.TryValidatePlayableGapTracks(currentRun.Mode, out string gapReason))
                {
                    Debug.LogError($"Gap road initialization failed: {gapReason}", this);
                    obstacleManager.EndRun();
                    environmentManager.EndRun();
                    hasFiniteRoute = false;
                    initialized = false;
                    return;
                }
                effectiveTrackIdentifier += "-obstacles-fixed-moving-row-gap-v6";
            }
            if (taskTracker != null)
                effectiveTrackIdentifier += "-scored-tasks-v2-finite-recovery";

            EnsureTrackAhead();
            RunPrepared?.Invoke();
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
            if (!initialized || generationPaused)
                return;

            RecyclePassedSegments();
            EnsureTrackAhead();

            if (player != null)
                environmentManager?.UpdateJourneyPresentation(player.position.z);
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

            while (nextSpawnZ < playerZ + aheadDistance &&
                   (!hasFiniteRoute || spawnedCount < finiteSegmentLimit))
            {
                if (!SpawnNextSegment(spawnedCount < safeStartingSegments))
                    break;
            }
        }

        private bool SpawnNextSegment(bool forceSafeStart)
        {
            DifficultySnapshot snapshot = difficultyManager != null
                ? difficultyManager.UpdateDistance(
                    Mathf.Max(0f, player == null ? 0f : player.position.z))
                : new DifficultySnapshot(
                    currentRun.Mode, 6f, 3f, 0.25f, 0f, 0f);

            bool requiresClearTrack =
                (environmentManager != null &&
                 environmentManager.RequiresClearTrack(spawnedCount)) ||
                IsFinishCorridor(spawnedCount);

            TrackSegment prefab;
            ObstacleManager.EncounterPlan encounter = default;

            if (catalog.RequiresThemeRoadSelection)
            {
                // The scheduled theme is resolved BEFORE acquiring a complete track.
                // Plan one encounter before acquiring either a clear road OR its gap variant.
                if (environmentManager == null ||
                    !environmentManager.TryGetPlayableRoadTrack(spawnedCount, out prefab))
                {
                    Debug.LogError($"No playable themed road for segment {spawnedCount}; generation stopped.", this);
                    generationPaused = true;
                    return false;
                }
                bool beforeTunnel = environmentManager.IsTransitionSegment(spawnedCount + 1);
                bool blockEncounter = forceSafeStart || requiresClearTrack || beforeTunnel;
                float obstacleDistance = Mathf.Max(0f, nextSpawnZ + TrackSegment.ContractLength * 0.5f);
                DifficultySnapshot obstacleDifficulty = difficultyManager != null
                    ? difficultyManager.EvaluateForDistance(obstacleDistance) : snapshot;

                if (obstacleManager != null)
                {
                    TrackSegment gap = obstacleManager.GapTracksEnabled
                        ? environmentManager.GetGapTrackForRoad(prefab) : null;
                    encounter = obstacleManager.PlanEncounter(prefab, gap, spawnedCount, blockEncounter,
                        obstacleDifficulty.ObstacleProbability, obstacleDifficulty.MovingObstacleSpeed);
                    if (encounter.IsGap)
                        prefab = encounter.Track;
                }
                // Record only the ACTUAL selected track, never first Empty then Gap.
                UpdateSelectionStateForForcedSegment(prefab);
            }
            else if (requiresClearTrack)
            {
                prefab = catalog.FallbackSegment;
                UpdateSelectionStateForForcedSegment(prefab);
            }
            else
            {
                TrackSelectionResult selection = TrackSelectionEngine.Select(
                    candidates,
                    random,
                    selectionState,
                    snapshot.ObstacleProbability,
                    forceSafeStart);

                if (selection.Success && selection.Candidate.Prefab != null)
                {
                    prefab = selection.Candidate.Prefab;
                    selectionState = selection.NextState;
                }
                else
                {
                    prefab = catalog.FallbackSegment;
                    UpdateSelectionStateForForcedSegment(prefab);
                    Debug.LogWarning(
                        "No compatible weighted segment was available; " +
                        "using the safe fallback.",
                        this);
                }
            }

            TrackSegment instance = Acquire(prefab);
            instance.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, nextSpawnZ),
                Quaternion.identity);
            instance.Activate(snapshot);

            environmentManager?.Attach(instance, spawnedCount);
            taskTracker?.RegisterTrack(instance);

            if (obstacleManager != null)
            {
                obstacleManager.AttachPlanned(instance, encounter);
            }

            float endZ = nextSpawnZ + TrackSegment.ContractLength;
            activeSegments.Enqueue(new ActiveSegment(prefab, instance, endZ));
            generatedSequence.Add(prefab.SegmentId);
            nextSpawnZ = endZ;
            spawnedCount++;
            return true;
        }

        private void UpdateSelectionStateForForcedSegment(TrackSegment prefab)
        {
            TrackLaneMask reachable =
                TrackLaneMaskUtility.ExpandAdjacent(selectionState.ReachableLanes)
                & prefab.EntryLanes
                & prefab.SafeLanes;

            selectionState = new TrackSelectionState(
                reachable == TrackLaneMask.None
                    ? TrackLaneMask.All
                    : reachable,
                prefab.Kind);
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
            taskTracker?.ReleaseTrack(active.Instance);
            if (obstacleManager != null)
                obstacleManager.Release(active.Instance);

            environmentManager?.Release(active.Instance);
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
