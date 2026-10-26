using System;
using System.Collections.Generic;
using UnityEngine;
using EndlessRollball.Infrastructure;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed partial class ObstacleManager : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField]
        private Transform activeRoot;
        [SerializeField]
        private Transform poolRoot;
        [SerializeField]
        private SessionTaskTracker taskTracker;
        [Header("Neutral fixed obstacles")]
        [SerializeField]
        private List<GameObject> fixedPositionPrefabs = new List<GameObject>();
        [Header("Neutral moving obstacles")]
        [SerializeField]
        private List<GameObject> movingPrefabs = new List<GameObject>();
        [SerializeField, Range(0f, 1f)]
        private float movingFraction = 0.35f;
        [Header("Jump-only obstacles")]
        [SerializeField]
        private List<GameObject> jumpableOnlyPrefabs = new List<GameObject>();
        [SerializeField, Range(0f, 1f)]
        private float jumpFraction = 0f;
        [Tooltip("Share of selected jump encounters that use a themed physical gap instead of a jump row. Applied at run start.")]
        [SerializeField, Range(0f, 1f)]
        private float gapShareWithinJump = 0f;
        [Header("Hard combination library")]
        [SerializeField]
        private ObstaclePattern[] combinationPatterns = Array.Empty<ObstaclePattern>();
        [SerializeField]
        private BallMovement ballMovement;
        private readonly EncounterPlanner planner = new EncounterPlanner();
        private readonly PrefabPool prefabPool = new PrefabPool();
        private readonly Dictionary<GameObject, ObstaclePair> cachedInstances = new Dictionary<GameObject, ObstaclePair>();
        public System.Collections.Generic.IReadOnlyList<PrefabPool.Counters> PoolStatistics => prefabPool.Statistics;

        private void Awake() => prefabPool.Discarding += instance => cachedInstances.Remove(instance);
        [Header("Pooling")]
        [SerializeField, Min(0)]
        private int maxInactivePerPrefab = 2;
        [Header("Pool diagnostics (runtime)")]
        [SerializeField]
        private List<PrefabPool.Counters> poolStatistics = new List<PrefabPool.Counters>();
        private sealed class ObstaclePair
        {
            public GameObject Prefab;
            public GameObject Instance;
            public Renderer[] Renderers;
            public bool[] RendererDefaults;
            public SideToSideObstacleMotion Motion;
            public Collider[] Colliders;
            public bool Visible = true;
        }

        private readonly Dictionary<TrackSegment, List<ObstaclePair>> active = new Dictionary<TrackSegment, List<ObstaclePair>>();
        private readonly HashSet<TrackSegment> activeGaps = new HashSet<TrackSegment>();
        private readonly Dictionary<TrackSegment, HashSet<long>> attachedEncounters = new Dictionary<TrackSegment, HashSet<long>>();
        // Nine-metre road, with a quarter-metre clearance at each edge.
        private const float FootprintLimitX = 4.25f;
        private readonly Dictionary<GameObject, Bounds> fixedFootprints = new Dictionary<GameObject, Bounds>();
        private GameObject[] runFixed = Array.Empty<GameObject>();
        private GameObject[] runMoving = Array.Empty<GameObject>();
        private GameObject[] runJump = Array.Empty<GameObject>();
        private int runSeed;
        private bool ready;
        private bool motionPaused = true;
        private int runVersion;
        private float runGapShare;
        internal bool GapTracksEnabled => ready && runGapShare > 0f && jumpFraction > 0f;

        // A local, immutable plan is consumed by the generator in this spawn call.
        // No pending plan survives a reset, and Attach never rolls a second choice.
        internal readonly struct Placement
        {
            internal Placement(GameObject prefab, Vector3 position, bool jump, float phase = 0f, float speed = 0f, int taskIndex = 0)
            {
                Prefab = prefab;
                Position = position;
                Jump = jump;
                Phase = phase;
                Speed = speed;
                TaskIndex = taskIndex;
            }

            internal GameObject Prefab { get; }
            internal Vector3 Position { get; }
            internal bool Jump { get; }
            internal float Phase { get; }
            internal float Speed { get; }
            internal int TaskIndex { get; }
        }

        internal readonly struct EncounterPlan
        {
            internal EncounterPlan(ObstacleManager owner, int version, int index, TrackSegment track, Placement[] placements, bool gap, float endZ, float exitX, float width, long encounterId = 0, string family = null, TaskWindow[] windows = null, int gapTaskIndex = 1)
            {
                Owner = owner;
                Version = version;
                Index = index;
                Track = track;
                Placements = placements;
                IsGap = gap;
                EndZ = endZ;
                ExitX = exitX;
                Width = width;
                EncounterId = encounterId;
                Family = family;
                Windows = windows;
                GapTaskIndex = gapTaskIndex;
            }

            internal ObstacleManager Owner { get; }
            internal int Version { get; }
            internal int Index { get; }
            internal TrackSegment Track { get; }
            internal Placement[] Placements { get; }
            internal bool IsGap { get; }
            internal float EndZ { get; }
            internal float ExitX { get; }
            internal float Width { get; }
            internal long EncounterId { get; }
            internal string Family { get; }
            internal TaskWindow[] Windows { get; }
            internal int GapTaskIndex { get; }

            internal TaskWindow Window(int localIndex)
            {
                if (Windows != null)
                    foreach (TaskWindow window in Windows)
                        if (window.LocalIndex == localIndex)
                            return window;
                return null;
            }

            internal bool HasEncounter => Track != null && (IsGap || (Placements != null && Placements.Length > 0));
        }

        public bool BeginRun(int trackSeed)
        {
            EndRun();
            if (!isActiveAndEnabled)
                return false;
            if (activeRoot == null || poolRoot == null || activeRoot == poolRoot || activeRoot.IsChildOf(poolRoot) || poolRoot.IsChildOf(activeRoot) || !activeRoot.gameObject.activeInHierarchy || activeRoot.lossyScale != Vector3.one)
            {
                return SetupWarning("Assign separate roots with unit world scale.");
            }

            if (fixedPositionPrefabs == null || fixedPositionPrefabs.Count == 0)
                return SetupWarning("Keep your five neutral fixed prefabs assigned.");
            if (!ValidateList(fixedPositionPrefabs, false) || !ValidateList(movingPrefabs, true) || !ValidateList(jumpableOnlyPrefabs, false))
            {
                return false;
            }

            poolRoot.gameObject.SetActive(false);
            runFixed = fixedPositionPrefabs.ToArray();
            runMoving = movingPrefabs.ToArray();
            runJump = jumpableOnlyPrefabs.ToArray();
            foreach (GameObject prefab in runFixed)
            {
                if (!TryMeasureFixedFootprint(prefab, out Bounds footprint) || footprint.size.x > FootprintLimitX * 2f || footprint.min.z < -6f || footprint.max.z > 6f)
                {
                    return SetupWarning($"{prefab.name}: authored footprint does not fit the road. Adjust placement/art manually; obstacles are never rescaled.");
                }

                fixedFootprints[prefab] = footprint;
            }

            runGapShare = Mathf.Clamp01(gapShareWithinJump);
            planner.Reset();
            runSeed = unchecked(trackSeed ^ 0x2468ACE);
            // RunPrepared will release this through SetGenerationPaused(false).
            motionPaused = true;
            ready = true;
            return true;
        }

        public void Attach(TrackSegment segment, int segmentIndex, bool blocked, float probability, float movingSpeed = 0f)
        {
            var difficulty = new DifficultySnapshot(DifficultyMode.Easy, 6f, 0f, probability, movingSpeed, 0f);
            AttachPlanned(segment, PlanEncounter(segment, null, segmentIndex, blocked, difficulty, segment.transform.position.z));
        }

        private static float GapTakeoffZ(TrackSegment gap) => GapLip(gap, "Floor_Takeoff", true);
        private static float GapLandingZ(TrackSegment gap) => GapLip(gap, "Floor_Landing", false);
        private static float GapLip(TrackSegment gap, string name, bool far)
        {
            foreach (BoxCollider box in gap.GetComponentsInChildren<BoxCollider>(true))
                if (box.name == name)
                    return gap.transform.InverseTransformPoint(box.transform.TransformPoint(box.center + new Vector3(0f, box.size.y * .5f, box.size.z * (far ? .5f : -.5f)))).z;
            return far ? 8f : 16f;
        }

        internal void AttachPlanned(TrackSegment segment, EncounterPlan plan)
        {
            if (!ready || segment == null || !plan.HasEncounter || plan.Owner != this || plan.Version != runVersion || segment.SegmentId != plan.Track.SegmentId)
                return;
            if (!attachedEncounters.TryGetValue(segment, out HashSet<long> identities))
                attachedEncounters.Add(segment, identities = new HashSet<long>());
            if (!identities.Add(plan.EncounterId))
                return;
            if (!active.TryGetValue(segment, out List<ObstaclePair> pairs))
            {
                pairs = new List<ObstaclePair>(plan.Placements.Length);
                active.Add(segment, pairs);
            }

            for (int i = 0; i < plan.Placements.Length; i++)
            {
                Placement placement = plan.Placements[i];
                GameObject instance = prefabPool.Acquire(placement.Prefab, poolRoot);
                if (poolStatistics.Count != prefabPool.Statistics.Count)
                {
                    poolStatistics.Clear();
                    poolStatistics.AddRange(prefabPool.Statistics);
                }

                if (!cachedInstances.TryGetValue(instance, out ObstaclePair pair))
                {
                    pair = new ObstaclePair
                    {
                        Prefab = placement.Prefab,
                        Instance = instance,
                        Renderers = instance.GetComponentsInChildren<Renderer>(true),
                        Colliders = instance.GetComponentsInChildren<Collider>(true),
                        Motion = instance.GetComponentInChildren<SideToSideObstacleMotion>(true)
                    };
                    pair.RendererDefaults = new bool[pair.Renderers.Length];
                    for (int r = 0; r < pair.Renderers.Length; r++)
                        pair.RendererDefaults[r] = pair.Renderers[r].enabled;
                    cachedInstances.Add(instance, pair);
                }

                instance.transform.SetParent(activeRoot, false);
                instance.transform.SetPositionAndRotation(segment.transform.TransformPoint(placement.Position), segment.transform.rotation);
                instance.transform.localScale = Vector3.one;
                if (pair.Motion != null)
                    pair.Motion.Prepare(placement.Speed, placement.Phase, plan.Width * .5f);
                ApplyVisibility(pair, segment.IsPresentationVisible);
                instance.SetActive(true);
                if (pair.Motion != null)
                    pair.Motion.SetRunning(!motionPaused);
                pairs.Add(pair);
                taskTracker?.RegisterPayload(segment, plan.Index, instance, placement.Jump, pair.Motion, placement.TaskIndex, pair.Colliders, plan.EncounterId, plan.Window(placement.TaskIndex));
            }

            if (plan.IsGap)
            {
                activeGaps.Add(segment);
                taskTracker?.RegisterGap(segment, plan.Index, plan.GapTaskIndex, plan.EncounterId, plan.Window(plan.GapTaskIndex));
            }
        }

        public void Release(TrackSegment segment)
        {
            if (segment != null)
                attachedEncounters.Remove(segment);
            activeGaps.Remove(segment);
            if (segment == null || !active.TryGetValue(segment, out List<ObstaclePair> pairs))
            {
                return;
            }

            active.Remove(segment);
            foreach (ObstaclePair pair in pairs)
                Recycle(pair);
        }

        public void SetMotionPaused(bool paused)
        {
            motionPaused = paused;
            foreach (List<ObstaclePair> pairs in active.Values)
                foreach (ObstaclePair pair in pairs)
                    if (pair.Motion != null)
                        pair.Motion.SetRunning(!paused);
        }

        public void EndRun()
        {
            ready = false;
            runVersion = unchecked(runVersion + 1);
            runGapShare = 0f;
            planner.Reset();
            motionPaused = true;
            foreach (List<ObstaclePair> pairs in active.Values)
                foreach (ObstaclePair pair in pairs)
                    Recycle(pair);
            active.Clear();
            attachedEncounters.Clear();
            activeGaps.Clear();
            fixedFootprints.Clear();
            runFixed = Array.Empty<GameObject>();
            runMoving = Array.Empty<GameObject>();
            runJump = Array.Empty<GameObject>();
        }

        private void LateUpdate()
        {
            foreach (var entry in active)
                foreach (ObstaclePair pair in entry.Value)
                    if (entry.Key != null && pair.Instance != null)
                        ApplyVisibility(pair, entry.Key.IsPresentationVisible);
        }

        private static void ApplyVisibility(ObstaclePair pair, bool visible)
        {
            if (pair.Visible == visible)
                return;
            pair.Visible = visible;
            for (int i = 0; i < pair.Renderers.Length; i++)
            {
                Renderer renderer = pair.Renderers[i];
                if (renderer == null)
                    continue;
                bool enabled = visible && pair.RendererDefaults[i];
                if (renderer.enabled != enabled)
                    renderer.enabled = enabled;
            }
        }

        private void Recycle(ObstaclePair pair)
        {
            if (pair.Motion != null)
                pair.Motion.SetRunning(false);
            if (pair.Instance != null)
            {
                pair.Instance.SetActive(false);
                ApplyVisibility(pair, true);
            }

            prefabPool.Return(pair.Prefab, pair.Instance, poolRoot, maxInactivePerPrefab);
        }

        private bool TryGetFootprint(GameObject prefab, out Bounds footprint)
        {
            if (fixedFootprints.TryGetValue(prefab, out footprint))
                return true;
            if (!TryMeasureFixedFootprint(prefab, out footprint))
                return false;
            fixedFootprints.Add(prefab, footprint);
            return true;
        }

        private static bool TryMeasureFixedFootprint(GameObject prefab, out Bounds footprint)
        {
            footprint = default;
            bool hasBounds = false;
            Transform root = prefab.transform;
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (!(collider is BoxCollider box))
                    return false; // This library deliberately uses authored boxes only.
                EncapsulateLocalBounds(root, box.transform, new Bounds(box.center, box.size), ref footprint, ref hasBounds);
            }

            return hasBounds && footprint.size.x > 0f;
        }

        private static void EncapsulateLocalBounds(Transform root, Transform owner, Bounds local, ref Bounds combined, ref bool hasBounds)
        {
            Matrix4x4 matrix = root.worldToLocalMatrix * owner.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = matrix.MultiplyPoint3x4(corner);
                if (hasBounds)
                    combined.Encapsulate(point);
                else
                {
                    combined = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
            }
        }

        public bool OwnsActiveCollider(Collider shape)
        {
            if (shape == null || !shape.enabled || shape.isTrigger || !shape.gameObject.activeInHierarchy)
                return false;
            foreach (List<ObstaclePair> pairs in active.Values)
                foreach (ObstaclePair pair in pairs)
                {
                    if (pair.Instance == null || !pair.Instance.activeInHierarchy)
                        continue;
                    Transform root = pair.Instance.transform;
                    if (shape.transform == root || shape.transform.IsChildOf(root))
                        return true;
                }

            return false;
        }

        private bool ValidateList(List<GameObject> prefabs, bool moving)
        {
            if (prefabs == null)
                return SetupWarning("An obstacle list is null.");
            var unique = new HashSet<GameObject>();
            foreach (GameObject prefab in prefabs)
            {
                if (prefab == null || !unique.Add(prefab))
                    return SetupWarning("Remove missing/duplicate prefab entries.");
                Collider[] shapes = prefab.GetComponentsInChildren<Collider>(true);
                Rigidbody[] bodies = prefab.GetComponentsInChildren<Rigidbody>(true);
                MonoBehaviour[] scripts = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                SideToSideObstacleMotion[] motions = prefab.GetComponentsInChildren<SideToSideObstacleMotion>(true);
                if (shapes.Length == 0 || prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                    return SetupWarning($"{prefab.name} needs collision and visuals.");
                if (moving)
                {
                    if (bodies.Length != 1 || motions.Length != 1 || scripts.Length != 1 || !motions[0].enabled || bodies[0].transform != motions[0].transform || !bodies[0].isKinematic || bodies[0].useGravity)
                    {
                        return SetupWarning($"{prefab.name}: invalid moving setup.");
                    }
                }
                else if (bodies.Length != 0 || scripts.Length != 0)
                    return SetupWarning($"{prefab.name} is not a static prefab.");
                foreach (Collider shape in shapes)
                {
                    if (!shape.enabled || shape.isTrigger)
                        return SetupWarning($"{prefab.name}: invalid collider.");
                    if (moving && shape.transform != bodies[0].transform && !shape.transform.IsChildOf(bodies[0].transform))
                    {
                        return SetupWarning($"{prefab.name}: collider is outside Mover.");
                    }
                }
            }

            return true;
        }

        private bool SetupWarning(string message)
        {
            Debug.LogWarning($"Obstacle initialization skipped: {message}", this);
            return false;
        }

        private void OnDisable() => EndRun();
    }
}
