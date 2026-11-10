using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator
    {
        [Header("Phoenix lava flight")]
        [SerializeField] private TrackSegment phoenixLavaPrefab;
        [Tooltip("Lava surface height relative to the Dungeon road floor.")]
        [SerializeField] private float lavaSurfaceHeight = -.6f;
        internal float LavaSurfaceHeight => lavaSurfaceHeight;

        private void ConfigureLavaSurface(TrackSegment segment)
        {
            Transform visual = segment.transform.Find("RoadVisual/LavaVisual");
            if (visual == null) return;
            Vector3 position = visual.localPosition;
            position.y = lavaSurfaceHeight;
            visual.localPosition = position;
        }
        private bool phoenixReserved, phoenixCommitted, phoenixRunning;
        private int phoenixFirstIndex, phoenixExitIndex;
        private TrackSegment phoenixFallbackRoad;
        internal bool IsCarrierEventRunning => beachSurfRunning || phoenixRunning;
        private bool IsCarrierReserved(int index) => IsBeachSurfReserved(index) ||
            (phoenixReserved && index >= phoenixFirstIndex - 1 && index <= phoenixExitIndex + 1);
        private bool IsPhoenixLava(int index) => phoenixReserved &&
            index >= phoenixFirstIndex && index < phoenixExitIndex;
        private bool IsCarrierInterior(int index) => IsBeachSurfSea(index) || IsPhoenixLava(index);

        internal bool TryReservePhoenixLava(float elapsed, float speed, float earliest,
            float target, float latest, float takeoffDistance, float duration,
            out float entryZ, out float exitZ)
        {
            entryZ = exitZ = 0f;
            if (!GameSequenceCoordinator.IsEventUnlocked(EndlessRollball.Scenery.EnvironmentTheme.Dungeon))
                return false;
            if (phoenixReserved || !IsRunPrepared || environmentManager == null ||
                phoenixLavaPrefab == null || !phoenixLavaPrefab.IsContractValid(out _) ||
                player == null || duration <= 0f) return false;
            if (!EndlessRollball.PhoenixLavaMath.TryEntry(nextSpawnZ, spawnedCount,
                player.position.z, elapsed, speed, earliest, target, latest, takeoffDistance,
                out int firstIndex, out float firstZ)) return false;
            int count = Mathf.Max(1, Mathf.CeilToInt(speed * duration / TrackSegment.ContractLength));
            int exitIndex = firstIndex + count;
            float returnZ = firstZ + count * TrackSegment.ContractLength;
            if (hasFiniteRoute && returnZ + 48f >= finishLineZ - finishClearance) return false;
            for (int i = firstIndex - 1; i <= exitIndex + 1; i++)
                if (!environmentManager.IsDungeonRoadSegment(i) || environmentManager.RequiresClearTrack(i))
                    return false;
            if (!environmentManager.TryGetPlayableRoadTrack(firstIndex, out phoenixFallbackRoad))
                return false;

            phoenixFirstIndex = firstIndex;
            phoenixExitIndex = exitIndex;
            phoenixReserved = true;
            entryZ = firstZ;
            exitZ = returnZ;
            return true;
        }

        internal bool CommitPhoenixLava()
        {
            if (!phoenixReserved || phoenixCommitted || !IsRunPrepared || phoenixLavaPrefab == null)
                return false;
            phoenixCommitted = true;
            SetPhoenixRunning(true);
            return true;
        }

        internal void SetPhoenixRunning(bool running)
        {
            phoenixRunning = running;
        }

        internal void CancelPhoenixReservation()
        {
            if (!phoenixReserved || phoenixCommitted) return;

            // The lava was streamed in advance. Restore a supported road before
            // the sphere can reach an entry whose boarding was cancelled.
            phoenixReserved = false;
            int count = activeSegments.Count;
            for (int i = 0; i < count; i++)
            {
                ActiveSegment entry = activeSegments.Dequeue();
                if (entry.Index < phoenixFirstIndex || entry.Index >= phoenixExitIndex ||
                    entry.Prefab != phoenixLavaPrefab)
                {
                    activeSegments.Enqueue(entry);
                    continue;
                }

                float entryZ = entry.Instance.EntryAnchor.position.z;
                Return(entry);
                TrackSegment road = Acquire(phoenixFallbackRoad);
                float rootZ = entryZ - phoenixFallbackRoad.EntryLocal.z;
                road.transform.SetPositionAndRotation(new Vector3(0f, 0f, rootZ), Quaternion.identity);
                road.Activate(difficultyManager.CurrentSnapshot);
                ConfigureRoadMarkers(road);
                environmentManager.Attach(road, entry.Index);
                taskTracker?.RegisterTrack(road);
                activeSegments.Enqueue(new ActiveSegment(phoenixFallbackRoad, road, entry.EndZ, entry.Index));
                if (entry.Index < generatedSequence.Count)
                    generatedSequence[entry.Index] = phoenixFallbackRoad.SegmentId;
            }
            phoenixFallbackRoad = null;
        }

        internal bool TryGetPhoenixExitFloor(out float height)
        {
            height = 0f;
            if (!phoenixReserved) return false;
            foreach (ActiveSegment entry in activeSegments)
            {
                if (entry.Index != phoenixExitIndex || entry.Instance == null) continue;
                Transform floorObject = entry.Instance.transform.Find("FloorCollider");
                if (floorObject == null || !floorObject.TryGetComponent(out Collider floor) ||
                    !floor.enabled || floor.isTrigger || !floor.gameObject.activeInHierarchy) return false;
                height = floor.bounds.max.y;
                return true;
            }
            return false;
        }

        private void ResetPhoenixLava()
        {
            SetPhoenixRunning(false);
            phoenixReserved = phoenixCommitted = false;
            phoenixFirstIndex = phoenixExitIndex = -1;
            phoenixFallbackRoad = null;
        }
    }
}
