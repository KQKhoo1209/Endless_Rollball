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
            public SegmentPlan(TrackSegment track, List<EncounterPlan> encounters)
            {
                Track = track;
                Encounters = encounters;
            }

            public TrackSegment Track { get; }
            public List<EncounterPlan> Encounters { get; }
            public bool IsGap => Track != null && Track.RequiredJump;

            public float EndZ
            {
                get
                {
                    float end = float.NegativeInfinity;
                    if (Encounters != null)
                        foreach (EncounterPlan plan in Encounters)
                            end = Mathf.Max(end, plan.EndZ);
                    return end;
                }
            }
        }

        internal SegmentPlan PlanSegment(TrackSegment road, TrackSegment gap, int index, bool blocked, DifficultySnapshot difficulty, float originZ, float reservationLimit)
        {
            if (!ready || !isActiveAndEnabled || road == null || blocked || index < 0 || road.Kind != TrackSegmentKind.Empty)
                return default;
            var encounters = new List<EncounterPlan>(3);
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
                EncounterPlan plan = ChooseEncounter(road, slot == 0 ? gap : null, index, identity, difficulty, originZ, centre, reservationLimit, random);
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
        internal EncounterPlan PlanEncounter(TrackSegment road, TrackSegment gap, int index, bool blocked, DifficultySnapshot difficulty, float originZ)
        {
            if (!ready || blocked || road == null || road.Kind != TrackSegmentKind.Empty)
                return default;
            long identity = (long)Math.Round((originZ + 12f) * 1000d);
            var random = new System.Random(unchecked(runSeed ^ (int)identity ^ (int)(identity >> 32)));
            if (!planner.Roll(index, EncounterPlanner.Probability(difficulty), difficulty, random))
                return default;
            EncounterPlan plan = ChooseEncounter(road, gap, index, identity, difficulty, originZ, 12f, originZ + 23f, random);
            if (plan.HasEncounter)
                planner.Commit(index, plan.EndZ, plan.ExitX, plan.Family);
            return plan;
        }

        internal void AttachSegment(TrackSegment segment, SegmentPlan plan)
        {
            if (plan.Encounters != null)
                foreach (EncounterPlan encounter in plan.Encounters)
                    AttachPlanned(segment, encounter);
        }

        private EncounterPlan ChooseEncounter(TrackSegment road, TrackSegment gap, int index, long identity, DifficultySnapshot difficulty, float origin, float centre, float limit, System.Random random)
        {
            if (combinationPatterns.Length > 0 && random.NextDouble() < difficulty.CombinationChance)
            {
                int offset = random.Next(combinationPatterns.Length);
                for (int attempt = 0; attempt < combinationPatterns.Length; attempt++)
                {
                    ObstaclePattern pattern = combinationPatterns[(offset + attempt) % combinationPatterns.Length];
                    if (pattern == null || pattern.Family == planner.LastFamily || (pattern.UsesGap && gap == null))
                        continue;
                    EncounterPlan result = BuildPattern(pattern, road, gap, index, identity, difficulty, origin, centre, limit);
                    if (result.HasEncounter)
                        return result;
                }
            }

            bool jumping = runJump.Length > 0 && random.NextDouble() < jumpFraction;
            if (jumping && gap != null && GapTracksEnabled && random.NextDouble() < runGapShare)
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
                    float x = 0f, exit = 0f;
                    if (!jumping && !moving)
                    {
                        if (bounds.size.x + 2f * Radius + .3f > 9f)
                            continue;
                        bool left = random.Next(2) == 0;
                        x = left ? -4.5f - bounds.min.x : 4.5f - bounds.max.x;
                        exit = left ? (x + bounds.max.x + 4.5f) * .5f : (-4.5f + x + bounds.min.x) * .5f;
                    }

                    if (moving && bounds.size.x + 2f * Radius + .3f > 9f)
                        continue;
                    float landingAllowance = jumping ? JumpHalfTravel(difficulty) + .5f : 1f;
                    float placementCentre = Mathf.Min(centre, limit - origin - bounds.size.z * .5f - Radius - .15f - landingAllowance);
                    float first = origin + placementCentre - bounds.size.z * .5f;
                    float last = origin + placementCentre + bounds.size.z * .5f;
                    if (moving)
                    {
                        SideToSideObstacleMotion motion = prefab.GetComponentInChildren<SideToSideObstacleMotion>(true);
                        if (motion != null)
                        {
                            Bounds travel = motion.AuthoredTravelBounds;
                            first += Mathf.Min(0, travel.min.z);
                            last += Mathf.Max(0, travel.max.z);
                        }
                    }

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
                    var placement = new Placement(prefab, new Vector3(x, 0f, placementCentre - bounds.center.z), jumping, random.Next(2) == 0 ? .25f : .75f, moving ? difficulty.MovingObstacleSpeed : 0f, task);
                    return new EncounterPlan(this, runVersion, index, road, new[] { placement }, false, end, exit, 9f, identity, moving ? "Moving" : jumping ? "Hurdle" : "Fixed", new[] { window });
                }

                choices = runFixed;
                jumping = moving = false;
            }

            return default;
        }

        private float Radius => ballMovement != null ? ballMovement.SphereRadius : .5f;
        private float LateralSpeed => ballMovement != null ? ballMovement.MaxLateralSpeed : 7f;
        private float Acceleration => ballMovement != null ? ballMovement.SteeringAcceleration : 22f;

        private float JumpHalfTravel(DifficultySnapshot difficulty) => difficulty.ForwardSpeed * (ballMovement != null ? ballMovement.JumpSpeed : 6f) / Mathf.Max(.1f, -Physics.gravity.y);
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
                    if (passage + Radius + .08f > x + bounds.min.x && passage - Radius - .08f < x + bounds.max.x)
                        return default;
                    nav[stage] = true;
                }
                else
                    jump[stage] = true;
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
