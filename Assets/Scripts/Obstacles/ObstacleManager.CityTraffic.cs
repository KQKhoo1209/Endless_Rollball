using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        private readonly Dictionary<TrackSegment, CityVehicleEncounter> activeCityVehicles =
            new Dictionary<TrackSegment, CityVehicleEncounter>();
        private readonly Dictionary<TrackSegment, GameObject> activeStopSources =
            new Dictionary<TrackSegment, GameObject>();
        private bool? lastLongitudinalOncoming;

        private bool TryPlanTrackAlternative(ThemeObstacleLibrary library, TrackSegment road,
            int index, long identity, DifficultySnapshot difficulty, float originZ, float limit,
            System.Random random, bool allowJunction, bool allowTraffic, bool allowStops,
            bool forceSelection, out SegmentPlan result)
        {
            result = default;
            int offset = random.Next(4);
            for (int i = 0; i < 4; i++)
            {
                switch ((offset + i) % 4)
                {
                    case 0:
                        if (TryPlanCityVehicle(library, road, index, identity, difficulty, originZ,
                            limit, random, allowStops, allowTraffic, out result, forceSelection))
                            return true;
                        break;
                    case 1:
                        if (allowJunction && TryPlanJunction(library, index, identity,
                            difficulty, originZ, limit, random, out result, forceSelection))
                            return true;
                        break;
                    case 2:
                        if (TryPlanRoadworks(library, index, identity, difficulty,
                            originZ, limit, random, out result, forceSelection))
                            return true;
                        break;
                    case 3:
                        if (TryPlanCombined(library, road, index, identity, difficulty,
                            originZ, limit, random, out EncounterPlan combined, forceSelection))
                        {
                            planner.Commit(index, combined.EndZ, combined.ExitX, combined.Family);
                            result = new SegmentPlan(road, new List<EncounterPlan> { combined });
                            return true;
                        }
                        break;
                }
            }
            return false;
        }

        private bool TryPlanCityVehicle(ThemeObstacleLibrary library, TrackSegment road, int index, long identity,
            DifficultySnapshot difficulty, float originZ, float limit, System.Random random,
            bool allowStops, bool allowTraffic, out SegmentPlan result, bool forceSelection = false)
        {
            result = default;
            if (library == null || (!allowStops && !allowTraffic) ||
                (!forceSelection && random.NextDouble() >= .35))
                return false;

            var candidates = new List<CityVehicleEncounter>();
            if (allowStops && library.RoadsideStops != null)
                candidates.AddRange(library.RoadsideStops);
            if (allowTraffic && library.LongitudinalTrafficTracks != null)
                candidates.AddRange(library.LongitudinalTrafficTracks);
            if (candidates.Count == 0)
                return false;

            var maximumDifficulty = new DifficultySnapshot(difficulty.Mode,
                difficulty.ForwardSpeed + difficulty.SpeedAdjustmentRange, 0f,
                difficulty.ObstacleProbability, difficulty.MovingObstacleSpeed,
                difficulty.Progress01, difficulty.RoadWidth, difficulty.RecoverySeconds,
                difficulty.ConsecutiveLimit, difficulty.CombinationChance);
            // Shuffle all candidates so filtering one family does not favour
            // a particular roadside or traffic direction.
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                CityVehicleEncounter value = candidates[i];
                candidates[i] = candidates[other];
                candidates[other] = value;
            }
            // First traffic is oncoming; subsequent successful traffic choices
            // alternate direction. Invalid/missing candidates can still fall back.
            bool preferredOncoming = !lastLongitudinalOncoming.GetValueOrDefault(false);
            var deferred = new List<CityVehicleEncounter>();
            for (int i = candidates.Count - 1; i >= 0; i--)
                if (candidates[i] is LongitudinalTrafficEncounter traffic &&
                    traffic.Oncoming != preferredOncoming)
                {
                    deferred.Add(candidates[i]);
                    candidates.RemoveAt(i);
                }
            candidates.AddRange(deferred);
            for (int i = 0; i < candidates.Count; i++)
            {
                CityVehicleEncounter authored = candidates[i];
                if (authored == null || authored.FamilyId == planner.LastFamily)
                    continue;
                TrackSegment track = authored.IsMoving ? authored.GetComponent<TrackSegment>() : road;
                Transform payloadRoot = authored.IsMoving && track != null ? track.VisualRoot : authored.transform;
                if (track == null || !track.IsContractValid(out _) || !track.Supports(difficulty.Mode) ||
                    track.RequiredJump || track.Kind != (authored.IsMoving
                        ? TrackSegmentKind.MovingObstacle : TrackSegmentKind.Empty) ||
                    track.transform.localPosition != Vector3.zero ||
                    track.transform.localRotation != Quaternion.identity ||
                    track.transform.localScale != Vector3.one || track.VisualRoot == track.transform ||
                    track.VisualRoot.localPosition != Vector3.zero ||
                    track.VisualRoot.localRotation != Quaternion.identity ||
                    track.VisualRoot.localScale != Vector3.one || authored.VehicleRoot == null ||
                    authored.transform.localPosition != Vector3.zero ||
                    authored.transform.localRotation != Quaternion.identity ||
                    authored.transform.localScale != Vector3.one ||
                    (!authored.IsMoving && authored.GetComponent<TrackSegment>() != null) ||
                    authored.VehicleRoot.parent != payloadRoot ||
                    authored.VehicleRoot.localPosition != Vector3.zero ||
                    authored.VehicleRoot.localRotation != Quaternion.identity ||
                    authored.VehicleRoot.localScale != Vector3.one)
                    continue;

                GameObject[] sources = authored.Sources;
                if (sources == null || sources.Length == 0 ||
                    !ValidateList(new List<GameObject>(sources), false))
                    continue;
                bool valid = true;
                foreach (GameObject source in sources)
                    if (!source.activeSelf || source.transform.localPosition != Vector3.zero ||
                        source.transform.localRotation != Quaternion.identity ||
                        source.transform.localScale != Vector3.one || !TryGetFootprint(source, out _))
                        valid = false;
                if (!valid)
                    continue;

                GameObject selected = authored.SelectVehicle(random);
                if (selected == null || !TryGetFootprint(selected, out Bounds bounds))
                    continue;
                bounds = RotatedFootprint(bounds, authored.YawDegrees);
                float halfWidth = difficulty.Corridor.Width * .5f;
                float clearance = Radius + PassagePadding;
                bool right = authored is RoadsideStopEncounter stop ? stop.RightSide :
                    ((LongitudinalTrafficEncounter)authored).Oncoming;
                float safeX = (right ? -1f : 1f) * difficulty.Corridor.Width * .25f;
                float x = authored.IsMoving ? -safeX : right
                    ? halfWidth - RoadCorridor.BoundarySkin - bounds.max.x
                    : -halfWidth + RoadCorridor.BoundarySkin - bounds.min.x;
                if (!difficulty.Corridor.Contains(safeX, clearance) ||
                    x + bounds.min.x < -halfWidth || x + bounds.max.x > halfWidth ||
                    (safeX + clearance > x + bounds.min.x && safeX - clearance < x + bounds.max.x))
                    continue;

                float z = authored.IsMoving ? authored.VehicleZ(0f) : 12f - bounds.center.z;
                float start, landing, end, deadline, approach;
                if (authored.IsMoving)
                {
                    float halfLength = Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z));
                    float overlap = halfLength + clearance +
                        1.5f * maximumDifficulty.ForwardSpeed * Time.fixedDeltaTime * 2f;
                    bool oncoming = ((LongitudinalTrafficEncounter)authored).Oncoming;
                    float first = oncoming ? (15f - overlap) / 1.5f : 18f - 2f * overlap;
                    float last = oncoming ? (15f + overlap) / 1.5f : 18f + 2f * overlap;
                    if (first < .5f || last >= 28f)
                        continue;
                    approach = originZ - 24f;
                    start = originZ + first;
                    landing = originZ + 28f;
                    deadline = originZ + 30f;
                    end = originZ + 36f;
                }
                else
                {
                    start = originZ + z + bounds.min.z;
                    landing = originZ + z + bounds.max.z + clearance;
                    end = deadline = originZ + TrackSegment.ContractLength;
                    approach = start - 12f;
                    if (start < originZ + .5f || landing + .5f >= end)
                        continue;
                }
                if (end > limit || !planner.CanReach(index, authored.IsMoving && !cityPacingActive ? approach : start,
                    end, safeX, maximumDifficulty, LateralSpeed, Acceleration))
                    continue;

                var window = new TaskWindow
                {
                    LocalIndex = 0, EntryZ = approach, HazardStartZ = start,
                    LandingStartZ = landing, DeadlineZ = deadline, RecoveryEndZ = end
                };
                result = new SegmentPlan(track, new List<EncounterPlan>(), index: index,
                    encounterId: identity, cityVehicle: authored, vehiclePrefab: selected,
                    vehiclePosition: new Vector3(x, 0f, z), vehicleWindow: window);
                planner.Commit(index, end, safeX, authored.FamilyId);
                if (authored is LongitudinalTrafficEncounter selectedTraffic)
                    lastLongitudinalOncoming = selectedTraffic.Oncoming;
                return true;
            }
            return false;
        }

        private void AttachCityVehicle(TrackSegment track, SegmentPlan plan,
            EndlessRollball.Scenery.EnvironmentManager environment, TrackGenerator generator)
        {
            if (!ready || track == null || plan.Track == null ||
                track.SegmentId != plan.Track.SegmentId || activeCityVehicles.ContainsKey(track))
                return;
            CityVehicleEncounter encounter;
            GameObject stopSource = null;
            GameObject stopInstance = null;
            if (!plan.CityVehicle.IsMoving)
            {
                stopSource = plan.CityVehicle.gameObject;
                GameObject instance = prefabPool.Acquire(stopSource, poolRoot);
                stopInstance = instance;
                if (poolStatistics.Count != prefabPool.Statistics.Count)
                {
                    poolStatistics.Clear();
                    poolStatistics.AddRange(prefabPool.Statistics);
                }
                instance.transform.SetParent(track.VisualRoot, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                instance.SetActive(true);
                encounter = instance.GetComponent<CityVehicleEncounter>();
            }
            else
            {
                encounter = track.GetComponent<CityVehicleEncounter>();
            }
            if (encounter == null || ballMovement == null ||
                !encounter.Prepare(plan.VehiclePrefab, ballMovement.transform, plan.VehiclePosition, generator))
            {
                Debug.LogError("Could not prepare City vehicle encounter.", track);
                if (stopSource != null && stopInstance != null)
                {
                    stopInstance.SetActive(false);
                    prefabPool.Return(stopSource, stopInstance, poolRoot, maxInactivePerPrefab);
                }
                return;
            }
            activeCityVehicles.Add(track, encounter);
            if (stopSource != null)
            {
                activeStopSources.Add(track, stopSource);
                environment?.SetRoadsideStop(track, ((RoadsideStopEncounter)encounter).RightSide);
            }
            taskTracker?.RegisterPayload(track, plan.Index, encounter.VehicleRoot.gameObject,
                false, null, 0, encounter.Hazards, plan.EncounterId,
                plan.VehicleWindow, encounter.IsMoving);
            encounter.SetRunning(!motionPaused);
        }

        private bool OwnsCityVehicleCollider(Collider shape)
        {
            foreach (var entry in activeCityVehicles)
                if (entry.Key != null && entry.Key.gameObject.activeInHierarchy &&
                    entry.Value != null && Array.IndexOf(entry.Value.Hazards, shape) >= 0)
                    return true;
            return false;
        }

        private void ReleaseCityVehicle(TrackSegment track)
        {
            if (track != null && activeCityVehicles.TryGetValue(track, out CityVehicleEncounter value))
            {
                if (value != null) value.SetRunning(false);
                if (activeStopSources.TryGetValue(track, out GameObject source))
                {
                    if (value != null) value.gameObject.SetActive(false);
                    prefabPool.Return(source, value != null ? value.gameObject : null,
                        poolRoot, maxInactivePerPrefab);
                    activeStopSources.Remove(track);
                }
                activeCityVehicles.Remove(track);
            }
        }

        private void SetCityVehiclesPaused(bool paused)
        {
            foreach (CityVehicleEncounter value in activeCityVehicles.Values)
                if (value != null) value.SetRunning(!paused);
        }

        private void EndCityVehicles()
        {
            foreach (TrackSegment track in new List<TrackSegment>(activeCityVehicles.Keys))
                ReleaseCityVehicle(track);
            activeCityVehicles.Clear();
            activeStopSources.Clear();
            lastLongitudinalOncoming = null;
        }
    }
}
