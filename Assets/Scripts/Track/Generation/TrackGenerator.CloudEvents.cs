using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator
    {
        internal bool TryFindCloudGateway(
            float playerZ, SessionTaskTracker tasks,
            out Vector3 position, out int zone)
        {
            position = default;
            zone = -1;

            if (!IsRunPrepared || environmentManager == null)
            {
                EndlessRollball.Infrastructure.RuntimeLog.Info("[CloudRoad] Run or environment is not ready.");
                return false;
            }

            if (!HasVisibleCityRoad(playerZ - 1f, playerZ + 2f))
            {
                EndlessRollball.Infrastructure.RuntimeLog.Info("[CloudRoad] Player is not on continuous visible Street road.");
                return false;
            }

            var sequence=FindFirstObjectByType<GameSequenceCoordinator>();
            int nearby = 0;
            int boundaryBlocked = 0;
            int roadBlocked = 0;
            int hazardBlocked = 0;

            foreach (ActiveSegment entry in activeSegments)
            {
                TrackSegment road = entry.Instance;
                if (road == null ||
                    road.EntryAnchor == null || road.ExitAnchor == null)
                    continue;

                float z = (road.EntryAnchor.position.z +
                           road.ExitAnchor.position.z) * .5f;

                if (z < playerZ + 25f || z > playerZ + 80f)
                    continue;

                if(sequence!=null && sequence.Eligible) {
                    sequence.Window(out float earliest,out _,out float latest);
                    float predicted=sequence.Elapsed+(z-playerZ)/Mathf.Max(.1f,difficultyManager.CurrentSnapshot.ForwardSpeed);
                    if(predicted<earliest || predicted>latest)continue;
                }
                nearby++;

                if ((hasFiniteRoute && z + 40f >= finishLineZ) ||
                    environmentManager.IsTransitionSegment(entry.Index) ||
                    environmentManager.RequiresClearTrack(entry.Index))
                {
                    boundaryBlocked++;
                    continue;
                }

                if (!HasVisibleCityRoad(z - 10f, z + 15f))
                {
                    roadBlocked++;
                    continue;
                }

                if (!tasks.IsSpecialEventAreaClear(z - 12f, z + 18f))
                {
                    hazardBlocked++;
                    continue;
                }

                position = new Vector3(-2f, road.EntryAnchor.position.y, z);
                zone = environmentManager.EncounterZoneAt(entry.Index);
                return true;
            }

            EndlessRollball.Infrastructure.RuntimeLog.Info(
                $"[CloudRoad] nearby={nearby}, boundaryBlocked={boundaryBlocked}, " +
                $"roadBlocked={roadBlocked}, hazardBlocked={hazardBlocked}");

            return false;
        }

        private bool IsCloudEntryBuffer(int index)
        {
            if (environmentManager == null || index < 0)
                return false;

            var sequence=FindFirstObjectByType<GameSequenceCoordinator>();
            if(sequence!=null && sequence.Eligible && player!=null) {
                if(!sequence.Available)return false;
                sequence.Window(out _,out float target,out _);
                float desired=player.position.z+Mathf.Max(.1f,difficultyManager.CurrentSnapshot.ForwardSpeed)*(target-sequence.Elapsed);
                float segmentZ=startZ+index*TrackSegment.ContractLength;
                return environmentManager.IsStreetRoadSegment(index) && Mathf.Abs(segmentZ-desired)<48f;
            }
            return false;
        }

        private readonly RaycastHit[] cloudDropHits = new RaycastHit[24];

        internal bool IsCloudStreetFloor(Collider support)
        {
            if (support == null ||
                !support.enabled ||
                !support.gameObject.activeInHierarchy ||
                support.isTrigger ||
                support.name != "FloorCollider")
                return false;

            TrackSegment road = support.GetComponentInParent<TrackSegment>();
            if (road == null) return false;

            foreach (ActiveSegment entry in activeSegments)
            {
                if (entry.Instance != road) continue;

                return road.IsPresentationVisible &&
                       !road.RequiredJump &&
                       road.Kind == TrackSegmentKind.Empty &&
                       environmentManager != null &&
                       environmentManager.IsStreetRoadSegment(entry.Index);
            }

            return false;
        }

        internal bool TryFindCloudDropFloor(
            Vector3 position, out Collider floor)
        {
            floor = null;

            if (!HasVisibleCityRoad(position.z - 1f, position.z + 8f))
                return false;

            if (taskTracker != null &&
                !taskTracker.IsSpecialEventAreaClear(
                    position.z - 2f, position.z + 10f))
                return false;

            int count = Physics.RaycastNonAlloc(
                position,
                Vector3.down,
                cloudDropHits,
                500f,
                ~0,
                QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = cloudDropHits[i];

                if (!IsCloudStreetFloor(hit.collider) ||
                    hit.distance >= nearest)
                    continue;

                nearest = hit.distance;
                floor = hit.collider;
            }

            return floor != null;
        }
    }
}