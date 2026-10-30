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
            {
                EnvironmentTheme.Street,
                EnvironmentTheme.Water,
                EnvironmentTheme.Dungeon
            }

            )
            {
                EnvironmentThemeConfig settings = GetSettings(theme);
                TrackSegment road = settings != null ? settings.roadTrackPrefab : null;
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
            EnvironmentJourneyPlanner.ScenerySlot slot = GetScenerySlot(segmentIndex);
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
            {
                EnvironmentTheme.Street,
                EnvironmentTheme.Water,
                EnvironmentTheme.Dungeon
            }

            )
            {
                EnvironmentThemeConfig settings = GetSettings(theme);
                if (!roads.Add(settings.roadTrackPrefab))
                {
                    reason = $"Assign a distinct clear road for {theme}.";
                    return false;
                }

                foreach (TrackSegment gap in new[]
                {
                    settings.gapTrackPrefab,
                    settings.normalGapTrackPrefab,
                    settings.hardGapTrackPrefab
                }

                )
                {
                    if (gap == null || !gaps.Add(gap))
                    {
                        reason = $"Assign distinct Easy, Normal and Hard gap variants for {theme}.";
                        return false;
                    }

                    if (!gap.IsContractValid(out reason))
                    {
                        reason = $"{theme} gap: {reason}";
                        return false;
                    }

                    if (!gap.Supports(difficulty) || gap.Kind != TrackSegmentKind.Gap || !gap.RequiredJump || gap.EntryLanes != TrackLaneMask.All || gap.SafeLanes != TrackLaneMask.All || gap.transform.localScale != Vector3.one || gap.transform.localPosition != Vector3.zero || gap.transform.localRotation != Quaternion.identity)
                    {
                        reason = $"{theme} gap must be an identity-transform, all-lanes, required-jump Gap track supporting {difficulty}.";
                        return false;
                    }
                }
            }

            reason = string.Empty;
            return true;
        }

        // Resolve using the generated road, not the player's current theme.
        internal ThemeObstacleLibrary GetObstacleLibraryForRoad(
            TrackSegment clearRoad)
        {
            if (clearRoad == null)
                return null;

            if (street != null && street.roadTrackPrefab == clearRoad)
                return street.ObstacleLibrary;

            if (beach != null && beach.roadTrackPrefab == clearRoad)
                return beach.ObstacleLibrary;

            if (dungeon != null && dungeon.roadTrackPrefab == clearRoad)
                return dungeon.ObstacleLibrary;

            return null;
        }

        // The generator has already resolved the scheduled theme's clear road.
        // Map that exact prefab, never CurrentTheme (which follows the player).
        internal TrackSegment GetGapTrackForRoad(TrackSegment clearRoad, DifficultySnapshot difficulty)
        {
            if (street != null && street.roadTrackPrefab == clearRoad)
                return street.GapFor(difficulty);
            if (beach != null && beach.roadTrackPrefab == clearRoad)
                return beach.GapFor(difficulty);
            if (dungeon != null && dungeon.roadTrackPrefab == clearRoad)
                return dungeon.GapFor(difficulty);
            return null;
        }

        internal void RecordRouteEntry(int index, float entry, float exit) => journey.RecordEntry(index, entry, exit);

        internal int EncounterZoneAt(int index) => GetScenerySlot(index).ZoneId;

        internal bool CanUseJunctionAt(int index)
        {
            return CanUseCityTrafficAt(index) &&
                TryGetScenery(EnvironmentTheme.Street, out var set) && set.junctionScenery != null;
        }

        internal bool CanUseCityTrafficAt(int index)
        {
            if (!sceneryReady || index < 1)
                return false;
            for (int offset = -1; offset <= 1; offset++)
            {
                int candidate = index + offset;
                var slot = GetScenerySlot(candidate);
                if (slot.Theme != EnvironmentTheme.Street || slot.IsTunnel || RequiresClearTrack(candidate))
                    return false;
            }
            return true;
        }

        internal bool CanUseBeachBarrierAt(int index)
        {
            if (!sceneryReady || index < 4) return false;
            for(int offset=0; offset<=1; offset++)
            {
                int candidate=index+offset;
                var slot=GetScenerySlot(candidate);
                if(slot.Theme != EnvironmentTheme.Water || slot.IsTunnel || RequiresClearTrack(candidate)) return false;
            }
            return true;
        }

        internal bool CanUseRoadsideStopAt(int index) => CanUseCityTrafficAt(index);

        internal bool IsStreetRoadSegment(int index)
        {
            if (!sceneryReady || index < 0) return false;
            var slot = GetScenerySlot(index);
            return slot.Theme == EnvironmentTheme.Street && !slot.IsTunnel;
        }

        internal bool IsDungeonRoadSegment(int index)
        {
            if(!sceneryReady || index<0) return false;
            var slot=GetScenerySlot(index);
            return slot.Theme==EnvironmentTheme.Dungeon && !slot.IsTunnel;
        }

        internal bool IsCityTrafficApproach(int index)
        {
            if (!CanUseCityTrafficAt(index) || !LongitudinalTrafficEncounter.IsApproachSegment(index))
                return false;
            ThemeObstacleLibrary library = street != null ? street.ObstacleLibrary : null;
            return library != null && library.LongitudinalTrafficTracks != null &&
                System.Array.Exists(library.LongitudinalTrafficTracks, candidate => candidate != null);
        }

        private static bool IsClearRoad(TrackSegment road)
        {
            return road.Kind == TrackSegmentKind.Empty && !road.RequiredJump && road.EntryLanes == TrackLaneMask.All && road.SafeLanes == TrackLaneMask.All;
        }
    }
}
