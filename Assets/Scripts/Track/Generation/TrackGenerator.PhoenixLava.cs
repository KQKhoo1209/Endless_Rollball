using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator
    {
        [Header("Phoenix lava flight")]
        [SerializeField] private TrackSegment phoenixLavaPrefab;
        private bool phoenixReserved, phoenixCommitted, phoenixRunning;
        private int phoenixFirstIndex, phoenixExitIndex;
        internal bool IsCarrierEventRunning => beachSurfRunning || phoenixRunning;
        private bool IsCarrierReserved(int index) => IsBeachSurfReserved(index) ||
            (phoenixReserved && index >= phoenixFirstIndex - 1 && index <= phoenixExitIndex + 1);
        private bool IsPhoenixLava(int index) => phoenixCommitted &&
            index >= phoenixFirstIndex && index < phoenixExitIndex;
        private bool IsCarrierInterior(int index) => IsBeachSurfSea(index) || IsPhoenixLava(index);

        internal bool TryReservePhoenixLava(float elapsed, float speed, float earliest,
            float target, float latest, float takeoffDistance, float duration,
            out float entryZ, out float exitZ)
        {
            entryZ = exitZ = 0f;
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
            // Until boarding succeeds these sections remain supported ordinary roads.
            // Replace only the active interior, retaining queue order and pool ownership.
            int count = activeSegments.Count;
            for (int i = 0; i < count; i++)
            {
                ActiveSegment entry = activeSegments.Dequeue();
                if (!IsPhoenixLava(entry.Index)) { activeSegments.Enqueue(entry); continue; }
                float rootZ = entry.Instance.transform.position.z;
                Return(entry);
                TrackSegment lava = Acquire(phoenixLavaPrefab);
                lava.transform.SetPositionAndRotation(new Vector3(0f, 0f, rootZ), Quaternion.identity);
                lava.Activate(difficultyManager.CurrentSnapshot);
                environmentManager.Attach(lava, entry.Index);
                taskTracker?.RegisterTrack(lava);
                activeSegments.Enqueue(new ActiveSegment(phoenixLavaPrefab, lava, entry.EndZ, entry.Index));
                if (entry.Index < generatedSequence.Count)
                    generatedSequence[entry.Index] = phoenixLavaPrefab.SegmentId;
            }
            SetPhoenixRunning(true);
            return true;
        }

        internal void SetPhoenixRunning(bool running)
        {
            phoenixRunning = running;
        }

        internal void CancelPhoenixReservation()
        {
            if (phoenixCommitted) return;
            phoenixReserved = false;
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
        }
    }
}
