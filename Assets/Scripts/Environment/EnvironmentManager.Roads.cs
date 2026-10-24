using System.Collections.Generic;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball.Scenery
{
    public sealed partial class EnvironmentManager
    {
        public bool TryValidatePlayableRoadTracks(DifficultyMode difficulty, out string reason)
        {
            if (!isActiveAndEnabled || !sceneryReady)
            {
                reason = "The EnvironmentManager must successfully initialize its scenery and journey first.";
                return false;
            }

            foreach (EnvironmentTheme theme in new[]
                { EnvironmentTheme.Street, EnvironmentTheme.Water, EnvironmentTheme.Dungeon })
            {
                TrackSegment road = GetSettings(theme).roadTrackPrefab;
                if (road == null)
                {
                    reason = $"Assign the complete playable road track for {theme}.";
                    return false;
                }
                if (!road.IsContractValid(out reason))
                {
                    reason = $"{theme} road: {reason}";
                    return false;
                }
                if (!road.Supports(difficulty) || !IsClearRoad(road))
                {
                    reason = $"{theme} needs a clear all-lanes road supporting {difficulty}. Keep the gap in its separate Gap Track Prefab field.";
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        public bool TryGetPlayableRoadTrack(int segmentIndex, out TrackSegment prefab)
        {
            prefab = null;
            if (!isActiveAndEnabled || !sceneryReady || segmentIndex < 0)
                return false;

            // A tunnel slot belongs to the approaching zone. Its regular road remains
            // continuous under the tunnel; the first exit slot uses the next theme.
            ScenerySlot slot = GetScenerySlot(segmentIndex);
            prefab = GetSettings(slot.Theme).roadTrackPrefab;
            return prefab != null && IsClearRoad(prefab) && prefab.IsContractValid(out _);
        }

        internal bool TryValidatePlayableGapTracks(DifficultyMode difficulty, out string reason)
        {
            if (!TryValidatePlayableRoadTracks(difficulty, out reason))
                return false;

            var roads = new HashSet<TrackSegment>();
            var gaps = new HashSet<TrackSegment>();
            foreach (EnvironmentTheme theme in new[]
                { EnvironmentTheme.Street, EnvironmentTheme.Water, EnvironmentTheme.Dungeon })
            {
                ThemeSettings settings = GetSettings(theme);
                TrackSegment gap = settings.gapTrackPrefab;
                if (gap == null || !roads.Add(settings.roadTrackPrefab) || !gaps.Add(gap))
                {
                    reason = $"Assign one distinct matching Gap Track Prefab and clear road for each theme ({theme}).";
                    return false;
                }
                if (!gap.IsContractValid(out reason))
                {
                    reason = $"{theme} gap: {reason}";
                    return false;
                }
                if (!gap.Supports(difficulty) || gap.Kind != TrackSegmentKind.Gap || !gap.RequiredJump ||
                    gap.EntryLanes != TrackLaneMask.All || gap.SafeLanes != TrackLaneMask.All ||
                    gap.transform.localScale != Vector3.one || gap.transform.localPosition != Vector3.zero ||
                    gap.transform.localRotation != Quaternion.identity)
                {
                    reason = $"{theme} gap must be an identity-transform, all-lanes, required-jump Gap track supporting {difficulty}.";
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        // The generator has already resolved the scheduled theme's clear road.
        // Map that exact prefab, never CurrentTheme (which follows the player).
        internal TrackSegment GetGapTrackForRoad(TrackSegment clearRoad)
        {
            foreach (ThemeSettings settings in new[] { street, beach, dungeon })
                if (settings.roadTrackPrefab == clearRoad)
                    return settings.gapTrackPrefab;
            return null;
        }

        private static bool IsClearRoad(TrackSegment road)
        {
            return road.Kind == TrackSegmentKind.Empty && !road.RequiredJump &&
                road.EntryLanes == TrackLaneMask.All && road.SafeLanes == TrackLaneMask.All;
        }
    }
}
