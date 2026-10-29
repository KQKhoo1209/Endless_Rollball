using System;
using System.Collections.Generic;
using System.Globalization;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball.Scenery
{
    internal sealed class EnvironmentJourneyPlanner
    {
        private readonly SortedDictionary<int, float> physicalEntries = new SortedDictionary<int, float>();
        internal void RecordEntry(int index, float entry, float exit)
        {
            physicalEntries[index] = entry;
            physicalEntries[index + 1] = exit;
        }
        private EnvironmentTheme CurrentTheme;
        private int standardEasyStreetSegments, standardThreeThemeSegmentsPerZone, endlessSegmentsPerZone;
        public string Error { get; private set; }

        private enum JourneyKind
        {
            None,
            StandardEasy,
            StandardThree,
            Endless
        }

        internal readonly struct ScenerySlot
        {
            public ScenerySlot(int zoneId, EnvironmentTheme theme, bool isTunnel)
            {
                ZoneId = zoneId;
                Theme = theme;
                IsTunnel = isTunnel;
            }

            public int ZoneId { get; }
            public EnvironmentTheme Theme { get; }
            public bool IsTunnel { get; }
        }

        private JourneyKind journeyKind;
        private int streetSegmentsForRun;
        private int zoneSegmentsForRun;
        private float journeyOriginZ;
        private string journeyIdentifierForRun;
        private System.Random journeyRandom;
        private readonly List<EnvironmentTheme> standardOrder = new List<EnvironmentTheme>();
        private readonly Dictionary<int, EnvironmentTheme> endlessThemes = new Dictionary<int, EnvironmentTheme>();
        private int lastPlannedEndlessZone;
        public bool HasScheduledJourney => journeyKind != JourneyKind.None;
        // With valid scenery configuration, connectors are owned by
        // the schedule. Review/Fast-Paced instead have no connectors.
        public bool SuppressRandomTunnels => true;
        public string JourneyIdentifier => journeyIdentifierForRun ?? string.Empty;

        public int FinalFiniteZoneStartIndex
        {
            get
            {
                if (!HasScheduledJourney)
                    return 0;
                long finalIndex;
                if (journeyKind == JourneyKind.StandardEasy)
                    finalIndex = (long)streetSegmentsForRun + 1;
                else if (journeyKind == JourneyKind.StandardThree)
                    finalIndex = 2L * ((long)zoneSegmentsForRun + 1);
                else
                    return -1; // Endless has no final zone.
                return finalIndex <= int.MaxValue ? (int)finalIndex : -1;
            }
        }

        public bool IsTransitionSegment(int segmentIndex)
        {
            if (!HasScheduledJourney || segmentIndex < 0)
                return false;
            if (journeyKind == JourneyKind.StandardEasy)
                return segmentIndex == streetSegmentsForRun;
            if (journeyKind == JourneyKind.StandardThree)
                return segmentIndex == zoneSegmentsForRun || segmentIndex == 2 * zoneSegmentsForRun + 1;
            return segmentIndex % (zoneSegmentsForRun + 1) == zoneSegmentsForRun;
        }

        public bool RequiresClearTrack(int segmentIndex)
        {
            if (!HasScheduledJourney || segmentIndex < 0)
                return false;
            if (IsTransitionSegment(segmentIndex))
                return true;
            if (journeyKind == JourneyKind.StandardEasy)
                return segmentIndex == streetSegmentsForRun + 1;
            if (journeyKind == JourneyKind.StandardThree)
                return segmentIndex == zoneSegmentsForRun + 1 || segmentIndex == 2 * (zoneSegmentsForRun + 1);
            return segmentIndex > 0 && segmentIndex % (zoneSegmentsForRun + 1) == 0;
        }

        internal static EnvironmentTheme SelectStartingTheme(GameMode mode, DifficultyMode difficulty, int seed)
        {
            if (mode == GameMode.Standard)
                return difficulty == DifficultyMode.Hard ? EnvironmentTheme.Dungeon : EnvironmentTheme.Street;
            var random = new System.Random(unchecked(seed ^ 0x2468ACE));
            return mode == GameMode.Endless ? (EnvironmentTheme)random.Next(3) :
                FastPacedThemeAtRoll(difficulty, random.Next(100));
        }

        internal static EnvironmentTheme FastPacedThemeAtRoll(DifficultyMode difficulty, int roll)
        {
            if (roll < 0 || roll >= 100) throw new ArgumentOutOfRangeException(nameof(roll));
            switch (difficulty)
            {
                case DifficultyMode.Easy:
                    return roll < 65 ? EnvironmentTheme.Street : EnvironmentTheme.Water;
                case DifficultyMode.Normal:
                    return roll < 45 ? EnvironmentTheme.Water : roll < 75 ? EnvironmentTheme.Street : EnvironmentTheme.Dungeon;
                case DifficultyMode.Hard:
                    return roll < 45 ? EnvironmentTheme.Dungeon : roll < 75 ? EnvironmentTheme.Water : EnvironmentTheme.Street;
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }

        public bool ConfigureJourney(GameMode mode, DifficultyMode difficulty, int seed, float trackOriginZ, EnvironmentTheme startingTheme, int easySegments, int standardSegments, int endlessSegments)
        {
            ResetJourneyPlan();
            CurrentTheme = startingTheme;
            standardEasyStreetSegments = easySegments;
            standardThreeThemeSegmentsPerZone = standardSegments;
            endlessSegmentsPerZone = endlessSegments;
            journeyOriginZ = trackOriginZ;
            journeyRandom = new System.Random(unchecked(seed ^ 0x5EED123));
            if (float.IsNaN(trackOriginZ) || float.IsInfinity(trackOriginZ))
                return JourneyWarning("Track origin must be finite.");
            if ((mode == GameMode.Endless) != (difficulty == DifficultyMode.Endless))
                return JourneyWarning("Endless mode and the Endless difficulty profile " + "must be selected together.");
            if (mode == GameMode.FastPaced)
            {
                journeyIdentifierForRun = $"fastpaced-env-v2-weighted-no-tunnel-z{Number(trackOriginZ)}";
                return true;
            }

            if (mode == GameMode.Standard && difficulty == DifficultyMode.Easy)
            {
                if (standardEasyStreetSegments < 1)
                    return JourneyWarning("Standard Easy needs at least one Street slot.");
                journeyKind = JourneyKind.StandardEasy;
                streetSegmentsForRun = standardEasyStreetSegments;
                standardOrder.Add(EnvironmentTheme.Street);
                standardOrder.Add(EnvironmentTheme.Water);
                journeyIdentifierForRun = $"standard-easy-env-v2-s{streetSegmentsForRun}" + $"-t1-r1-z{Number(trackOriginZ)}";
            }
            else if (mode == GameMode.Standard)
            {
                if (standardThreeThemeSegmentsPerZone < 4)
                    return JourneyWarning("Normal/Hard zones need at least four slots.");
                journeyKind = JourneyKind.StandardThree;
                zoneSegmentsForRun = standardThreeThemeSegmentsPerZone;
                standardOrder.AddRange(difficulty == DifficultyMode.Hard
                    ? new[] { EnvironmentTheme.Dungeon, EnvironmentTheme.Water, EnvironmentTheme.Street }
                    : new[] { EnvironmentTheme.Street, EnvironmentTheme.Water, EnvironmentTheme.Dungeon });

                journeyIdentifierForRun = $"standard-three-env-v2-fixed-n{zoneSegmentsForRun}" + $"-o{(int)standardOrder[0]}" + $"{(int)standardOrder[1]}{(int)standardOrder[2]}" + $"-t2-r1-z{Number(trackOriginZ)}";
            }
            else if (mode == GameMode.Endless)
            {
                if (endlessSegmentsPerZone < 4)
                    return JourneyWarning("Endless needs at least four slots per zone.");
                journeyKind = JourneyKind.Endless;
                zoneSegmentsForRun = endlessSegmentsPerZone;
                endlessThemes.Add(0, CurrentTheme);
                lastPlannedEndlessZone = 0;
                journeyIdentifierForRun = $"endless-env-v2-equal3-n{zoneSegmentsForRun}" +
                    $"-r1-z{Number(trackOriginZ)}";
            }
            else
            {
                return JourneyWarning("Unsupported game mode.");
            }

            return true;
        }

        internal int IndexAtWorldZ(float z)
        {
            int index = 0; float entry = journeyOriginZ;
            foreach (var pair in physicalEntries)
            {
                if (pair.Value > z) break;
                index = pair.Key; entry = pair.Value;
            }
            return index + Mathf.Max(0, Mathf.CeilToInt(z - entry));
        }

        internal ScenerySlot GetScenerySlot(int segmentIndex)
        {
            if (!HasScheduledJourney || segmentIndex < 0)
                return new ScenerySlot(0, CurrentTheme, false);
            if (journeyKind == JourneyKind.StandardEasy)
            {
                int zone = segmentIndex > streetSegmentsForRun ? 1 : 0;
                return new ScenerySlot(zone, standardOrder[zone], segmentIndex == streetSegmentsForRun);
            }

            if (journeyKind == JourneyKind.StandardThree)
            {
                int period = zoneSegmentsForRun + 1;
                int zone = Mathf.Min(segmentIndex / period, 2);
                return new ScenerySlot(zone, standardOrder[zone], IsTransitionSegment(segmentIndex));
            }

            int endlessZone = segmentIndex / (zoneSegmentsForRun + 1);
            return new ScenerySlot(endlessZone, GetThemeForZone(endlessZone), IsTransitionSegment(segmentIndex));
        }

        internal EnvironmentTheme GetThemeForZone(int zone)
        {
            if (journeyKind != JourneyKind.Endless)
                return standardOrder[zone];
            while (lastPlannedEndlessZone < zone)
            {
                int nextZone = lastPlannedEndlessZone + 1;
                // Independent equal draws: all three themes are eligible immediately.
                EnvironmentTheme next = (EnvironmentTheme)journeyRandom.Next(3);
                endlessThemes.Add(nextZone, next);
                lastPlannedEndlessZone = nextZone;
            }

            if (!endlessThemes.TryGetValue(zone, out EnvironmentTheme theme))
                throw new InvalidOperationException("An old pruned zone was requested. Attach scheduled " + "tracks in forward spawn-index order.");
            return theme;
        }

        internal void PruneEndlessThemeHistory(int visibleZone)
        {
            if (journeyKind != JourneyKind.Endless)
                return;
            var obsolete = new List<int>();
            foreach (int zone in endlessThemes.Keys)
                if (zone < visibleZone - 1)
                    obsolete.Add(zone);
            foreach (int zone in obsolete)
                endlessThemes.Remove(zone);
        }

        private bool JourneyWarning(string message)
        {
            Error = message;
            return false;
        }

        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        internal void ResetJourneyPlan()
        {
            journeyKind = JourneyKind.None;
            streetSegmentsForRun = 0;
            zoneSegmentsForRun = 0;
            physicalEntries.Clear();
            journeyOriginZ = 0f;
            journeyIdentifierForRun = null;
            journeyRandom = null;
            standardOrder.Clear();
            endlessThemes.Clear();
            lastPlannedEndlessZone = 0;
        }
    }
}
