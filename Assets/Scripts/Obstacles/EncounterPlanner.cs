using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    /// <summary>Distance-based encounter reservations, independent of pooled segment instances.</summary>
    internal sealed class EncounterPlanner
    {
        private float previousEnd = float.NegativeInfinity;
        private float previousExitX;
        private int lastSegment = -2, consecutive;
        public string LastFamily { get; private set; }

        public void Reset()
        {
            previousEnd = float.NegativeInfinity;
            previousExitX = 0f;
            lastSegment = -2;
            consecutive = 0;
            LastFamily = null;
        }

        public bool Roll(int segment, float probability, DifficultySnapshot difficulty, System.Random random)
        {
            bool gentle = difficulty.Mode == DifficultyMode.Easy || (difficulty.Mode == DifficultyMode.Endless && difficulty.Progress01 < .5f);
            if (segment > lastSegment + 1)
                consecutive = 0;
            if (gentle && (segment <= lastSegment || consecutive >= difficulty.ConsecutiveLimit))
                return false;
            return random.NextDouble() < probability;
        }

        public static float SteeringTime(float fromX, float toX, float lateralSpeed, float acceleration)
        {
            float distance = Mathf.Abs(toX - fromX);
            if (distance < .05f)
                return 0f;
            float a = Mathf.Max(1f, acceleration), v = Mathf.Max(1f, lateralSpeed);
            return (distance <= v * v / a ? 2f * Mathf.Sqrt(distance / a) : distance / v + v / a) + .15f;
        }

        public bool CanReach(int segment, float startZ, float endZ, float entryX, DifficultySnapshot difficulty, float lateralSpeed, float acceleration)
        {
            if (endZ < startZ)
                return false;
            float recovery = difficulty.RecoverySeconds;
            if (GameSessionSettings.SelectedMode == GameMode.FastPaced)
                recovery = Mathf.Max(.8f, recovery * .8f);
            float seconds = Mathf.Max(recovery, SteeringTime(previousExitX, entryX, lateralSpeed, acceleration));
            return float.IsNegativeInfinity(previousEnd) || startZ - previousEnd >= difficulty.ForwardSpeed * seconds;
        }

        public void Commit(int segment, float endZ, float exitX, string family = null)
        {
            if (segment != lastSegment)
                consecutive = segment == lastSegment + 1 ? consecutive + 1 : 1;
            lastSegment = segment;
            previousEnd = endZ;
            previousExitX = exitX;
            if (!string.IsNullOrEmpty(family))
                LastFamily = family;
        }

        public float PreferredOppositeSide => previousExitX >= 0f ? -1f : 1f;

        public static float Probability(DifficultySnapshot difficulty) => Mathf.Clamp01(difficulty.ObstacleProbability + (GameSessionSettings.SelectedMode == GameMode.FastPaced ? .1f : 0f));
    }
}
