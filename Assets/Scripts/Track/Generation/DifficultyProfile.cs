using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    [CreateAssetMenu(menuName = "Endless Rollball/Track/Difficulty Profile")]
    public sealed class DifficultyProfile : ScriptableObject
    {
        [SerializeField] private DifficultyMode mode;
        [SerializeField] private bool standardized;
        [SerializeField] private int fixedSeed = 24001;
        [SerializeField] private string trackIdentifier = "easy-v1-24001";

        [Header("Starting values")]
        [SerializeField, Min(0f)] private float forwardSpeed = 6f;
        [SerializeField, Min(0f)] private float speedAdjustmentRange = 3f;
        [SerializeField, Range(0f, 1f)] private float obstacleProbability = 0.25f;
        [SerializeField, Min(0f)] private float movingObstacleSpeed;

        [Header("Endless maximum values")]
        [SerializeField, Min(0f)] private float maximumForwardSpeed = 10f;
        [SerializeField, Range(0f, 1f)] private float maximumObstacleProbability = 0.7f;
        [SerializeField, Min(0f)] private float maximumMovingObstacleSpeed = 3f;
        [SerializeField, Min(1f)] private float distanceToMaximum = 1000f;

        public DifficultyMode Mode => mode;
        public bool Standardized => standardized;
        public string TrackIdentifier => trackIdentifier;

        public TrackRunConfig CreateRunConfig(int? seedOverride = null)
        {
            int seed = standardized
                ? fixedSeed
                : seedOverride ?? CreateRandomSeed();

            string identifier = standardized
                ? trackIdentifier
                : $"{mode.ToString().ToLowerInvariant()}-v1-{seed}";

            return new TrackRunConfig(mode, seed, standardized, identifier);
        }

        public DifficultySnapshot Evaluate(float distance)
        {
            float progress = mode == DifficultyMode.Endless
                ? Mathf.Clamp01(Mathf.Max(0f, distance) / distanceToMaximum)
                : 0f;

            return new DifficultySnapshot(
                mode,
                Mathf.Lerp(forwardSpeed, maximumForwardSpeed, progress),
                speedAdjustmentRange,
                Mathf.Lerp(obstacleProbability, maximumObstacleProbability, progress),
                Mathf.Lerp(movingObstacleSpeed, maximumMovingObstacleSpeed, progress),
                progress);
        }

        public void Configure(
            DifficultyMode configuredMode,
            bool isStandardized,
            int configuredSeed,
            string configuredIdentifier,
            float configuredForwardSpeed,
            float configuredSpeedRange,
            float configuredObstacleProbability,
            float configuredMovingSpeed,
            float configuredMaximumSpeed,
            float configuredMaximumObstacleProbability,
            float configuredMaximumMovingSpeed,
            float configuredDistanceToMaximum)
        {
            mode = configuredMode;
            standardized = isStandardized;
            fixedSeed = configuredSeed;
            trackIdentifier = configuredIdentifier;
            forwardSpeed = configuredForwardSpeed;
            speedAdjustmentRange = configuredSpeedRange;
            obstacleProbability = configuredObstacleProbability;
            movingObstacleSpeed = configuredMovingSpeed;
            maximumForwardSpeed = configuredMaximumSpeed;
            maximumObstacleProbability = configuredMaximumObstacleProbability;
            maximumMovingObstacleSpeed = configuredMaximumMovingSpeed;
            distanceToMaximum = Mathf.Max(1f, configuredDistanceToMaximum);
        }

        private static int CreateRandomSeed()
        {
            unchecked
            {
                return Environment.TickCount ^ DateTime.UtcNow.Ticks.GetHashCode();
            }
        }
    }
}
