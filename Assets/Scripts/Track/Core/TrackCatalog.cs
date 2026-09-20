using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    [CreateAssetMenu(menuName = "Endless Rollball/Track/Track Catalog")]
    public sealed class TrackCatalog : ScriptableObject
    {
        [SerializeField] private TrackSegment fallbackSegment;
        [SerializeField] private TrackSegment[] segments = Array.Empty<TrackSegment>();

        public TrackSegment FallbackSegment => fallbackSegment;
        public IReadOnlyList<TrackSegment> Segments => segments;

        public List<TrackCandidate> BuildCandidates(DifficultyMode mode)
        {
            List<TrackCandidate> candidates = new List<TrackCandidate>(segments.Length);

            foreach (TrackSegment segment in segments)
            {
                if (segment == null || !segment.Supports(mode))
                {
                    continue;
                }

                candidates.Add(TrackCandidate.FromSegment(segment, mode));
            }

            return candidates;
        }

        public bool IsValid(out string reason)
        {
            if (fallbackSegment == null)
            {
                reason = "A fallback segment is required.";
                return false;
            }

            if (!fallbackSegment.IsContractValid(out reason))
            {
                reason = $"Fallback segment is invalid: {reason}";
                return false;
            }

            if (segments.Length == 0)
            {
                reason = "At least one segment is required.";
                return false;
            }

            foreach (TrackSegment segment in segments)
            {
                if (segment == null || !segment.IsContractValid(out reason))
                {
                    reason = segment == null
                        ? "The catalog contains a missing segment."
                        : $"{segment.name}: {reason}";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        public void Configure(TrackSegment configuredFallback, TrackSegment[] configuredSegments)
        {
            fallbackSegment = configuredFallback;
            segments = configuredSegments ?? Array.Empty<TrackSegment>();
        }
    }
}
