using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        private const string JunctionFamily = "Street.Junction";

        private sealed class ActiveJunction
        {
            public JunctionTrafficController Controller;
            public Collider[] Hazards;
        }

        private readonly Dictionary<TrackSegment, ActiveJunction> activeJunctions =
            new Dictionary<TrackSegment, ActiveJunction>();

        private bool TryPlanJunction(
            ThemeObstacleLibrary library, int index, long identity,
            DifficultySnapshot difficulty, float originZ,
            float reservationLimit, System.Random random,
            out SegmentPlan result, bool forceSelection = false)
        {
            result = default;
            if (library == null || library.JunctionTracks == null ||
                library.JunctionTracks.Length == 0 ||
                planner.LastFamily == JunctionFamily || (!forceSelection && random.NextDouble() >= 0.25))
                return false;

            int offset = random.Next(library.JunctionTracks.Length);
            // Steering and physics clearance must also allow the player's
            // configured forward-speed adjustment, not just the base speed.
            var traversalDifficulty = new DifficultySnapshot(
                difficulty.Mode, difficulty.ForwardSpeed + difficulty.SpeedAdjustmentRange,
                0f, difficulty.ObstacleProbability, difficulty.MovingObstacleSpeed,
                difficulty.Progress01, difficulty.RoadWidth, difficulty.RecoverySeconds,
                difficulty.ConsecutiveLimit, difficulty.CombinationChance);
            for (int attempt = 0; attempt < library.JunctionTracks.Length; attempt++)
            {
                JunctionTrafficController authored = library.JunctionTracks[
                    (offset + attempt) % library.JunctionTracks.Length];
                if (authored == null)
                    continue;

                TrackSegment track = authored.GetComponent<TrackSegment>();
                if (track == null || !track.IsContractValid(out _) ||
                    !track.Supports(difficulty.Mode) ||
                    track.Kind != TrackSegmentKind.MovingObstacle || track.RequiredJump ||
                    track.transform.localPosition != Vector3.zero ||
                    track.transform.localRotation != Quaternion.identity ||
                    track.transform.localScale != Vector3.one ||
                    track.VisualRoot == track.transform ||
                    track.VisualRoot.localPosition != Vector3.zero ||
                    track.VisualRoot.localRotation != Quaternion.identity ||
                    track.VisualRoot.localScale != Vector3.one ||
                    authored.TrafficRoot == null ||
                    authored.TrafficRoot.parent != track.VisualRoot ||
                    authored.TrafficRoot.localPosition != Vector3.zero ||
                    authored.TrafficRoot.localRotation != Quaternion.identity ||
                    authored.TrafficRoot.localScale != Vector3.one)
                    continue;

                if (!TryGetJunctionVehicleSize(authored, out float halfDepth, out float halfLength))
                    continue;

                int seed = random.Next();
                float safeX = JunctionTrafficController.PlannedSafePassage(seed);
                float clearance = Radius + PassagePadding;
                // Allow for player/car update order and one subsequent physics step.
                float overlapDistance = halfDepth + clearance +
                    traversalDifficulty.ForwardSpeed * Time.fixedDeltaTime * 2f;

                // The early car finishes three metres before its lane centre.
                if (overlapDistance >= 3f)
                    continue;

                // At earliest overlap, a left-to-right car must already have
                // cleared the left passage. The opposite direction is symmetric.
                float progress = Mathf.Clamp01((18f - overlapDistance) / 27f);
                float nearestEdge = Mathf.Lerp(-12f, 12f, progress) - halfLength;
                if (nearestEdge <= -1.7f + clearance ||
                    12f - halfLength <= difficulty.Corridor.Width * .5f +
                        Mathf.Abs(difficulty.Corridor.CentreX) + clearance)
                    continue;

                // The request origin is the physical entry; traffic uses root-local coordinates.
                float rootZ = originZ - track.EntryLocal.z;
                float approach = rootZ - 24f;
                float end = Mathf.Max(originZ + track.Length, rootZ + 24f);
                if (end > reservationLimit ||
                    !difficulty.Corridor.Contains(safeX, clearance) ||
                    !planner.CanReach(index, cityPacingActive ? rootZ + 10f - overlapDistance : approach, end, safeX,
                        traversalDifficulty, LateralSpeed, Acceleration))
                    continue;

                var window = new TaskWindow
                {
                    LocalIndex = 0,
                    EntryZ = approach,
                    HazardStartZ = rootZ + 10f - overlapDistance,
                    LandingStartZ = rootZ + 14f + overlapDistance,
                    DeadlineZ = end,
                    RecoveryEndZ = end
                };
                result = new SegmentPlan(track, new List<EncounterPlan>(),
                    index: index, encounterId: identity, junction: authored,
                    junctionWindow: window, trafficSeed: seed);
                planner.Commit(index, end, safeX, JunctionFamily);
                return true;
            }
            return false;
        }

        private bool TryGetJunctionVehicleSize(JunctionTrafficController controller,
            out float halfDepth, out float halfLength)
        {
            halfDepth = halfLength = 0f;
            GameObject[] sources = controller.VehiclePrefabs;
            if (sources == null || sources.Length == 0 ||
                !ValidateList(new List<GameObject>(sources), false))
                return false;

            foreach (GameObject prefab in sources)
            {
                if (!prefab.activeSelf ||
                    prefab.transform.localPosition != Vector3.zero ||
                    prefab.transform.localRotation != Quaternion.identity ||
                    prefab.transform.localScale != Vector3.one ||
                    !TryGetFootprint(prefab, out Bounds bounds))
                    return false;

                // Sources face +Z; their quarter-turn makes source X the
                // crossing depth and source Z the length along the crossing.
                halfDepth = Mathf.Max(halfDepth,
                    Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)));
                halfLength = Mathf.Max(halfLength,
                    Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z)));
            }
            return true;
        }

        private void AttachJunction(TrackSegment segment, SegmentPlan plan)
        {
            if (!ready || segment == null || plan.Track == null ||
                segment.SegmentId != plan.Track.SegmentId ||
                activeJunctions.ContainsKey(segment))
                return;

            JunctionTrafficController controller = segment.GetComponent<JunctionTrafficController>();
            if (controller == null || ballMovement == null ||
                !controller.Prepare(ballMovement.transform, plan.TrafficSeed))
            {
                Debug.LogError("Could not prepare junction traffic.", segment);
                return;
            }

            Collider[] hazards = controller.GetTrafficColliders();
            activeJunctions.Add(segment, new ActiveJunction
            {
                Controller = controller,
                Hazards = hazards
            });
            // Both vehicles belong to the same navigation opportunity.
            taskTracker?.RegisterPayload(segment, plan.Index,
                controller.TrafficRoot.gameObject, false, null, 0,
                hazards, plan.EncounterId, plan.JunctionWindow);
            controller.SetRunning(!motionPaused);
        }

        private bool OwnsJunctionCollider(Collider shape)
        {
            foreach (var entry in activeJunctions)
                if (entry.Key != null && entry.Key.gameObject.activeInHierarchy &&
                    Array.IndexOf(entry.Value.Hazards, shape) >= 0)
                    return true;
            return false;
        }

        private void ReleaseJunction(TrackSegment segment)
        {
            if (segment == null || !activeJunctions.TryGetValue(segment, out ActiveJunction encounter))
                return;
            if (encounter.Controller != null)
                encounter.Controller.SetRunning(false);
            activeJunctions.Remove(segment);
        }

        private void SetJunctionsPaused(bool paused)
        {
            foreach (ActiveJunction encounter in activeJunctions.Values)
                if (encounter.Controller != null)
                    encounter.Controller.SetRunning(!paused);
        }

        private void EndJunctions()
        {
            SetJunctionsPaused(true);
            activeJunctions.Clear();
        }
    }
}
