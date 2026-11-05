using System;

namespace EndlessRollball
{
    // Pure timing policy shared by the scheduler, hazards and numeric checks.
    public static class PhoenixLavaMath
    {
        public static bool TryEntry(float frontierZ, int frontierIndex, float playerZ,
            float elapsed, float speed, float earliest, float target, float latest,
            float takeoffDistance, out int firstIndex, out float entryZ)
        {
            firstIndex = -1;
            entryZ = 0f;
            if (speed <= 0f || earliest < 0f || latest < earliest || elapsed > latest)
                return false;
            float desired = playerZ + speed * (target - elapsed) + takeoffDistance;
            int offset = Math.Max(3, (int)Math.Round((desired - frontierZ) / 24f,
                MidpointRounding.AwayFromZero));
            float candidate = frontierZ + offset * 24f;
            float predicted = elapsed + (candidate - takeoffDistance - playerZ) / speed;
            if (predicted < earliest || predicted > latest) return false;
            firstIndex = frontierIndex + offset;
            entryZ = candidate;
            return true;
        }

        public static float SecondsUntilFlight(float elapsed, float earliest) => Math.Max(0f, earliest - elapsed);

        public static float JumpLift(float progress, float height)
        {
            float t = Math.Max(0f, Math.Min(1f, progress));
            return 4f * height * t * (1f - t);
        }

        public static float EruptionHeight(float time, float encounter, float height)
        {
            float age = time - (encounter - .4f);
            if (age < 0f || age >= 1.25f) return 0f;
            return height * Math.Max(0f, Math.Min(1f, Math.Min(age / .2f, (1.25f - age) / .2f)));
        }
    }
}
