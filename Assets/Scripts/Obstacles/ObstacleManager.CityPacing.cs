using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        internal sealed class CityPlanningRequest
        {
            internal TrackSegment Road, Gap;
            internal int Index, Zone;
            internal bool Blocked, Junction, Traffic, Stops;
            internal DifficultySnapshot Difficulty;
            internal float Origin, Limit;
            internal ThemeObstacleLibrary Library;
            internal Action<int, float, float> RecordEntry;
        }
        private readonly Dictionary<int, SegmentPlan> cityLookahead = new Dictionary<int, SegmentPlan>();
        internal bool TryTakeCityPlan(int index, out SegmentPlan plan)
        {
            if (!cityLookahead.TryGetValue(index, out plan)) return false;
            cityLookahead.Remove(index);
            return true;
        }
        internal void PlanCityWindow(List<CityPlanningRequest> requests)
        {
            // Reserve a lane before filling the eight-segment window. Ordinary
            // hazards can use the other lane, subject to their reachable passage.
            CityPlanningRequest trafficSlot = requests.Count >= 5 ? requests[3] : null;
            Bounds reserved = default;
            bool hasReservation = trafficSlot != null && !trafficSlot.Blocked && trafficSlot.Traffic;
            if (hasReservation)
            {
                float width = trafficSlot.Difficulty.Corridor.Width;
                bool oncoming = !lastLongitudinalOncoming.GetValueOrDefault(false);
                reserved = new Bounds(new Vector3((oncoming ? 1f : -1f) * width * .25f,
                    1f, trafficSlot.Origin + 12f), new Vector3(2.8f, 40f, 216f));
                cityOccupancy.Add(reserved);
            }
            float entryZ = requests.Count == 0 ? 0f : requests[0].Origin;
            foreach (CityPlanningRequest request in requests)
            {
                float delta = entryZ - request.Origin;
                request.Origin = entryZ;
                // Earlier compaction can only bring a protected boundary nearer.
                request.Limit += delta;
                familyMinimum = request.Library != null && request.Library.CityPacing != null ? Mathf.Max(1, request.Library.CityPacing.minimumFamilyAppearances) : 3;
                planningZone = request.Zone;
                EnterQuotaZone(planningZone);
                bool scheduled = hasReservation && request == trafficSlot;
                if (scheduled) cityOccupancy.Remove(reserved);
                cityTrafficScheduled = scheduled;
                cityLookahead[request.Index] = PlanSegment(request.Road, request.Gap, request.Index,
                    request.Blocked, request.Difficulty, request.Origin, request.Limit,
                    request.Library, request.Junction, scheduled, request.Stops);
                SegmentPlan selected = cityLookahead[request.Index];
                TrackSegment selectedTrack = selected.Track != null ? selected.Track : request.Road;
                entryZ = new TrackRouteSpan(request.Index, request.Origin, selectedTrack.EntryLocal.z, selectedTrack.ExitLocal.z).ExitZ;
                request.RecordEntry?.Invoke(request.Index, request.Origin, entryZ);
                cityTrafficScheduled = false;
            }
        }
        private int familyMinimum = 3;
        private int planningZone = -1;
        private int activeQuotaZone = -1;
        private readonly int[] cityFamilyCounts = new int[5];
        private static readonly string[] cityFamilyNames = { "Jump barriers", "Moving traffic", "Roadworks", "Junction", "Parking/stops" };
        private void EnterQuotaZone(int zone)
        {
            if (activeQuotaZone == zone) return;
            if (activeQuotaZone >= 0)
                for (int i = 0; i < cityFamilyCounts.Length; i++)
                    if (cityFamilyCounts[i] < familyMinimum)
                        Debug.LogError($"City zone {activeQuotaZone}: {cityFamilyNames[i]} appeared {cityFamilyCounts[i]}/{familyMinimum} times. Layout or safety validation prevented the quota.", this);
            Array.Clear(cityFamilyCounts, 0, cityFamilyCounts.Length);
            activeQuotaZone = zone;
        }
        private bool cityTrafficScheduled;
        private bool cityReverseSide;
        private bool cityPacingActive;
        private float nextCityDecision = float.NegativeInfinity;
        private int cityCount, citySinceTraffic, citySinceAction;
        private ThemeObstacleLibrary pacedLibrary;
        private readonly List<Bounds> cityOccupancy = new List<Bounds>();
        public int CityRejectedPlacements { get; private set; }
        public int[] CityFamilyAppearances => (int[])cityFamilyCounts.Clone();
        private void ResetCityPacing()
        {
            activeQuotaZone = -1; planningZone = -1;
            Array.Clear(cityFamilyCounts, 0, cityFamilyCounts.Length);
            cityLookahead.Clear();
            cityPacingActive = false; pacedLibrary = null;
            nextCityDecision = float.NegativeInfinity;
            cityCount = citySinceTraffic = citySinceAction = CityRejectedPlacements = 0;
            cityOccupancy.Clear();
        }

        private SegmentPlan PlanPacedCity(TrackSegment road, TrackSegment gap, int index,
            DifficultySnapshot difficulty, float origin, float limit, ThemeObstacleLibrary library,
            bool junction, bool traffic, bool stops)
        {
            if (pacedLibrary != library)
            {
                nextCityDecision = float.NegativeInfinity;
                cityCount = citySinceTraffic = citySinceAction = 0;
                pacedLibrary = library;
            }
            cityPacingActive = true;
            cityReverseSide = false;
            var random = new System.Random(unchecked(runSeed ^ index * 7919));
            var settings = library.CityPacing;
            Vector2 interval = difficulty.Mode == DifficultyMode.Easy ? settings.easySeconds :
                difficulty.Mode == DifficultyMode.Normal ? settings.normalSeconds :
                difficulty.Mode == DifficultyMode.Hard ? settings.hardSeconds :
                Vector2.Lerp(settings.easySeconds, settings.hardSeconds, difficulty.Progress01);
            var maximum = new DifficultySnapshot(difficulty.Mode,
                difficulty.ForwardSpeed + difficulty.SpeedAdjustmentRange, 0f,
                difficulty.ObstacleProbability, difficulty.MovingObstacleSpeed, difficulty.Progress01,
                difficulty.RoadWidth, difficulty.RecoverySeconds, difficulty.ConsecutiveLimit,
                difficulty.CombinationChance);
            cityOccupancy.RemoveAll(bounds => bounds.max.z < origin - 192f);
            int actionLimit = difficulty.Mode == DifficultyMode.Easy ? 4 : 3;
            bool needsAction = citySinceAction >= actionLimit - 1;
            // Try fine distance samples, retaining exact collider and reachability validation.
            for (float centre = 11f; centre <= 21f; centre += 2f)
            {
                if (origin + centre < nextCityDecision && !cityTrafficScheduled) continue;
                long identity = (long)Math.Round((origin + centre) * 1000d);
                var priorities = new List<int> { 0, 1, 2, 3, 4 };
                priorities.Sort((a, b) => {
                    int deficit = cityFamilyCounts[a].CompareTo(cityFamilyCounts[b]);
                    return deficit != 0 ? deficit : a.CompareTo(b);
                });
                foreach (int choice in priorities)
                {
                    var snapshot = planner.Copy();
                    bool? direction = lastLongitudinalOncoming;
                    SegmentPlan result = default;
                    bool action = false;
                    // Track replacements have authored decision points near the centre.
                    bool authoredSlot = centre >= 11f && centre <= 13f;
                    if (choice == 0 && authoredSlot && (needsAction || cityFamilyCounts[0] < familyMinimum) && !cityTrafficScheduled &&
                        TryPlanCombined(library, road, index, identity, maximum, origin, limit,
                            random, out EncounterPlan combined, true))
                    {
                        planner.Commit(index, combined.EndZ, combined.ExitX, combined.Family);
                        result = new SegmentPlan(road, new List<EncounterPlan> { combined });
                        action = true;
                    }
                    else if (choice == 1 && authoredSlot && traffic && (cityTrafficScheduled || cityFamilyCounts[1] < familyMinimum || citySinceTraffic >= 5))
                        TryPlanCityVehicle(library, road, index, identity, maximum, origin, limit,
                            random, false, true, out result, true);
                    else if (choice == 2 && authoredSlot && (cityFamilyCounts[2] < familyMinimum || cityCount % 3 == 1))
                        TryPlanRoadworks(library, index, identity, maximum, origin, limit, random, out result, true);
                    else if (choice == 3 && authoredSlot)
                    {
                        if (junction && (cityFamilyCounts[3] < familyMinimum || cityCount % 7 == 6))
                            TryPlanJunction(library, index, identity, maximum, origin, limit, random, out result, true);
                        else if (stops && cityCount % 5 == 2)
                            TryPlanCityVehicle(library, road, index, identity, maximum, origin, limit,
                                random, true, false, out result, true);
                    }
                    else if (choice == 4)
                    {
                        EncounterPlan ordinary = ChooseEncounter(road, gap, index, identity,
                            maximum, origin, centre, limit, random, true);
                        if (!ordinary.HasEncounter)
                            ordinary = ChooseEncounter(road, gap, index, identity, maximum,
                                origin, centre, limit, random);
                        if (ordinary.HasEncounter)
                        {
                            planner.Commit(index, ordinary.EndZ, ordinary.ExitX, ordinary.Family);
                            result = new SegmentPlan(ordinary.Track, new List<EncounterPlan> { ordinary });
                            action = ordinary.IsGap;
                        }
                    }
                    if (result.Track == null) { planner.Restore(snapshot); lastLongitudinalOncoming = direction; continue; }
                    Bounds occupied = CityBounds(result, origin, difficulty.Corridor.Width);
                    if (cityOccupancy.Exists(previous => previous.Intersects(occupied)))
                    {
                        CityRejectedPlacements++;
                        cityReverseSide = !cityReverseSide;
                        planner.Restore(snapshot); lastLongitudinalOncoming = direction; continue;
                    }
                    cityOccupancy.Add(occupied);
                    int acceptedFamily = result.CityVehicle is LongitudinalTrafficEncounter ? 1 :
                        result.Junction != null ? 3 : result.Roadworks != null ? 2 : action ? 0 : 4;
                    cityFamilyCounts[acceptedFamily]++;
                    cityCount++; citySinceTraffic++;
                    if (result.CityVehicle is LongitudinalTrafficEncounter) citySinceTraffic = 0;
                    citySinceAction = action ? 0 : citySinceAction + 1;
                    int breathAfter = difficulty.Mode == DifficultyMode.Easy ? 3 : difficulty.Mode == DifficultyMode.Normal ? 4 : 5;
                    float seconds = Mathf.Lerp(interval.x, interval.y, (float)random.NextDouble());
                    if (cityCount % breathAfter == 0) seconds += settings.breathingSeconds;
                    float decisionZ = origin + centre;
                    TaskWindow decisionWindow = result.VehicleWindow ?? result.JunctionWindow ?? result.RoadworksWindow;
                    if (decisionWindow != null) decisionZ = decisionWindow.HazardStartZ;
                    else if (result.Encounters != null && result.Encounters.Count > 0 &&
                        result.Encounters[0].Windows != null && result.Encounters[0].Windows.Length > 0)
                        decisionZ = result.Encounters[0].Windows[0].HazardStartZ;
                    nextCityDecision = decisionZ + seconds * difficulty.ForwardSpeed;
                    cityPacingActive = false;
                    return result;
                }
            }
            cityPacingActive = false;
            return new SegmentPlan(road, new List<EncounterPlan>());
        }

        // Conservative swept occupancy includes the visible travel beyond the owning segment.
        // It prevents a car passing through a parked object even before the player arrives.
        private Bounds CityBounds(SegmentPlan plan, float origin, float width)
        {
            Bounds bounds = new Bounds(new Vector3(0f, 1f, origin + 12f), new Vector3(width, 20f, 24f));
            if (plan.CityVehicle != null && TryGetFootprint(plan.VehiclePrefab, out Bounds vehicle))
            {
                bounds = RotatedFootprint(vehicle, plan.CityVehicle.YawDegrees);
                bounds.center += plan.VehiclePosition + Vector3.forward * origin;
                if (plan.CityVehicle.IsMoving)
                {
                    Vector3 min = bounds.min, max = bounds.max;
                    min.z = origin - 96f; max.z = origin + 120f;
                    bounds.SetMinMax(min, max);
                }
            }
            else if (plan.Encounters != null && plan.Encounters.Count > 0 && !plan.IsGap)
            {
                bool first = true;
                foreach (EncounterPlan encounter in plan.Encounters)
                foreach (Placement placement in encounter.Placements)
                {
                    if (!TryGetFootprint(placement.Prefab, out Bounds shape)) continue;
                    shape = RotatedFootprint(shape, placement.YawDegrees);
                    shape.center += placement.Position + Vector3.forward * origin;
                    if (first) { bounds = shape; first = false; } else bounds.Encapsulate(shape);
                }
            }
            bounds.Expand(new Vector3(.3f, 20f, .5f));
            return bounds;
        }
    }
}
