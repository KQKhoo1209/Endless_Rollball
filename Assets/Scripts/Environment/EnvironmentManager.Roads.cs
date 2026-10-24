using EndlessRollball.Track;

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
                    reason = $"{theme} needs a clear all-lanes road supporting {difficulty}; new obstacles are not configured yet.";
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

        private static bool IsClearRoad(TrackSegment road)
        {
            return road.Kind == TrackSegmentKind.Empty && !road.RequiredJump &&
                road.EntryLanes == TrackLaneMask.All && road.SafeLanes == TrackLaneMask.All;
        }
    }
}
