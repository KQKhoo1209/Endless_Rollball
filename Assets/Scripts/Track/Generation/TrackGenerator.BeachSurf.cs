using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator
    {
        [Header("Beach surfing")]
        [SerializeField] private TrackSegment beachSurfSeaPrefab;

        private bool beachSurfReserved;
        private int beachSurfFirstIndex;
        private int beachSurfExitIndex;
        private float beachSurfEntryZ;
        private float beachSurfExitZ;

        internal string SurfReservationIssue { get; private set; }

        internal bool TryReserveBeachSurf(
            float desiredEntryZ,
            float surfSpeed,
            float duration,
            out float entryZ,
            out float exitZ)
        {
            entryZ = 0f;
            exitZ = 0f;
            SurfReservationIssue = "Check sea prefab/anchors, player, and prepared track references.";

            if (beachSurfReserved ||
                !IsRunPrepared ||
                environmentManager == null ||
                beachSurfSeaPrefab == null ||
                surfSpeed <= 0f ||
                duration <= 0f)
                return false;

            if (!beachSurfSeaPrefab.IsContractValid(out _))
                return false;

            // Leave room beyond the existing generation frontier
            // and the ordinary three-segment obstacle lookahead.
            int offset = Mathf.Max(3, Mathf.RoundToInt((desiredEntryZ-nextSpawnZ)/TrackSegment.ContractLength));
            var sequence = FindFirstObjectByType<GameSequenceCoordinator>();
            if(sequence!=null && sequence.Eligible) {
                sequence.Window(out float earliest,out float target,out float latest);
                var whale=FindFirstObjectByType<WhaleSurfController>();
                float boarding=whale!=null?whale.BoardingSeconds:.9f;
                float takeoff=whale!=null?whale.BoardingTakeoffDistance:8f;
                if(!PhoenixLavaMath.TryEntry(nextSpawnZ,spawnedCount,player.position.z,
                    sequence.Elapsed,surfSpeed,Mathf.Max(0,earliest-boarding),target-boarding,
                    latest-boarding,takeoff,out int candidateIndex,out _)) {
                    SurfReservationIssue="No ungenerated 24 m entry fits the time window; check generation lookahead and entry timing.";
                    return false;
                }
                offset=candidateIndex-spawnedCount;
            }
            int firstIndex = spawnedCount + offset;

            int seaCount = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    surfSpeed * duration /
                    TrackSegment.ContractLength));

            int exitIndex = firstIndex + seaCount;

            float firstZ =
                nextSpawnZ +
                offset * TrackSegment.ContractLength;

            float returnZ =
                firstZ +
                seaCount * TrackSegment.ContractLength;

            // Preserve space for the exit bridge and recovery.
            if (hasFiniteRoute &&
                returnZ + 2f * TrackSegment.ContractLength >=
                finishLineZ - finishClearance)
                return false;

            // Include the boarding bridge and two exit bridges.
            for (int index = firstIndex - 1;
                 index <= exitIndex + 1;
                 index++)
            {
                if (!environmentManager.IsBeachRoadSegment(index) ||
                    environmentManager.RequiresClearTrack(index)) {
                    SurfReservationIssue=$"Surf corridor crosses a theme boundary or protected road at segment {index}. Check Beach zone length.";
                    return false;
                }
            }

            beachSurfFirstIndex = firstIndex;
            beachSurfExitIndex = exitIndex;
            beachSurfEntryZ = firstZ;
            beachSurfExitZ = returnZ;
            beachSurfReserved = true;
            SurfReservationIssue=null;

            entryZ = firstZ;
            exitZ = returnZ;

            return true;
        }

        private bool IsBeachSurfSea(int index)
        {
            return beachSurfReserved &&
                   index >= beachSurfFirstIndex &&
                   index < beachSurfExitIndex;
        }

        private bool IsBeachSurfReserved(int index)
        {
            return beachSurfReserved &&
                   index >= beachSurfFirstIndex - 1 &&
                   index <= beachSurfExitIndex + 1;
        }

        private bool beachSurfRunning;

        internal bool IsBeachSurfRunning => beachSurfRunning;

        internal void SetBeachSurfRunning(bool running)
        {
            beachSurfRunning = running;
            environmentManager?.SetBeachSurfPresentation(running);
        }

        internal bool TryGetBeachSurfExitFloor(out float floorHeight)
        {
            floorHeight = 0f;

            if (!beachSurfReserved)
                return false;

            foreach (ActiveSegment entry in activeSegments)
            {
                if (entry.Index != beachSurfExitIndex)
                    continue;

                TrackSegment road = entry.Instance;

                if (road == null || !road.IsPresentationVisible)
                    return false;

                Transform floorTransform =
                    road.transform.Find("FloorCollider");

                if (floorTransform == null ||
                    !floorTransform.TryGetComponent(out Collider floor) ||
                    !floor.enabled ||
                    !floor.gameObject.activeInHierarchy ||
                    floor.isTrigger)
                    return false;

                floorHeight = floor.bounds.max.y;
                return true;
            }

            return false;
        }

        private void ResetBeachSurfReservation()
        {
            environmentManager?.SetBeachSurfPresentation(false);
            beachSurfReserved = false;
            beachSurfRunning = false;
            beachSurfFirstIndex = -1;
            beachSurfExitIndex = -1;
            beachSurfEntryZ = 0f;
            beachSurfExitZ = 0f;
        }
    }
}
