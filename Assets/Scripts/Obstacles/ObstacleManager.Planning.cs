using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        internal sealed class TaskWindow
        {
            public int LocalIndex;
            public float EntryZ, HazardStartZ, LandingStartZ, DeadlineZ, RecoveryEndZ;
        }

        internal readonly struct SegmentPlan
        {
            public SegmentPlan(
                TrackSegment track,
                List<EncounterPlan> encounters,
                RoadworksTrackEncounter roadworks = null,
                int index = 0,
                long encounterId = 0,
                TaskWindow roadworksWindow = null,
                JunctionTrafficController junction = null,
                TaskWindow junctionWindow = null,
                int trafficSeed = 0,
                CityVehicleEncounter cityVehicle = null,
                GameObject vehiclePrefab = null,
                Vector3 vehiclePosition = default,
                TaskWindow vehicleWindow = null,
                BeachBarrierEncounter beachBarrier = null)
            {
                BeachBarrier = beachBarrier;
                Track = track;
                Encounters = encounters;
                Roadworks = roadworks;
                Index = index;
                EncounterId = encounterId;
                RoadworksWindow = roadworksWindow;
                Junction = junction;
                JunctionWindow = junctionWindow;
                TrafficSeed = trafficSeed;
                CityVehicle = cityVehicle;
                VehiclePrefab = vehiclePrefab;
                VehiclePosition = vehiclePosition;
                VehicleWindow = vehicleWindow;
            }

            public BeachBarrierEncounter BeachBarrier { get; }
            public TrackSegment Track { get; }
            public List<EncounterPlan> Encounters { get; }
            public RoadworksTrackEncounter Roadworks { get; }
            public int Index { get; }
            public long EncounterId { get; }
            public TaskWindow RoadworksWindow { get; }
            public JunctionTrafficController Junction { get; }
            public TaskWindow JunctionWindow { get; }
            public int TrafficSeed { get; }
            public CityVehicleEncounter CityVehicle { get; }
            public GameObject VehiclePrefab { get; }
            public Vector3 VehiclePosition { get; }
            public TaskWindow VehicleWindow { get; }

            public bool IsGap => Track != null && Track.RequiredJump;
            public bool ReplacesTrack => BeachBarrier != null || IsGap || Roadworks != null || Junction != null ||
                (CityVehicle != null && CityVehicle.IsMoving);

            public float EndZ
            {
                get
                {
                    if (VehicleWindow != null)
                        return VehicleWindow.RecoveryEndZ;
                    if (JunctionWindow != null)
                        return JunctionWindow.RecoveryEndZ;
                    if (RoadworksWindow != null)
                        return RoadworksWindow.RecoveryEndZ;

                    float end = float.NegativeInfinity;

                    if (Encounters != null)
                        foreach (EncounterPlan plan in Encounters)
                            end = Mathf.Max(end, plan.EndZ);

                    return end;
                }
            }
        }

        internal SegmentPlan PlanSegment(TrackSegment road, TrackSegment gap, int index, bool blocked, DifficultySnapshot difficulty, float originZ, float reservationLimit, ThemeObstacleLibrary library = null, bool allowJunction = false, bool allowCityTraffic = false, bool allowStops = false, bool allowBeachBarriers = false)
        {
            if (!ready || !isActiveAndEnabled || road == null || blocked || index < 0 || road.Kind != TrackSegmentKind.Empty)
                return default;

            UseFixedThemeLibrary(library);

            bool dungeonLibraryAvailable=library!=null && (library.DungeonTraps.Length>0 || library.RollingStoneBalls.Length>0);
            if (dungeonLibraryAvailable)
            {
                // Give independently configured physical gaps their existing selection chance first.
                var gapRandom=new System.Random(unchecked(runSeed ^ index*3571));
                var savedFixed=runFixed; runFixed=Array.Empty<GameObject>();
                var physicalGap=ChooseEncounter(road,gap,index,(long)Math.Round((originZ+12)*1000),difficulty,originZ,12,reservationLimit,gapRandom,true);
                runFixed=savedFixed;
                if(physicalGap.IsGap)
                { planner.Commit(index,physicalGap.EndZ,0,"Gap"); return new SegmentPlan(gap,new List<EncounterPlan>{physicalGap}); }
                if(TryPlanDungeon(road,index,difficulty,originZ,reservationLimit,library,out var dungeon)) return dungeon;
            }
            if(!dungeonLibraryAvailable) FinishDungeonVisit();
            else runFixed = Array.Empty<GameObject>(); // Dungeon rejects must not bypass its reservations through generic parking.

            if (allowBeachBarriers && TryPlanBeachBarrier(road, index, difficulty, originZ, reservationLimit, library, out SegmentPlan beach)) return beach;

            var encounters = new List<EncounterPlan>(3);
            if (library != null && library.CityPacing != null)
                return PlanPacedCity(road, gap, index, difficulty, originZ, reservationLimit,
                    library, allowJunction, allowCityTraffic, allowStops);
            float spacing = difficulty.Mode == DifficultyMode.Hard ? 8f : difficulty.Mode == DifficultyMode.Normal ? 12f : difficulty.Mode == DifficultyMode.Endless ? Mathf.Lerp(24f, 8f, difficulty.Progress01) : 24f;
            int slots = Mathf.Max(1, Mathf.FloorToInt(24f / spacing));
            TrackSegment selected = road;
            for (int slot = 0; slot < slots; slot++)
            {
                float centre = spacing * .5f + spacing * slot;
                long identity = (long)Math.Round((originZ + centre) * 1000d);
                var random = new System.Random(unchecked(runSeed ^ (int)identity ^ (int)(identity >> 32)));
                if (!planner.Roll(index, EncounterPlanner.Probability(difficulty), difficulty, random))
                    continue;
                if (slot == 0 && TryPlanTrackAlternative(library, road, index, identity,
                    difficulty, originZ, reservationLimit, random, allowJunction,
                    allowCityTraffic, allowStops, false, out SegmentPlan replacement))
                    return replacement;
                EncounterPlan plan = ChooseEncounter(road, slot == 0 ? gap : null, index, identity, difficulty, originZ, centre, reservationLimit, random, true);
                if (!plan.HasEncounter)
                {
                    // Before repeating a family, try all valid alternative
                    // track encounters without their usual frequency roll.
                    if (slot == 0 && TryPlanTrackAlternative(library, road, index, identity,
                        difficulty, originZ, reservationLimit, random, allowJunction,
                        allowCityTraffic, allowStops, true, out replacement))
                        return replacement;
                    plan = ChooseEncounter(road, slot == 0 ? gap : null, index, identity,
                        difficulty, originZ, centre, reservationLimit, random);
                }
                if (!plan.HasEncounter)
                    continue;
                encounters.Add(plan);
                planner.Commit(index, plan.EndZ, plan.ExitX, plan.Family);
                if (plan.IsGap)
                {
                    selected = gap;
                    break;
                }
            }

            return new SegmentPlan(selected, encounters);
        }

        // Compatibility entry point for callers that request one segment's first encounter.
        internal EncounterPlan PlanEncounter(TrackSegment road, TrackSegment gap, int index, bool blocked, DifficultySnapshot difficulty, float originZ, ThemeObstacleLibrary library = null)
        {
            if (!ready || blocked || road == null || road.Kind != TrackSegmentKind.Empty)
                return default;

            UseFixedThemeLibrary(library);

            long identity = (long)Math.Round((originZ + 12f) * 1000d);
            var random = new System.Random(unchecked(runSeed ^ (int)identity ^ (int)(identity >> 32)));
            if (!planner.Roll(index, EncounterPlanner.Probability(difficulty), difficulty, random))
                return default;
            EncounterPlan plan = ChooseEncounter(road, gap, index, identity, difficulty, originZ, 12f, originZ + 23f, random);
            if (plan.HasEncounter)
                planner.Commit(index, plan.EndZ, plan.ExitX, plan.Family);
            return plan;
        }

        internal void AttachSegment(TrackSegment segment, SegmentPlan plan,
            EndlessRollball.Scenery.EnvironmentManager environment = null,
            TrackGenerator generator = null)
        {
            if (plan.BeachBarrier != null)
            { AttachBeachBarrier(segment, plan); return; }
            if (plan.CityVehicle != null)
            {
                AttachCityVehicle(segment, plan, environment, generator);
                return;
            }
            if (plan.Junction != null)
            {
                AttachJunction(segment, plan);
                return;
            }
            if (plan.Roadworks != null)
            {
                AttachRoadworks(segment, plan);
                return;
            }

            if (plan.Encounters != null)
                foreach (EncounterPlan encounter in plan.Encounters)
                    AttachPlanned(segment, encounter, generator);
        }

        private EncounterPlan ChooseEncounter(TrackSegment road, TrackSegment gap, int index, long identity, DifficultySnapshot difficulty, float origin, float centre, float limit, System.Random random, bool avoidLastFamily = false)
        {
            bool jumping = random.NextDouble() < jumpFraction;
            if (jumping && gap != null && GapTracksEnabled && (!avoidLastFamily || planner.LastFamily != "Gap") && random.NextDouble() < runGapShare)
            {
                float first = origin + GapTakeoffZ(gap);
                float landing = origin + GapLandingZ(gap) + Radius + .25f;
                float end = Mathf.Max(landing + 2f, first + 2f * JumpHalfTravel(difficulty) + Radius + .5f);
                if (end <= limit && planner.CanReach(index, first, end, 0f, difficulty, LateralSpeed, Acceleration))
                {
                    var window = new TaskWindow
                    {
                        LocalIndex = 1,
                        EntryZ = first - 12f,
                        HazardStartZ = first,
                        LandingStartZ = landing,
                        DeadlineZ = end,
                        RecoveryEndZ = end
                    };
                    return new EncounterPlan(this, runVersion, index, gap, Array.Empty<Placement>(), true, end, 0f, 9f, identity, "Gap", new[] { window }, 1);
                }
            }
            // Physical gaps were considered above.
            // A physical jump obstacle additionally requires a jump prefab.
            jumping = jumping && runJump.Length > 0;

            bool moving = !jumping && runMoving.Length > 0 && difficulty.MovingObstacleSpeed > 0f && random.NextDouble() < movingFraction;
            GameObject[] choices = jumping ? runJump : moving ? runMoving : runFixed;
            for (int family = 0; family < 2; family++)
            {
                if (choices.Length == 0)
                {
                    choices = runFixed;
                    jumping = moving = false;
                    continue;
                }

                int offset = random.Next(choices.Length);
                for (int attempt = 0; attempt < choices.Length; attempt++)
                {
                    GameObject prefab = choices[(offset + attempt) % choices.Length];
                    if (!TryGetFootprint(prefab, out Bounds bounds))
                        continue;
                    string familyId = moving ? "Moving" : jumping ? "Hurdle" : FixedFamilyFor(prefab);
                    if (avoidLastFamily && familyId == planner.LastFamily)
                        continue;
                    float yawDegrees = 0f;
                    float halfWidth = difficulty.Corridor.Width * .5f;
                    float x = -bounds.center.x, exit = 0f;
                    Bounds sweep = bounds;
                    if (jumping && (bounds.size.x > difficulty.Corridor.Width || !CanClearJump(bounds, difficulty)))
                        continue;
                    if (!jumping && !moving)
                    {
                        PalmObstacle palm = prefab.GetComponent<PalmObstacle>();
                        bool beachRoot = palm != null && palm.RootedOnBeach;
                        if (!beachRoot && bounds.size.x + 2f * (Radius + PassagePadding + RoadCorridor.BoundarySkin) > difficulty.Corridor.Width)
                            continue;
                        bool left = cityPacingActive ? (planner.PreferredOppositeSide > 0f) != cityReverseSide : random.Next(2) == 0;
                        if (palm != null) left = palm.BlocksLeft;
                        if (familyId == "Street.ParkedVehicles" && !left)
                        {
                            yawDegrees = 180f;
                            bounds = RotatedFootprint(bounds, yawDegrees);
                            sweep = bounds;
                        }
                        x = beachRoot ? palm.RoadsideX(halfWidth) :
                            left ? -halfWidth + RoadCorridor.BoundarySkin - bounds.min.x : halfWidth - RoadCorridor.BoundarySkin - bounds.max.x;
                        if (beachRoot)
                        {
                            float nearEdge = left ? x + bounds.max.x : x + bounds.min.x;
                            float passageWidth = left ? halfWidth - nearEdge : nearEdge + halfWidth;
                            if (nearEdge <= -halfWidth || nearEdge >= halfWidth ||
                                passageWidth < 2f * (Radius + PassagePadding + RoadCorridor.BoundarySkin))
                                continue;
                        }
                        exit = left ? (x + bounds.max.x + halfWidth) * .5f : (-halfWidth + x + bounds.min.x) * .5f;
                    }

                    if (moving)
                    {
                        SideToSideObstacleMotion motion = Geometry(prefab).Motions.Length == 1 ? Geometry(prefab).Motions[0] : null;
                        exit = (random.Next(2) == 0 ? -1f : 1f) * (halfWidth - Radius - PassagePadding - RoadCorridor.BoundarySkin);
                        x = 0f; // The motion path is measured in the obstacle root's coordinates.
                        if (motion == null || motion.transform.parent != prefab.transform ||
                            !motion.TryPlanBounds(halfWidth, exit, Radius + PassagePadding, out Bounds travel))
                            continue;
                        sweep.SetMinMax(bounds.min + travel.min - motion.transform.localPosition,
                            bounds.max + travel.max - motion.transform.localPosition);
                    }
                    float landingAllowance = jumping ? JumpHalfTravel(difficulty) + .5f : 1f;
                    float placementCentre = Mathf.Min(centre, limit - origin - sweep.size.z * .5f - Radius - .15f - landingAllowance);
                    float first = origin + placementCentre - sweep.size.z * .5f;
                    float last = origin + placementCentre + sweep.size.z * .5f;
                    float landing = last + Radius + .15f;
                    float end = landing + landingAllowance;
                    if (first < origin + .5f || end > limit || !difficulty.Corridor.Contains(exit, Radius) || !planner.CanReach(index, first, end, exit, difficulty, LateralSpeed, Acceleration))
                        continue;
                    int task = jumping ? 1 : 0;
                    var window = new TaskWindow
                    {
                        LocalIndex = task,
                        EntryZ = first - 12f,
                        HazardStartZ = first,
                        LandingStartZ = landing,
                        DeadlineZ = end,
                        RecoveryEndZ = end
                    };
                    var placement = new Placement(prefab, new Vector3(x, 0f, placementCentre - sweep.center.z), jumping, random.Next(2) == 0 ? .25f : .75f, moving ? difficulty.MovingObstacleSpeed : 0f, task, yawDegrees);
                    return new EncounterPlan(this, runVersion, index, road, new[] { placement }, false, end, exit, difficulty.Corridor.Width, identity, familyId, new[] { window });
                }

                choices = runFixed;
                jumping = moving = false;
            }

            return default;
        }

        private const float PassagePadding = .15f;
        private float Radius => ballMovement != null ? ballMovement.SphereRadius : .5f;
        private float LateralSpeed => ballMovement != null ? ballMovement.MaxLateralSpeed : 7f;
        private float Acceleration => ballMovement != null ? ballMovement.SteeringAcceleration : 22f;

        private float JumpHalfTravel(DifficultySnapshot difficulty) => difficulty.ForwardSpeed * (ballMovement != null ? ballMovement.JumpSpeed : 6f) / Mathf.Max(.1f, -Physics.gravity.y);
        private bool CanClearJump(Bounds bounds, DifficultySnapshot difficulty)
        {
            float gravity = Mathf.Max(.1f, -Physics.gravity.y);
            float jumpSpeed = ballMovement != null ? ballMovement.JumpSpeed : 6f;
            float clearanceHeight = Mathf.Max(0f, bounds.max.y) + .08f;
            float verticalBudget = jumpSpeed * jumpSpeed - 2f * gravity * clearanceHeight;
            if (verticalBudget <= 0f)
                return false;
            float clearTravel = 2f * Mathf.Sqrt(verticalBudget) / gravity * difficulty.ForwardSpeed;
            return clearTravel >= bounds.size.z + 2f * Radius + .15f;
        }

        private EncounterPlan BuildPattern(ObstaclePattern pattern, TrackSegment road, TrackSegment gap, int index, long identity, DifficultySnapshot difficulty, float origin, float centre, float limit)
        {
            if (pattern.Stages.Length == 0 || pattern.Placements.Length == 0)
                return default;
            float shift = pattern.UsesGap ? 0f : centre - pattern.Stages[0].centreZ;
            var placements = new Placement[pattern.Placements.Length];
            var starts = new float[pattern.Stages.Length];
            var ends = new float[starts.Length];
            var nav = new bool[starts.Length];
            var jump = new bool[starts.Length];
            for (int stage = 0; stage < starts.Length; stage++)
            {
                starts[stage] = float.PositiveInfinity;
                ends[stage] = float.NegativeInfinity;
            }

            for (int i = 0; i < placements.Length; i++)
            {
                var authored = pattern.Placements[i];
                int stage = authored.stageIndex;
                if (stage < 0 || stage >= starts.Length || authored.prefab == null || !TryGetFootprint(authored.prefab, out Bounds bounds))
                    return default;
                float x = authored.centre.x - bounds.center.x;
                float z = authored.centre.y + shift - bounds.center.z;
                if (!authored.requiredJump)
                {
                    if (x + bounds.min.x < -4.505f || x + bounds.max.x > 4.505f)
                        return default;
                    float passage = pattern.Stages[stage].passageX;
                    if (passage + Radius + PassagePadding > x + bounds.min.x && passage - Radius - PassagePadding < x + bounds.max.x)
                        return default;
                    nav[stage] = true;
                }
                else
                {
                    if (x + bounds.min.x < -4.505f || x + bounds.max.x > 4.505f || !CanClearJump(bounds, difficulty))
                        return default;
                    jump[stage] = true;
                }
                starts[stage] = Mathf.Min(starts[stage], origin + z + bounds.min.z);
                ends[stage] = Mathf.Max(ends[stage], origin + z + bounds.max.z);
                placements[i] = new Placement(authored.prefab, new Vector3(x, 0f, z), authored.requiredJump, taskIndex: stage * 2 + (authored.requiredJump ? 1 : 0));
            }

            if (pattern.UsesGap)
            {
                int stage = pattern.GapStageIndex;
                if (gap == null || stage < 0 || stage >= starts.Length)
                    return default;
                starts[stage] = origin + GapTakeoffZ(gap);
                ends[stage] = origin + GapLandingZ(gap);
                jump[stage] = true;
            }

            var windows = new List<TaskWindow>();
            float previousEnd = float.NegativeInfinity, previousX = pattern.Stages[0].passageX;
            for (int stage = 0; stage < starts.Length; stage++)
            {
                float x = pattern.Stages[stage].passageX;
                if (!difficulty.Corridor.Contains(x, Radius) || float.IsInfinity(starts[stage]) || starts[stage] < origin + .5f)
                    return default;
                float landing = ends[stage] + Radius + .15f;
                float deadline = landing + (jump[stage] && !(pattern.UsesGap && stage == pattern.GapStageIndex) ? JumpHalfTravel(difficulty) + .5f : 1f);
                if (pattern.UsesGap && stage == pattern.GapStageIndex)
                    deadline = Mathf.Max(landing + 2f, starts[stage] + 2f * JumpHalfTravel(difficulty) + Radius + .5f);
                float needed = difficulty.ForwardSpeed * EncounterPlanner.SteeringTime(previousX, x, LateralSpeed, Acceleration);
                if (stage > 0 && starts[stage] - previousEnd < Mathf.Max(.5f, needed))
                    return default;
                if (nav[stage])
                    windows.Add(new TaskWindow { LocalIndex = stage * 2, EntryZ = starts[stage] - 12f, HazardStartZ = starts[stage], LandingStartZ = landing, DeadlineZ = deadline });
                if (jump[stage])
                    windows.Add(new TaskWindow { LocalIndex = stage * 2 + 1, EntryZ = starts[stage] - 12f, HazardStartZ = starts[stage], LandingStartZ = landing, DeadlineZ = deadline });
                previousEnd = deadline;
                previousX = x;
            }

            if (previousEnd > limit || !planner.CanReach(index, starts[0], previousEnd, pattern.Stages[0].passageX, difficulty, LateralSpeed, Acceleration))
                return default;
            foreach (TaskWindow window in windows)
                window.RecoveryEndZ = previousEnd;
            return new EncounterPlan(this, runVersion, index, pattern.UsesGap ? gap : road, placements, pattern.UsesGap, previousEnd, previousX, 9f, identity, pattern.Family, windows.ToArray(), pattern.GapStageIndex * 2 + 1);
        }
    }
}
