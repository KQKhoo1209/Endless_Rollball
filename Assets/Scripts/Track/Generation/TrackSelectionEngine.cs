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
            List<TrackCandidate> compatible = new List<TrackCandidate>(candidates.Count);

            foreach (TrackCandidate candidate in candidates)
            {
                if (candidate.Weight <= 0f)
                {
                    continue;
                }

                if (state.PreviousKind == TrackSegmentKind.Gap && candidate.Kind == TrackSegmentKind.Gap)
                {
                    continue;
                }

                if (forceSafeStart && candidate.Kind is not TrackSegmentKind.Empty and not TrackSegmentKind.Tunnel)
                {
                    continue;
                }

                TrackLaneMask reachable = expandedReachable & candidate.EntryLanes & candidate.SafeLanes;
                if (reachable != TrackLaneMask.None)
                {
                    compatible.Add(candidate);
                }
            }

            if (compatible.Count == 0)
            {
                return default;
            }

            bool preferObstacle = !forceSafeStart && random.NextDouble() < obstacleProbability;
            List<TrackCandidate> preferred = compatible.FindAll(candidate => candidate.IsObstacle == preferObstacle);
            IReadOnlyList<TrackCandidate> selectionPool = preferred.Count > 0 ? preferred : compatible;
            TrackCandidate selected = WeightedSelect(selectionPool, random);
            TrackLaneMask nextReachable = expandedReachable & selected.EntryLanes & selected.SafeLanes;

            return new TrackSelectionResult(
                selected,
                new TrackSelectionState(nextReachable, selected.Kind));
        }

        private static TrackCandidate WeightedSelect(IReadOnlyList<TrackCandidate> candidates, Random random)
        {
            double total = 0d;
            for (int index = 0; index < candidates.Count; index++)
            {
                total += candidates[index].Weight;
            }

            double roll = random.NextDouble() * total;
            for (int index = 0; index < candidates.Count; index++)
            {
                roll -= candidates[index].Weight;
                if (roll <= 0d)
                {
                    return candidates[index];
                }
            }

            return candidates[candidates.Count - 1];
        }
    }
}
