using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator
    {
        private readonly System.Collections.Generic.HashSet<TrackSegment> cloudContinuationRoads =
            new System.Collections.Generic.HashSet<TrackSegment>();

        private void PopulateRoadAfterCloud()
        {
            if(obstacleManager==null || environmentManager==null || taskTracker==null || player==null)
            { cloudContinuationRoads.Clear(); return; }
            float clearThrough=Mathf.Max(player.position.z,protectedArrivalEnd);
            obstacleManager.ResumeSharedRoadAfterCloud(clearThrough);
            int populated=0;
            foreach(var entry in activeSegments)
            {
                TrackSegment road=entry.Instance;
                if(!cloudContinuationRoads.Contains(road) || !road.IsPresentationVisible ||
                    road.RequiredJump || road.Kind!=TrackSegmentKind.Empty ||
                    road.ExitAnchor.position.z<=clearThrough ||
                    !environmentManager.IsStreetRoadSegment(entry.Index) ||
                    environmentManager.RequiresClearTrack(entry.Index) ||
                    environmentManager.IsTransitionSegment(entry.Index+1) || IsFinishCorridor(entry.Index) ||
                    (hasFiniteRoute && entry.Index>=finishSegmentIndex-1)) continue;
                ThemeObstacleLibrary library=environmentManager.GetObstacleLibraryForRoad(entry.Prefab);
                if(library==null || library.SharedPacing==null) continue;
                float origin=road.EntryAnchor.position.z;
                var request=new ObstacleManager.CityPlanningRequest {
                    Road=entry.Prefab, Index=entry.Index, Zone=environmentManager.EncounterZoneAt(entry.Index),
                    Origin=origin, Limit=Mathf.Min(road.ExitAnchor.position.z,
                        hasFiniteRoute?finishLineZ-finishClearance:float.PositiveInfinity),
                    Difficulty=difficultyManager.EvaluateForDistance(Mathf.Max(0,origin+12)),
                    Library=library, PayloadsOnly=true,
                    SpawnAhead=Mathf.Max(0,origin-player.position.z), RecycleBehind=recycleBehindDistance
                };
                var plan=obstacleManager.PlanCloudReturnPayloads(request);
                taskTracker.RegisterGeneratedRoadPayloads(
                    ()=>obstacleManager.AttachSegment(road,plan,environmentManager,this));
                if(plan.Encounters!=null) populated+=plan.Encounters.Count;
            }
            cloudContinuationRoads.Clear();
            EndlessRollball.Infrastructure.RuntimeLog.Info(
                $"[CloudRoad] Resumed road payloads={populated}, landing buffer ends at Z={clearThrough:F2}.",this);
        }

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

        private StreetBouncePadSpawner streetPadSpawner;

        private bool IsCloudEntryBuffer(int index)
        {
            if (streetPadSpawner == null || environmentManager == null ||
                !environmentManager.IsStreetRoadSegment(index))
                return false;

            // Upcoming segments are measured from the actual generation frontier,
            // including any distance changes from earlier compact junctions.
            float centreZ = nextSpawnZ + (index - spawnedCount + .5f) * TrackSegment.ContractLength;
            return streetPadSpawner.ReservesRoad(centreZ);
        }

        private bool CanPlaceCloudPad(int index)
        {
            if (!IsCloudEntryBuffer(index) || (hasFiniteRoute && index + 2 >= finishSegmentIndex))
                return false;

            for (int nearby = index - 1; nearby <= index + 1; nearby++)
            {
                if (!environmentManager.IsStreetRoadSegment(nearby) ||
                    environmentManager.RequiresClearTrack(nearby))
                    return false;
            }
            return true;
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
