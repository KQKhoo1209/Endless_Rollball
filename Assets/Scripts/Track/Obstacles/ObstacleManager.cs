using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class ObstacleManager : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private Transform activeRoot;
        [SerializeField] private Transform poolRoot;
        [SerializeField] private SessionTaskTracker taskTracker;

        [Header("Neutral fixed obstacles")]
        [SerializeField]
        private List<GameObject> fixedPositionPrefabs =
            new List<GameObject>();

        [Header("Neutral moving obstacles")]
        [SerializeField]
        private List<GameObject> movingPrefabs =
            new List<GameObject>();
        [SerializeField, Range(0f, 1f)] private float movingFraction = 0.35f;

        [Header("Jump-only obstacles")]
        [SerializeField]
        private List<GameObject> jumpableOnlyPrefabs = new List<GameObject>();
        [SerializeField, Range(0f, 1f)] private float jumpFraction = 0f;

        [Tooltip("Share of selected jump encounters that use a themed physical gap instead of a jump row. Applied at run start.")]
        [SerializeField, Range(0f, 1f)] private float gapShareWithinJump = 0f;

        [Header("Encounter frequency")]
        [Tooltip("Multiplies the difficulty encounter chance, capped at 100%. Clear recovery sections are still enforced. Applied at run start.")]
        [SerializeField, Range(0.1f, 3f)] private float encounterProbabilityMultiplier = 1f;

        [Header("Pooling")]
        [SerializeField, Min(0)] private int maxInactivePerPrefab = 2;

        private sealed class ObstaclePair
        {
            public GameObject Prefab;
            public GameObject Instance;
            public Renderer[] Renderers;
            public bool[] RendererDefaults;
            public SideToSideObstacleMotion Motion;
        }

        private readonly Dictionary<TrackSegment, ObstaclePair> active =
            new Dictionary<TrackSegment, ObstaclePair>();
        private readonly Dictionary<GameObject, Queue<GameObject>> pools =
            new Dictionary<GameObject, Queue<GameObject>>();
        private readonly HashSet<TrackSegment> activeGaps = new HashSet<TrackSegment>();
        // Nine-metre road, with a quarter-metre clearance at each edge.
        private const float FootprintLimitX = 4.25f;
        private readonly Dictionary<GameObject, Bounds> fixedFootprints =
            new Dictionary<GameObject, Bounds>();

        private GameObject[] runFixed = Array.Empty<GameObject>();
        private GameObject[] runMoving = Array.Empty<GameObject>();
        private GameObject[] runJump = Array.Empty<GameObject>();
        private int runSeed;
        private int lastPlacedIndex;
        private bool ready;
        private bool motionPaused = true;
        private int runVersion;
        private float runGapShare;
        private float runProbabilityMultiplier = 1f;

        internal bool GapTracksEnabled => ready && runGapShare > 0f && jumpFraction > 0f;

        // A local, immutable plan is consumed by the generator in this spawn call.
        // No pending plan survives a reset, and Attach never rolls a second choice.
        internal readonly struct EncounterPlan
        {
            internal EncounterPlan(ObstacleManager owner, int version, int index,
                TrackSegment track, GameObject payload, float offsetX, float phase, float movingSpeed, bool isGap,
                bool isJumpRow = false)
            {
                Owner = owner;
                Version = version;
                Index = index;
                Track = track;
                Payload = payload;
                OffsetX = offsetX;
                Phase = phase;
                MovingSpeed = movingSpeed;
                IsGap = isGap;
                IsJumpRow = isJumpRow;
            }

            internal ObstacleManager Owner { get; }
            internal int Version { get; }
            internal int Index { get; }
            internal TrackSegment Track { get; }
            internal GameObject Payload { get; }
            internal float OffsetX { get; }
            internal float Phase { get; }
            internal float MovingSpeed { get; }
            internal bool IsGap { get; }
            internal bool IsJumpRow { get; }
            internal bool HasEncounter => IsGap || Payload != null;
        }

        public bool BeginRun(int trackSeed)
        {
            EndRun();
            if (!isActiveAndEnabled)
                return false;

            if (activeRoot == null || poolRoot == null ||
                activeRoot == poolRoot || activeRoot.IsChildOf(poolRoot) ||
                poolRoot.IsChildOf(activeRoot) ||
                !activeRoot.gameObject.activeInHierarchy ||
                activeRoot.lossyScale != Vector3.one)
            {
                return SetupWarning("Assign separate roots with unit world scale.");
            }

            if (fixedPositionPrefabs == null || fixedPositionPrefabs.Count == 0)
                return SetupWarning("Keep your five neutral fixed prefabs assigned.");

            if (!ValidateList(fixedPositionPrefabs, false) ||
                !ValidateList(movingPrefabs, true) ||
                !ValidateList(jumpableOnlyPrefabs, false))
            {
                return false;
            }

            poolRoot.gameObject.SetActive(false);
            runFixed = fixedPositionPrefabs.ToArray();
            runMoving = movingPrefabs.ToArray();
            runJump = jumpableOnlyPrefabs.ToArray();
            foreach (GameObject prefab in runFixed)
            {
                if (!TryMeasureFixedFootprint(prefab, out Bounds footprint) ||
                    footprint.size.x > FootprintLimitX * 2f ||
                    footprint.min.z < -6f || footprint.max.z > 6f)
                {
                    return SetupWarning($"{prefab.name}: authored footprint does not fit the road. Adjust placement/art manually; obstacles are never rescaled.");
                }
                fixedFootprints.Add(prefab, footprint);
            }
            runGapShare = Mathf.Clamp01(gapShareWithinJump);
            runProbabilityMultiplier = Mathf.Clamp(encounterProbabilityMultiplier, .1f, 3f);
            runSeed = unchecked(trackSeed ^ 0x2468ACE);
            lastPlacedIndex = -2;
            // RunPrepared will release this through SetGenerationPaused(false).
            motionPaused = true;
            ready = true;
            return true;
        }

        public void Attach(
            TrackSegment segment, int segmentIndex, bool blocked,
            float probability, float movingSpeed = 0f)
        {
            // Legacy payload-only entry point: it cannot replace an acquired floor.
            AttachPlanned(segment, PlanEncounter(segment, null, segmentIndex, blocked, probability, movingSpeed));
        }

        internal EncounterPlan PlanEncounter(
            TrackSegment clearRoad, TrackSegment gapRoad, int segmentIndex,
            bool blocked, float probability, float movingSpeed)
        {
            if (!ready || !isActiveAndEnabled || clearRoad == null ||
                segmentIndex < 0 || blocked)
            {
                return default;
            }

            if (clearRoad.Kind != TrackSegmentKind.Empty || clearRoad.RequiredJump ||
                clearRoad.EntryLanes != TrackLaneMask.All || clearRoad.SafeLanes != TrackLaneMask.All ||
                segmentIndex <= lastPlacedIndex + 1)
            {
                return default;
            }

            var random = new System.Random(
                unchecked(runSeed ^ (segmentIndex * 73856093)));
            if (random.NextDouble() >= Mathf.Clamp01(probability * runProbabilityMultiplier))
                return default;

            bool canUseGap = GapTracksEnabled && gapRoad != null;
            bool jumping = (runJump.Length > 0 || canUseGap) && jumpFraction > 0f &&
                random.NextDouble() < Mathf.Clamp01(jumpFraction);

            if (jumping && canUseGap && (runJump.Length == 0 || random.NextDouble() < runGapShare))
            {
                return new EncounterPlan(this, runVersion, segmentIndex, gapRoad, null, 0f, 0f, 0f, true);
            }

            bool moving = !jumping && runMoving.Length > 0 && movingSpeed > 0f &&
                random.NextDouble() < Mathf.Clamp01(movingFraction);

            GameObject[] choices = jumping ? runJump : (moving ? runMoving : runFixed);
            GameObject prefab = choices[random.Next(choices.Length)];
            float offsetX = 0f;
            if (!jumping && !moving)
            {
                // Keep the same left/right random draw; move wide assets inward,
                // without squeezing their mesh or their authored colliders.
                float desiredCentreX = random.Next(2) == 0 ? -2.25f : 2.25f;
                Bounds footprint = fixedFootprints[prefab];
                offsetX = Mathf.Clamp(desiredCentreX - footprint.center.x,
                    -FootprintLimitX - footprint.min.x,
                    FootprintLimitX - footprint.max.x);
            }
            float phase = moving ? (random.Next(2) == 0 ? 0.25f : 0.75f) : 0f;
            return new EncounterPlan(this, runVersion, segmentIndex, clearRoad, prefab, offsetX, phase, movingSpeed, false, jumping);
        }

        internal void AttachPlanned(TrackSegment segment, EncounterPlan plan)
        {
            if (!ready || !isActiveAndEnabled || segment == null || !plan.HasEncounter ||
                plan.Owner != this || plan.Version != runVersion || plan.Track == null ||
                segment.SegmentId != plan.Track.SegmentId || plan.Index <= lastPlacedIndex + 1 ||
                active.ContainsKey(segment) || activeGaps.Contains(segment))
                return;

            if (plan.IsGap)
            {
                if (segment.Kind != TrackSegmentKind.Gap || !segment.RequiredJump)
                    return;
                activeGaps.Add(segment);
                taskTracker?.RegisterGap(segment, plan.Index);
                lastPlacedIndex = plan.Index;
                return; // Collision and pooling belong to this complete track, not a payload.
            }
            if (segment.Kind != TrackSegmentKind.Empty || segment.RequiredJump)
                return;

            GameObject instance = Acquire(plan.Payload);
            instance.SetActive(false);
            instance.transform.SetParent(activeRoot, false);
            instance.transform.SetPositionAndRotation(
                segment.transform.TransformPoint(new Vector3(plan.OffsetX, 0f, 12f)),
                segment.transform.rotation);
            instance.transform.localScale = Vector3.one;

            var pair = new ObstaclePair
            {
                Prefab = plan.Payload,
                Instance = instance,
                Renderers = instance.GetComponentsInChildren<Renderer>(true),
                Motion = instance.GetComponentInChildren<SideToSideObstacleMotion>(true)
            };
            pair.RendererDefaults = new bool[pair.Renderers.Length];
            for (int i = 0; i < pair.Renderers.Length; i++)
                pair.RendererDefaults[i] = pair.Renderers[i].enabled;

            if (pair.Motion != null)
            {
                pair.Motion.Prepare(plan.MovingSpeed, plan.Phase);
            }

            ApplyVisibility(pair, segment.IsPresentationVisible);
            active.Add(segment, pair);
            instance.SetActive(true);
            if (pair.Motion != null)
                pair.Motion.SetRunning(!motionPaused);
            taskTracker?.RegisterPayload(segment, plan.Index, instance, plan.IsJumpRow, pair.Motion);
            lastPlacedIndex = plan.Index;
        }

        public void Release(TrackSegment segment)
        {
            activeGaps.Remove(segment);
            if (segment == null ||
                !active.TryGetValue(segment, out ObstaclePair pair))
            {
                return;
            }
            active.Remove(segment);
            Recycle(pair);
        }

        public void SetMotionPaused(bool paused)
        {
            motionPaused = paused;
            foreach (ObstaclePair pair in active.Values)
                if (pair.Motion != null)
                    pair.Motion.SetRunning(!paused);
        }

        public void EndRun()
        {
            ready = false;
            runVersion = unchecked(runVersion + 1);
            runGapShare = 0f;
            runProbabilityMultiplier = 1f;
            lastPlacedIndex = -2;
            motionPaused = true;
            foreach (ObstaclePair pair in active.Values)
                Recycle(pair);
            active.Clear();
            activeGaps.Clear();
            fixedFootprints.Clear();
            runFixed = Array.Empty<GameObject>();
            runMoving = Array.Empty<GameObject>();
            runJump = Array.Empty<GameObject>();
        }

        private void LateUpdate()
        {
            foreach (var entry in active)
            {
                TrackSegment track = entry.Key;
                ObstaclePair pair = entry.Value;
                if (track == null || pair.Instance == null)
                    continue;

                bool visible = track.IsPresentationVisible;
                ApplyVisibility(pair, visible);

                if (pair.Motion != null)
                {
                    pair.Motion.SetRunning(!motionPaused);
                }
            }
        }

        private static void ApplyVisibility(ObstaclePair pair, bool visible)
        {
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

        private GameObject Acquire(GameObject prefab)
        {
            if (pools.TryGetValue(prefab, out Queue<GameObject> pool))
            {
                while (pool.Count > 0)
                {
                    GameObject instance = pool.Dequeue();
                    if (instance != null)
                        return instance;
                }
            }
            GameObject created = Instantiate(prefab, poolRoot, false);
            created.name = $"{prefab.name}_Pooled";
            created.SetActive(false);
            return created;
        }

        private void Recycle(ObstaclePair pair)
        {
            if (pair.Instance == null)
                return;
            if (pair.Motion != null)
                pair.Motion.SetRunning(false);
            pair.Instance.SetActive(false);
            // Restore authored states before this instance is reused.
            ApplyVisibility(pair, true);

            if (poolRoot == null || pair.Prefab == null)
            {
                Destroy(pair.Instance);
                return;
            }
            pair.Instance.transform.SetParent(poolRoot, false);
            if (!pools.TryGetValue(pair.Prefab, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                pools.Add(pair.Prefab, pool);
            }
            if (pool.Count < maxInactivePerPrefab)
                pool.Enqueue(pair.Instance);
            else
                Destroy(pair.Instance);
        }

        private static bool TryMeasureFixedFootprint(GameObject prefab, out Bounds footprint)
        {
            footprint = default;
            bool hasBounds = false;
            Transform root = prefab.transform;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    return false;
                EncapsulateLocalBounds(root, filter.transform, filter.sharedMesh.bounds,
                    ref footprint, ref hasBounds);
            }
            foreach (SkinnedMeshRenderer renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                EncapsulateLocalBounds(root, renderer.transform, renderer.localBounds,
                    ref footprint, ref hasBounds);
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true))
            {
                if (!(collider is BoxCollider box))
                    return false; // This library deliberately uses authored boxes only.
                EncapsulateLocalBounds(root, box.transform, new Bounds(box.center, box.size),
                    ref footprint, ref hasBounds);
            }
            return hasBounds && footprint.size.x > 0f;
        }

        private static void EncapsulateLocalBounds(Transform root, Transform owner, Bounds local,
            ref Bounds combined, ref bool hasBounds)
        {
            Matrix4x4 matrix = root.worldToLocalMatrix * owner.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                Vector3 point = matrix.MultiplyPoint3x4(corner);
                if (hasBounds) combined.Encapsulate(point);
                else { combined = new Bounds(point, Vector3.zero); hasBounds = true; }
            }
        }

        public bool OwnsActiveCollider(Collider shape)
        {
            if (shape == null || !shape.enabled || shape.isTrigger ||
                !shape.gameObject.activeInHierarchy)
                return false;

            foreach (ObstaclePair pair in active.Values)
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
                SideToSideObstacleMotion[] motions =
                    prefab.GetComponentsInChildren<SideToSideObstacleMotion>(true);

                if (shapes.Length == 0 ||
                    prefab.GetComponentsInChildren<Renderer>(true).Length == 0)
                    return SetupWarning($"{prefab.name} needs collision and visuals.");

                if (moving)
                {
                    if (bodies.Length != 1 || motions.Length != 1 ||
                        scripts.Length != 1 || !motions[0].enabled ||
                        bodies[0].transform != motions[0].transform ||
                        !bodies[0].isKinematic || bodies[0].useGravity)
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
                    if (moving && shape.transform != bodies[0].transform &&
                        !shape.transform.IsChildOf(bodies[0].transform))
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
