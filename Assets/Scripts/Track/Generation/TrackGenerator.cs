using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessRollball.Scenery;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator : MonoBehaviour
    {
        [SerializeField]
        private Transform player;
        [SerializeField]
        private TrackCatalog catalog;
        [SerializeField]
        private DifficultyManager difficultyManager;
        [SerializeField]
        private Transform poolRoot;
        [SerializeField]
        private EnvironmentManager environmentManager;
        [SerializeField]
        private ObstacleManager obstacleManager;
        [SerializeField]
        private SessionTaskTracker taskTracker;
        [SerializeField]
        private Material corridorMarkerMaterial;
        [SerializeField]
        private float startZ = -12f;
        [SerializeField, Min(TrackSegment.ContractLength)]
        private float aheadDistance = 144f;
        [SerializeField, Min(0f)]
        private float recycleBehindDistance = 24f;
        [SerializeField, Min(0)]
        private int safeStartingSegments = 2;
        private readonly Queue<ActiveSegment> activeSegments = new Queue<ActiveSegment>();
        private readonly Dictionary<TrackSegment, Queue<TrackSegment>> pools = new Dictionary<TrackSegment, Queue<TrackSegment>>();
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
        public event Action<string> RunSetupFailed;
        public bool IsRunInitialized => initialized;
        public bool IsRunPrepared { get; private set; }
        public event Action<bool> StreamingHoldChanged;
        private bool streamingHold;
        private Coroutine preparation;
        private int preparationVersion;
        [SerializeField] private bool logLoadingTimings;
        private void SetStreamingHold(bool value)
        {
            if (streamingHold == value) return;
            streamingHold = value;
            StreamingHoldChanged?.Invoke(value);
        }
        private bool HorizonReady => (hasFiniteRoute && spawnedCount >= finiteSegmentLimit) ||
            nextSpawnZ >= (player == null ? 0f : player.position.z) + aheadDistance;
        private void FailPreparation(string reason)
        {
            GameplayLoadingTransition.AbortPreparation();
            initialized = IsRunPrepared = false;
            generationPaused = true;
            hasFiniteRoute = false;
            obstacleManager?.EndRun();
            environmentManager?.EndRun();
            RunSetupFailed?.Invoke(reason);
        }
        private void OnDisable()
        {
            if (!IsRunPrepared) GameplayLoadingTransition.AbortPreparation();
            preparationVersion++;
            if (preparation != null) StopCoroutine(preparation);
            preparation = null;
            IsRunPrepared = initialized = false;
            generationPaused = true;
        }


        private bool generationPaused;
        public void SetGenerationPaused(bool paused)
        {
            generationPaused = paused;
            if (obstacleManager != null)
                obstacleManager.SetMotionPaused(paused);
        }

        private void Start()
        {
            if (logLoadingTimings && GameSessionSettings.GameplayLoadRequestedAt.HasValue)
                Debug.Log($"[Loading] Gameplay scene Start reached {Time.realtimeSinceStartupAsDouble - GameSessionSettings.GameplayLoadRequestedAt.Value:F3}s after menu Start.", this);
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
                FailPreparation("Track catalog is invalid: no catalog is assigned.");
                return;
            }

            if (!catalog.IsValid(out string reason))
            {
                FailPreparation($"Track catalog is invalid: {reason}");
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

            IsRunPrepared = false;
            GameplayLoadingTransition.CoverPreparation();
            SetStreamingHold(false);
            RunResetting?.Invoke();
            if (preparation != null) StopCoroutine(preparation);
            preparation = StartCoroutine(PrepareRun(++preparationVersion));
        }

        private System.Collections.IEnumerator PrepareRun(int version)
        {
            double startedAt = Time.realtimeSinceStartupAsDouble;
            generationPaused = true;
            ReturnAllSegments();
            if (obstacleManager != null)
                obstacleManager.EndRun();
            taskTracker?.ResetRoute();
            random = new System.Random(currentRun.Seed);
            selectionState = TrackSelectionState.Initial;
            generatedSequence.Clear();
            nextSpawnZ = startZ;
            spawnedCount = 0;
            GameMode runMode = GameSessionSettings.SelectedMode ?? GameMode.Standard;
            if (environmentManager != null)
            {
                yield return environmentManager.BeginRunAsync(runMode, currentRun.Mode, currentRun.Seed, startZ);
                if (version != preparationVersion) yield break;
                if (!environmentManager.IsReady)
                {
                    FailPreparation(environmentManager.LoadingError ?? "Environment preparation failed. Check its configuration.");
                    yield break;
                }
            }
            if (logLoadingTimings)
                Debug.Log($"[Loading] Starting theme ready after {Time.realtimeSinceStartupAsDouble - startedAt:F3}s.", this);
            if (catalog.RequiresThemeRoadSelection)
            {
                string roadReason = "Assign the active EnvironmentManager for this themed-road catalogue.";
                if (environmentManager == null || !environmentManager.TryValidatePlayableRoadTracks(currentRun.Mode, out roadReason))
                {
                    Debug.LogError($"Playable road initialization failed: {roadReason}", this);
                    environmentManager?.EndRun();
                    FailPreparation("Playable road initialization failed: " + roadReason);
                    yield break;
                }
            }

            candidates = catalog.BuildCandidates(currentRun.Mode);
            effectiveTrackIdentifier = currentRun.TrackIdentifier;
            bool scheduled = environmentManager != null && environmentManager.HasScheduledJourney;
            bool suppressRandomTunnels = runMode == GameMode.FastPaced || (environmentManager != null && environmentManager.SuppressRandomTunnels);
            if (suppressRandomTunnels)
            {
                candidates.RemoveAll(candidate => candidate.Kind == TrackSegmentKind.Tunnel);
            }

            if (!TryConfigureFiniteRoute(runMode))
            {
                environmentManager?.EndRun();
                FailPreparation("Finite route configuration failed.");
                yield break;
            }

            if (scheduled || hasFiniteRoute)
            {
                TrackSegment safeFloor = catalog.FallbackSegment;
                if (safeFloor == null || safeFloor.Kind != TrackSegmentKind.Empty || safeFloor.RequiredJump || safeFloor.EntryLanes != TrackLaneMask.All || safeFloor.SafeLanes != TrackLaneMask.All)
                {
                    Debug.LogError("Scheduled journeys and finite finish corridors require " + "the catalogue's clear wide fallback with all lanes open.", this);
                    environmentManager?.EndRun();
                    hasFiniteRoute = false;
                    FailPreparation("A clear fallback track is required.");
                    yield break;
                }
            }

            string environmentIdentifier = environmentManager != null ? environmentManager.JourneyIdentifier : string.Empty;
            if (!string.IsNullOrEmpty(environmentIdentifier))
            {
                effectiveTrackIdentifier = $"{currentRun.TrackIdentifier}-{environmentIdentifier}";
            }
            else if (runMode == GameMode.FastPaced)
            {
                effectiveTrackIdentifier = $"{currentRun.TrackIdentifier}-fastpaced-env5d-v1-no-tunnel";
            }

            if (hasFiniteRoute)
                effectiveTrackIdentifier += "-" + finiteRouteIdentifier;
            if (catalog.RequiresThemeRoadSelection)
                effectiveTrackIdentifier += "-playable-roads-v1-straight-only";
            if (obstacleManager != null)
            {
                if (!obstacleManager.BeginRun(currentRun.Seed))
                {
                    const string reason = "Obstacle initialization failed. Review the obstacle assignments and collider configuration in the Console.";
                    generationPaused = true;
                    initialized = false;
                    hasFiniteRoute = false;
                    environmentManager?.EndRun();
                    Debug.LogError(reason, this);
                    RunSetupFailed?.Invoke(reason);
                    yield break;
                }

                if (catalog.RequiresThemeRoadSelection && obstacleManager.GapTracksEnabled && !environmentManager.TryValidatePlayableGapTracks(currentRun.Mode, out string gapReason))
                {
                    Debug.LogError($"Gap road initialization failed: {gapReason}", this);
                    obstacleManager.EndRun();
                    environmentManager.EndRun();
                    hasFiniteRoute = false;
                    FailPreparation("Gap initialization failed: " + gapReason);
                    yield break;
                }

                effectiveTrackIdentifier += "-encounters-v14-dungeon-rolling-stone-6m";
            }

            if (taskTracker != null)
                effectiveTrackIdentifier += "-scored-tasks-v4-staged";
            while (!HorizonReady)
            {
                if (version != preparationVersion) yield break;
                if (!SpawnNextSegment(spawnedCount < safeStartingSegments))
                {
                    if (!initialized) yield break;
                    if (environmentManager == null || environmentManager.LoadingError != null)
                    {
                        FailPreparation(environmentManager?.LoadingError ?? "Initial track generation failed.");
                        yield break;
                    }
                }
                yield return null;
            }
            if (version != preparationVersion) yield break;
            Camera view = environmentManager != null ? environmentManager.GameplayCamera : Camera.main;
            if (view == null || !view.isActiveAndEnabled)
            {
                FailPreparation("An active gameplay camera is required before revealing the prepared scene.");
                yield break;
            }
            yield return GameplayLoadingTransition.RevealPreparedScene(view);
            if (version != preparationVersion) yield break;
            IsRunPrepared = true;
            preparation = null;
            if (logLoadingTimings)
                Debug.Log($"[Loading] Initial track, scenery and obstacles ready after {Time.realtimeSinceStartupAsDouble - startedAt:F3}s.", this);
            if (logLoadingTimings && GameSessionSettings.GameplayLoadRequestedAt.HasValue)
                Debug.Log($"[Loading] Total menu-to-ready: {Time.realtimeSinceStartupAsDouble - GameSessionSettings.GameplayLoadRequestedAt.Value:F3}s.", this);
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
            if (!IsRunPrepared || generationPaused)
                return;
            RecyclePassedSegments();
            if (player != null && environmentManager != null)
            {
                DifficultySnapshot snapshot = difficultyManager.CurrentSnapshot;
                environmentManager.Prefetch(player.position.z, aheadDistance,
                    snapshot.ForwardSpeed + snapshot.SpeedAdjustmentRange, spawnedCount);
            }
            EnsureTrackAhead();
            if (!initialized) return;
            if (environmentManager != null && environmentManager.LoadingError != null)
            {
                FailPreparation(environmentManager.LoadingError);
                return;
            }
            if (player != null && environmentManager != null)
            {
                float maxSpeed = difficultyManager.CurrentSnapshot.ForwardSpeed + difficultyManager.CurrentSnapshot.SpeedAdjustmentRange;
                if (!HorizonReady && !environmentManager.StreamingHoldRequired &&
                    player.position.z >= nextSpawnZ - Mathf.Max(TrackSegment.ContractLength * .5f, maxSpeed))
                    environmentManager.CoverForPendingTrack();
                environmentManager.UpdateJourneyPresentation(player.position.z, HorizonReady);
                SetStreamingHold(environmentManager.StreamingHoldRequired);
            }
        }

        public void ConfigureForScene(Transform configuredPlayer, TrackCatalog configuredCatalog, DifficultyManager configuredDifficultyManager, Transform configuredPoolRoot)
        {
            player = configuredPlayer;
            catalog = configuredCatalog;
            difficultyManager = configuredDifficultyManager;
            poolRoot = configuredPoolRoot;
        }

        private void EnsureTrackAhead()
        {
            float playerZ = player == null ? 0f : player.position.z;
            while (nextSpawnZ < playerZ + aheadDistance && (!hasFiniteRoute || spawnedCount < finiteSegmentLimit))
            {
                if (!SpawnNextSegment(spawnedCount < safeStartingSegments))
                    break;
            }
        }

        private bool SpawnNextSegment(bool forceSafeStart)
        {
            // Check availability BEFORE planning consumes random choices or reservations.
            if (environmentManager != null && !environmentManager.IsSegmentSceneryReady(spawnedCount))
                return false;
            DifficultySnapshot snapshot = difficultyManager != null ? difficultyManager.UpdateDistance(Mathf.Max(0f, player == null ? 0f : player.position.z)) : new DifficultySnapshot(currentRun.Mode, 6f, 3f, 0.25f, 0f, 0f);
            if (hasFiniteRoute && spawnedCount == finishSegmentIndex && nextSpawnZ + TrackSegment.ContractLength <= finishLineZ)
            {
                finishSegmentIndex++;
                finiteSegmentLimit++;
            }
            bool requiresClearTrack = (environmentManager != null &&
                (environmentManager.RequiresClearTrack(spawnedCount))) || IsFinishCorridor(spawnedCount) || (hasFiniteRoute && spawnedCount >= finishSegmentIndex - 1);
            TrackSegment prefab;
            ObstacleManager.SegmentPlan encounter = default;
            if (catalog.RequiresThemeRoadSelection)
            {
                // The scheduled theme is resolved BEFORE acquiring a complete track.
                // Plan ordered encounters before acquiring either a clear road OR its gap variant.
                if (environmentManager == null || !environmentManager.TryGetPlayableRoadTrack(spawnedCount, out prefab))
                {
                    FailPreparation($"No playable themed road for segment {spawnedCount}; generation stopped.");
                    return false;
                }

                bool beforeTunnel = environmentManager.IsTransitionSegment(spawnedCount + 1);
                bool blockEncounter = forceSafeStart || requiresClearTrack || beforeTunnel;
                float obstacleDistance = Mathf.Max(0f, nextSpawnZ + TrackSegment.ContractLength * 0.5f);
                DifficultySnapshot obstacleDifficulty = difficultyManager != null ? difficultyManager.EvaluateForDistance(obstacleDistance) : snapshot;
                if (obstacleManager != null)
                {
                    TrackSegment gap = obstacleManager.GapTracksEnabled ? environmentManager.GetGapTrackForRoad(prefab, obstacleDifficulty) : null;
                    ThemeObstacleLibrary library = environmentManager.GetObstacleLibraryForRoad(prefab);

                    if (library != null && library.CityPacing != null)
                    {
                        if (!obstacleManager.TryTakeCityPlan(spawnedCount, out encounter))
                        {
                            var requests = new System.Collections.Generic.List<ObstacleManager.CityPlanningRequest>(8);
                            for (int offset = 0; offset < 8; offset++)
                            {
                                int futureIndex = spawnedCount + offset;
                                if ((hasFiniteRoute && futureIndex >= finiteSegmentLimit) ||
                                    !environmentManager.IsSegmentSceneryReady(futureIndex) ||
                                    !environmentManager.TryGetPlayableRoadTrack(futureIndex, out TrackSegment futureRoad)) break;
                                ThemeObstacleLibrary futureLibrary = environmentManager.GetObstacleLibraryForRoad(futureRoad);
                                if (futureLibrary != library) break;
                                float futureOrigin = nextSpawnZ + offset * TrackSegment.ContractLength;
                                DifficultySnapshot futureDifficulty = difficultyManager != null
                                    ? difficultyManager.EvaluateForDistance(Mathf.Max(0f, futureOrigin + 12f)) : snapshot;
                                requests.Add(new ObstacleManager.CityPlanningRequest {
                                    Road = futureRoad,
                                    Gap = obstacleManager.GapTracksEnabled ? environmentManager.GetGapTrackForRoad(futureRoad, futureDifficulty) : null,
                                    RecordEntry = environmentManager.RecordRouteEntry,
                                    Index = futureIndex, Zone = environmentManager.EncounterZoneAt(futureIndex), Origin = futureOrigin, Difficulty = futureDifficulty,
                                    Limit = EncounterReservationLimit(futureIndex, futureOrigin), Library = library,
                                    Blocked = futureIndex < safeStartingSegments || environmentManager.RequiresClearTrack(futureIndex) ||
                                        (IsFinishCorridor(futureIndex) || (hasFiniteRoute && futureIndex >= finishSegmentIndex - 1)) || environmentManager.IsTransitionSegment(futureIndex + 1),
                                    Junction = environmentManager.CanUseJunctionAt(futureIndex),
                                    Traffic = environmentManager.CanUseCityTrafficAt(futureIndex),
                                    Stops = environmentManager.CanUseRoadsideStopAt(futureIndex)
                                });
                            }
                            obstacleManager.PlanCityWindow(requests);
                            obstacleManager.TryTakeCityPlan(spawnedCount, out encounter);
                        }
                    }
                    else
                    {
                    var beachRequests = new System.Collections.Generic.List<ObstacleManager.BeachPlanningRequest>(3);
                    if(library!=null && library.BeachBarrierTracks.Length>0)
                        for(int offset=0;offset<3;offset++)
                        {
                            int futureIndex=spawnedCount+offset;
                            if((hasFiniteRoute && futureIndex>=finiteSegmentLimit) ||
                                !environmentManager.IsSegmentSceneryReady(futureIndex) ||
                                !environmentManager.TryGetPlayableRoadTrack(futureIndex,out TrackSegment futureRoad)) break;
                            var futureLibrary=environmentManager.GetObstacleLibraryForRoad(futureRoad);
                            if(futureLibrary!=library) break;
                            float futureOrigin=nextSpawnZ+offset*TrackSegment.ContractLength;
                            beachRequests.Add(new ObstacleManager.BeachPlanningRequest {
                                Road=futureRoad,Index=futureIndex,Zone=environmentManager.EncounterZoneAt(futureIndex),
                                Origin=futureOrigin,Limit=EncounterReservationLimit(futureIndex,futureOrigin),Library=library,
                                Difficulty=difficultyManager!=null?difficultyManager.EvaluateForDistance(Mathf.Max(0,futureOrigin+12)):snapshot,
                                Allowed=futureIndex>=safeStartingSegments && !environmentManager.RequiresClearTrack(futureIndex) &&
                                    !IsFinishCorridor(futureIndex) && !(hasFiniteRoute && futureIndex>=finishSegmentIndex-1) &&
                                    !environmentManager.IsTransitionSegment(futureIndex+1) && environmentManager.CanUseBeachBarrierAt(futureIndex)
                            });
                        }
                    if(!obstacleManager.PlanBeachWindow(beachRequests,out encounter))
                        encounter = obstacleManager.PlanSegment(prefab, gap, spawnedCount, blockEncounter, obstacleDifficulty, nextSpawnZ,
                            EncounterReservationLimit(spawnedCount, nextSpawnZ), library,
                            environmentManager.CanUseJunctionAt(spawnedCount),
                            environmentManager.CanUseCityTrafficAt(spawnedCount),
                            environmentManager.CanUseRoadsideStopAt(spawnedCount),false);
                    }
                    if (encounter.ReplacesTrack)
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
                TrackSelectionResult selection = TrackSelectionEngine.Select(candidates, random, selectionState, snapshot.ObstacleProbability, forceSafeStart);
                if (selection.Success && selection.Candidate.Prefab != null)
                {
                    prefab = selection.Candidate.Prefab;
                    selectionState = selection.NextState;
                }
                else
                {
                    prefab = catalog.FallbackSegment;
                    UpdateSelectionStateForForcedSegment(prefab);
                    Debug.LogWarning("No compatible weighted segment was available; " + "using the safe fallback.", this);
                }
            }

            TrackSegment instance = Acquire(prefab);
            var span = new TrackRouteSpan(spawnedCount, nextSpawnZ, prefab.EntryLocal.z, prefab.ExitLocal.z);
            float rootZ = span.RootZ;
            float physicalExitZ = span.ExitZ;
            environmentManager?.RecordRouteEntry(spawnedCount, nextSpawnZ, physicalExitZ);
            instance.transform.SetPositionAndRotation(new Vector3(0f, 0f, rootZ), Quaternion.identity);
            instance.Activate(snapshot);
            RoadCorridorMarkers markers = instance.GetComponent<RoadCorridorMarkers>();
            if (markers == null)
                markers = instance.gameObject.AddComponent<RoadCorridorMarkers>();
            markers.Configure(instance, difficultyManager, corridorMarkerMaterial);
            environmentManager?.Attach(instance, spawnedCount);
            taskTracker?.RegisterTrack(instance);
            if (obstacleManager != null)
            {
                obstacleManager.AttachSegment(instance, encounter, environmentManager, this);
            }

            float endZ = physicalExitZ;
            activeSegments.Enqueue(new ActiveSegment(prefab, instance, Mathf.Max(endZ, encounter.EndZ + 1f), spawnedCount));
            generatedSequence.Add(prefab.SegmentId);
            nextSpawnZ = endZ;
            spawnedCount++;
            return true;
        }

        private void UpdateSelectionStateForForcedSegment(TrackSegment prefab)
        {
            TrackLaneMask reachable = TrackLaneMaskUtility.ExpandAdjacent(selectionState.ReachableLanes) & prefab.EntryLanes & prefab.SafeLanes;
            selectionState = new TrackSelectionState(reachable == TrackLaneMask.None ? TrackLaneMask.All : reachable, prefab.Kind);
        }

        internal bool HasVisibleCityRoad(float minZ, float maxZ)
        {
            float covered = minZ;
            foreach (ActiveSegment entry in activeSegments)
            {
                TrackSegment road = entry.Instance;
                if (road == null || road.ExitAnchor.position.z <= covered) continue;
                if (road.EntryAnchor.position.z > covered + .01f) return false;
                int index = entry.Index;
                if (!road.IsPresentationVisible || road.RequiredJump || environmentManager == null ||
                    !environmentManager.IsStreetRoadSegment(index)) return false;
                covered = road.ExitAnchor.position.z;
                if (covered >= maxZ) return true;
            }
            return false;
        }

        internal bool HasVisibleDungeonRoad(float minZ, float maxZ)
        {
            float covered=minZ;
            foreach(ActiveSegment entry in activeSegments)
            {
                TrackSegment road=entry.Instance;
                if(road==null || road.ExitAnchor.position.z<=covered) continue;
                if(road.EntryAnchor.position.z>covered+.01f) return false;
                if(!road.IsPresentationVisible || road.RequiredJump || environmentManager==null ||
                    !environmentManager.IsDungeonRoadSegment(entry.Index)) return false;
                covered=road.ExitAnchor.position.z;
                if(covered>=maxZ) return true;
            }
            return false;
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
            public ActiveSegment(TrackSegment prefab, TrackSegment instance, float endZ, int index)
            {
                Index = index;
                Prefab = prefab;
                Instance = instance;
                EndZ = endZ;
            }

            public int Index { get; }
            public TrackSegment Prefab { get; }
            public TrackSegment Instance { get; }
            public float EndZ { get; }
        }
    }
}
