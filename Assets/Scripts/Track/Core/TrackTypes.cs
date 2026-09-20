using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    public enum DifficultyMode
    {
        Easy,
        Normal,
        Hard,
        Endless
    }

    [Flags]
    public enum DifficultyModeMask
    {
        None = 0,
        Easy = 1 << 0,
        Normal = 1 << 1,
        Hard = 1 << 2,
        Endless = 1 << 3,
        All = Easy | Normal | Hard | Endless
    }

    [Flags]
    public enum TrackLaneMask
    {
        None = 0,
        Left = 1 << 0,
        Centre = 1 << 1,
        Right = 1 << 2,
        All = Left | Centre | Right
    }

    public enum TrackSegmentKind
    {
        Empty,
        Narrow,
        Gap,
        Tunnel,
        StaticObstacle,
        MovingObstacle
    }

    public readonly struct TrackRunConfig
    {
        public TrackRunConfig(
            DifficultyMode mode,
            int seed,
            bool standardized,
            string trackIdentifier)
        {
            Mode = mode;
            Seed = seed;
            Standardized = standardized;
            TrackIdentifier = trackIdentifier ?? string.Empty;
        }

        public DifficultyMode Mode { get; }
        public int Seed { get; }
        public bool Standardized { get; }
        public string TrackIdentifier { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(TrackIdentifier);
    }

    public readonly struct DifficultySnapshot
    {
        public DifficultySnapshot(
            DifficultyMode mode,
            float forwardSpeed,
            float speedAdjustmentRange,
            float obstacleProbability,
            float movingObstacleSpeed,
            float progress01)
        {
            Mode = mode;
            ForwardSpeed = Mathf.Max(0f, forwardSpeed);
            SpeedAdjustmentRange = Mathf.Max(0f, speedAdjustmentRange);
            ObstacleProbability = Mathf.Clamp01(obstacleProbability);
            MovingObstacleSpeed = Mathf.Max(0f, movingObstacleSpeed);
            Progress01 = Mathf.Clamp01(progress01);
        }

        public DifficultyMode Mode { get; }
        public float ForwardSpeed { get; }
        public float SpeedAdjustmentRange { get; }
        public float ObstacleProbability { get; }
        public float MovingObstacleSpeed { get; }
        public float Progress01 { get; }
    }

    public static class TrackLaneMaskUtility
    {
        public static TrackLaneMask ExpandAdjacent(TrackLaneMask lanes)
        {
            TrackLaneMask expanded = lanes;

            if ((lanes & TrackLaneMask.Left) != 0)
            {
                expanded |= TrackLaneMask.Centre;
            }

            if ((lanes & TrackLaneMask.Centre) != 0)
            {
                expanded |= TrackLaneMask.Left | TrackLaneMask.Right;
            }

            if ((lanes & TrackLaneMask.Right) != 0)
            {
                expanded |= TrackLaneMask.Centre;
            }

            return expanded;
        }
    }

    public static class DifficultyModeMaskUtility
    {
        public static bool Allows(DifficultyModeMask mask, DifficultyMode mode)
        {
            DifficultyModeMask required = mode switch
            {
                DifficultyMode.Easy => DifficultyModeMask.Easy,
                DifficultyMode.Normal => DifficultyModeMask.Normal,
                DifficultyMode.Hard => DifficultyModeMask.Hard,
                DifficultyMode.Endless => DifficultyModeMask.Endless,
                _ => DifficultyModeMask.None
            };

            return (mask & required) != 0;
        }
    }
}
