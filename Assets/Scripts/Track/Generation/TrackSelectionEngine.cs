using System;
using System.Collections.Generic;

namespace EndlessRollball.Track
{
    public readonly struct TrackCandidate
    {
        public TrackCandidate(
            string id,
            TrackSegment prefab,
            TrackSegmentKind kind,
            TrackLaneMask entryLanes,
            TrackLaneMask safeLanes,
            bool requiredJump,
            float weight)
        {
            Id = id;
            Prefab = prefab;
            Kind = kind;
            EntryLanes = entryLanes;
            SafeLanes = safeLanes;
            RequiredJump = requiredJump;
            Weight = Math.Max(0f, weight);
        }

        public string Id { get; }
        public TrackSegment Prefab { get; }
        public TrackSegmentKind Kind { get; }
        public TrackLaneMask EntryLanes { get; }
        public TrackLaneMask SafeLanes { get; }
        public bool RequiredJump { get; }
        public float Weight { get; }
        public bool IsObstacle => Kind is TrackSegmentKind.Gap
            or TrackSegmentKind.StaticObstacle
            or TrackSegmentKind.MovingObstacle;

        public static TrackCandidate FromSegment(TrackSegment segment, DifficultyMode mode)
        {
            return new TrackCandidate(
                segment.SegmentId,
                segment,
                segment.Kind,
                segment.EntryLanes,
                segment.SafeLanes,
                segment.RequiredJump,
                segment.GetWeight(mode));
        }
    }

    public readonly struct TrackSelectionState
    {
        public TrackSelectionState(TrackLaneMask reachableLanes, TrackSegmentKind previousKind)
        {
            ReachableLanes = reachableLanes;
            PreviousKind = previousKind;
        }

        public TrackLaneMask ReachableLanes { get; }
        public TrackSegmentKind PreviousKind { get; }

        public static TrackSelectionState Initial => new TrackSelectionState(
            TrackLaneMask.All,
            TrackSegmentKind.Empty);
    }

    public readonly struct TrackSelectionResult
    {
        public TrackSelectionResult(TrackCandidate candidate, TrackSelectionState nextState)
        {
            Candidate = candidate;
            NextState = nextState;
            Success = true;
        }

        public TrackCandidate Candidate { get; }
        public TrackSelectionState NextState { get; }
        public bool Success { get; }
    }

    public static class TrackSelectionEngine
    {
        public static TrackSelectionResult Select(
            IReadOnlyList<TrackCandidate> candidates,
            Random random,
            TrackSelectionState state,
            float obstacleProbability,
            bool forceSafeStart)
        {
            if (candidates == null || candidates.Count == 0 || random == null)
            {
                return default;
            }

            TrackLaneMask expandedReachable = TrackLaneMaskUtility.ExpandAdjacent(state.ReachableLanes);
            bool any = false;
            for (int i = 0; i < candidates.Count; i++)
                if (Eligible(candidates[i], state, expandedReachable, forceSafeStart)) { any = true; break; }
            if (!any) return default;

            bool preferObstacle = !forceSafeStart && random.NextDouble() < obstacleProbability;
            bool hasPreferred = false;
            for (int i = 0; i < candidates.Count; i++)
                if (Eligible(candidates[i], state, expandedReachable, forceSafeStart) &&
                    candidates[i].IsObstacle == preferObstacle) { hasPreferred = true; break; }
            double total = 0d;
            TrackCandidate selected = default;
            for (int i = 0; i < candidates.Count; i++)
            {
                TrackCandidate candidate = candidates[i];
                if (!Eligible(candidate, state, expandedReachable, forceSafeStart) ||
                    (hasPreferred && candidate.IsObstacle != preferObstacle)) continue;
                total += candidate.Weight;
                selected = candidate; // Same last-candidate fallback as the previous weighted selection.
            }
            double roll = random.NextDouble() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                TrackCandidate candidate = candidates[i];
                if (!Eligible(candidate, state, expandedReachable, forceSafeStart) ||
                    (hasPreferred && candidate.IsObstacle != preferObstacle)) continue;
                roll -= candidate.Weight;
                if (roll <= 0d) { selected = candidate; break; }
            }
            TrackLaneMask nextReachable = expandedReachable & selected.EntryLanes & selected.SafeLanes;

            return new TrackSelectionResult(
                selected,
                new TrackSelectionState(nextReachable, selected.Kind));
        }

        private static bool Eligible(TrackCandidate candidate, TrackSelectionState state,
            TrackLaneMask reachable, bool forceSafeStart)
        {
            return !(candidate.Weight <= 0f) &&
                !(state.PreviousKind == TrackSegmentKind.Gap && candidate.Kind == TrackSegmentKind.Gap) &&
                !(forceSafeStart && candidate.Kind is not TrackSegmentKind.Empty and not TrackSegmentKind.Tunnel) &&
                (reachable & candidate.EntryLanes & candidate.SafeLanes) != TrackLaneMask.None;
        }
    }
}
