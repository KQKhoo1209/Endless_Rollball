using System;
using System.Collections.Generic;
using System.Globalization;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball.Scenery
{
    internal sealed class EnvironmentJourneyPlanner
    {
        private EnvironmentTheme CurrentTheme;
        private int standardEasyStreetSegments, standardThreeThemeSegmentsPerZone, endlessSegmentsPerZone;
        private float endlessDungeonUnlockDistance;
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
        private float dungeonUnlockForRun;
        private float journeyOriginZ;
        private string journeyIdentifierForRun;
        private System.Random journeyRandom;
        private readonly List<EnvironmentTheme> standardOrder = new List<EnvironmentTheme>();
        private readonly Dictionary<int, EnvironmentTheme> endlessThemes = new Dictionary<int, EnvironmentTheme>();
        private readonly List<EnvironmentTheme> endlessThemeBag = new List<EnvironmentTheme>();
        private int endlessThemeBagIndex;
        private int endlessPoolSize;
        private int lastPlannedEndlessZone;
        private EnvironmentTheme lastPlannedEndlessTheme;
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

        public bool ConfigureJourney(GameMode mode, DifficultyMode difficulty, int seed, float trackOriginZ, EnvironmentTheme startingTheme, int easySegments, int standardSegments, int endlessSegments, float unlockDistance)
        {
            ResetJourneyPlan();
            CurrentTheme = startingTheme;
            standardEasyStreetSegments = easySegments;
            standardThreeThemeSegmentsPerZone = standardSegments;
            endlessSegmentsPerZone = endlessSegments;
            endlessDungeonUnlockDistance = unlockDistance;
            journeyOriginZ = trackOriginZ;
            journeyRandom = new System.Random(unchecked(seed ^ 0x5EED123));
            if (float.IsNaN(trackOriginZ) || float.IsInfinity(trackOriginZ))
                return JourneyWarning("Track origin must be finite.");
            if ((mode == GameMode.Endless) != (difficulty == DifficultyMode.Endless))
                return JourneyWarning("Endless mode and the Endless difficulty profile " + "must be selected together.");
            if (mode == GameMode.FastPaced)
            {
                journeyIdentifierForRun = $"fastpaced-env5d-v1-no-tunnel-z{Number(trackOriginZ)}";
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
                journeyIdentifierForRun = $"standard-easy-env5d-v1-s{streetSegmentsForRun}" + $"-t1-r1-z{Number(trackOriginZ)}";
            }
            else if (mode == GameMode.Standard)
            {
                if (standardThreeThemeSegmentsPerZone < 4)
                    return JourneyWarning("Normal/Hard zones need at least four slots.");
                journeyKind = JourneyKind.StandardThree;
                zoneSegmentsForRun = standardThreeThemeSegmentsPerZone;
                standardOrder.Add(CurrentTheme);
                foreach (EnvironmentTheme theme in new[]
                {
                    EnvironmentTheme.Street,
                    EnvironmentTheme.Water,
                    EnvironmentTheme.Dungeon
                }

                )
                {
                    if (theme != CurrentTheme)
                        standardOrder.Add(theme);
                }

                // StartingTheme already selected the first theme.
                if (journeyRandom.Next(2) == 1)
                {
                    EnvironmentTheme temporary = standardOrder[1];
                    standardOrder[1] = standardOrder[2];
                    standardOrder[2] = temporary;
                }

                journeyIdentifierForRun = $"standard-three-env5d-v1-n{zoneSegmentsForRun}" + $"-o{(int)standardOrder[0]}" + $"{(int)standardOrder[1]}{(int)standardOrder[2]}" + $"-t2-r1-z{Number(trackOriginZ)}";
            }
            else if (mode == GameMode.Endless)
            {
                if (endlessSegmentsPerZone < 4 || float.IsNaN(endlessDungeonUnlockDistance) || float.IsInfinity(endlessDungeonUnlockDistance) || endlessDungeonUnlockDistance < 0f)
                    return JourneyWarning("Endless needs at least four slots per zone " + "and a finite non-negative unlock distance.");
                journeyKind = JourneyKind.Endless;
                zoneSegmentsForRun = endlessSegmentsPerZone;
                dungeonUnlockForRun = endlessDungeonUnlockDistance;
                endlessThemes.Add(0, CurrentTheme);
                lastPlannedEndlessZone = 0;
                lastPlannedEndlessTheme = CurrentTheme;
                // Complete the initial two-theme cycle after its start.
                endlessPoolSize = 2;
                endlessThemeBag.Add(CurrentTheme == EnvironmentTheme.Street ? EnvironmentTheme.Water : EnvironmentTheme.Street);
                endlessThemeBagIndex = 0;
                journeyIdentifierForRun = $"endless-env5d-v1-n{zoneSegmentsForRun}" + $"-d{Number(dungeonUnlockForRun)}" + $"-initial2-r1-z{Number(trackOriginZ)}";
            }
            else
            {
                return JourneyWarning("Unsupported game mode.");
            }

            return true;
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
                double entranceZ = journeyOriginZ + (double)nextZone * (zoneSegmentsForRun + 1) * TrackSegment.ContractLength;
                int poolSize = nextZone >= 2 && entranceZ >= dungeonUnlockForRun ? 3 : 2;
                if (poolSize != endlessPoolSize || endlessThemeBagIndex >= endlessThemeBag.Count)
                    RefillEndlessThemeBag(poolSize);
                EnvironmentTheme next = endlessThemeBag[endlessThemeBagIndex++];
                endlessThemes.Add(nextZone, next);
                lastPlannedEndlessZone = nextZone;
                lastPlannedEndlessTheme = next;
            }

            if (!endlessThemes.TryGetValue(zone, out EnvironmentTheme theme))
                throw new InvalidOperationException("An old pruned zone was requested. Attach scheduled " + "tracks in forward spawn-index order.");
            return theme;
        }

        private void RefillEndlessThemeBag(int poolSize)
        {
            endlessPoolSize = poolSize;
            endlessThemeBag.Clear();
            endlessThemeBag.Add(EnvironmentTheme.Street);
            endlessThemeBag.Add(EnvironmentTheme.Water);
            if (poolSize == 3)
                endlessThemeBag.Add(EnvironmentTheme.Dungeon);
            for (int i = endlessThemeBag.Count - 1; i > 0; i--)
            {
                int other = journeyRandom.Next(i + 1);
                EnvironmentTheme temporary = endlessThemeBag[i];
                endlessThemeBag[i] = endlessThemeBag[other];
                endlessThemeBag[other] = temporary;
            }

            if (endlessThemeBag[0] == lastPlannedEndlessTheme)
            {
                EnvironmentTheme temporary = endlessThemeBag[0];
                endlessThemeBag[0] = endlessThemeBag[1];
                endlessThemeBag[1] = temporary;
            }

            endlessThemeBagIndex = 0;
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
            dungeonUnlockForRun = 0f;
            journeyOriginZ = 0f;
            journeyIdentifierForRun = null;
            journeyRandom = null;
            standardOrder.Clear();
            endlessThemes.Clear();
            endlessThemeBag.Clear();
            endlessThemeBagIndex = 0;
            endlessPoolSize = 0;
            lastPlannedEndlessZone = 0;
            lastPlannedEndlessTheme = EnvironmentTheme.Street;
        }
    }
}
