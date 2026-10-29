using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        private readonly Dictionary<TrackSegment, Collider[]> activeRoadworks =
            new Dictionary<TrackSegment, Collider[]>();

        private bool TryPlanRoadworks(
            ThemeObstacleLibrary library,
            int index,
            long identity,
            DifficultySnapshot difficulty,
            float originZ,
            float reservationLimit,
            System.Random random,
            out SegmentPlan result, bool forceSelection = false)
        {
            result = default;

            if (library == null ||
                library.RoadworksTracks == null ||
                library.RoadworksTracks.Length == 0)
            {
                return false;
            }

            // Roadworks is considered only after the normal encounter roll.
            if (!forceSelection && random.NextDouble() >= 0.35)
                return false;

            int offset = random.Next(library.RoadworksTracks.Length);

            for (int attempt = 0;
                attempt < library.RoadworksTracks.Length;
                attempt++)
            {
                RoadworksTrackEncounter encounter =
                    library.RoadworksTracks[
                        (offset + attempt) % library.RoadworksTracks.Length];

                if (encounter == null ||
                    string.IsNullOrWhiteSpace(encounter.FamilyId) ||
                    encounter.FamilyId == planner.LastFamily ||
                    encounter.HazardRoot == null ||
                    encounter.HazardRoot == encounter.gameObject ||
                    !encounter.HazardRoot.transform.IsChildOf(
                        encounter.transform))
                {
                    continue;
                }

                TrackSegment track = encounter.Track;

                if (track == null ||
                    !track.IsContractValid(out _) ||
                    !track.Supports(difficulty.Mode) ||
                    track.Kind != TrackSegmentKind.StaticObstacle ||
                    track.RequiredJump ||
                    track.transform.localPosition != Vector3.zero ||
                    track.transform.localRotation != Quaternion.identity ||
                    track.transform.localScale != Vector3.one)
                {
                    continue;
                }

                float start = originZ + encounter.HazardStartZ;
                float landing = originZ + encounter.HazardEndZ
                    + Radius + 0.15f;

                // Reserve the entire segment, including its exit.
                float end = originZ + TrackSegment.ContractLength;

                if (encounter.HazardStartZ < 0.5f ||
                    encounter.HazardEndZ <= encounter.HazardStartZ ||
                    landing + 0.5f >= end ||
                    end > reservationLimit ||
                    !difficulty.Corridor.Contains(
                        encounter.SafePassageX,
                        Radius + PassagePadding) ||
                    !planner.CanReach(
                        index, start, end,
                        encounter.SafePassageX,
                        difficulty, LateralSpeed, Acceleration))
                {
                    continue;
                }

                var window = new TaskWindow
                {
                    LocalIndex = 0,
                    EntryZ = start - 12f,
                    HazardStartZ = start,
                    LandingStartZ = landing,
                    DeadlineZ = end,
                    RecoveryEndZ = end
                };

                result = new SegmentPlan(
                    track,
                    new List<EncounterPlan>(),
                    encounter,
                    index,
                    identity,
                    window);

                planner.Commit(
                    index, end,
                    encounter.SafePassageX,
                    encounter.FamilyId);

                return true;
            }

            return false;
        }

        private void AttachRoadworks(
            TrackSegment segment,
            SegmentPlan plan)
        {
            if (!ready ||
                segment == null ||
                plan.Track == null ||
                segment.SegmentId != plan.Track.SegmentId ||
                activeRoadworks.ContainsKey(segment))
            {
                return;
            }

            RoadworksTrackEncounter encounter =
                segment.GetComponent<RoadworksTrackEncounter>();

            if (encounter == null || encounter.HazardRoot == null)
                return;

            var hazardList = new List<Collider>();

            foreach (Collider shape in
                encounter.HazardRoot.GetComponentsInChildren<Collider>(true))
            {
                if (shape.GetComponentInParent<TrafficConeProp>() != null)
                    continue;

                hazardList.Add(shape);
            }

            Collider[] hazards = hazardList.ToArray();

            activeRoadworks.Add(segment, hazards);

            // All props and the patch belong to one navigation task.
            taskTracker?.RegisterPayload(
                segment,
                plan.Index,
                encounter.HazardRoot,
                false,
                null,
                0,
                hazards,
                plan.EncounterId,
                plan.RoadworksWindow);
        }

        internal bool OwnsRoadworksCollider(Collider shape)
        {
            if (shape == null ||
                !shape.enabled ||
                !shape.gameObject.activeInHierarchy)
            {
                return false;
            }

            foreach (var entry in activeRoadworks)
            {
                if (entry.Key == null ||
                    !entry.Key.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (Array.IndexOf(entry.Value, shape) >= 0)
                    return true;
            }

            return false;
        }
    }
}
