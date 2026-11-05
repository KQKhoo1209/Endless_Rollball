using System;

namespace EndlessRollball
{
    public static class CloudLandingMath
    {
        public static bool IsInsideRoundedPlatform(float x, float z, float halfWidth,
            float halfDepth, float cornerRadius)
        {
            if (Math.Abs(x) > halfWidth || Math.Abs(z) > halfDepth) return false;
            float dx = Math.Max(0f, Math.Abs(x) - (halfWidth - cornerRadius));
            float dz = Math.Max(0f, Math.Abs(z) - (halfDepth - cornerRadius));
            return dx * dx + dz * dz <= cornerRadius * cornerRadius;
        }

        // Time tolerance only: admit floating-point drift at either end of a step,
        // without treating a ball well below the platform as a new landing.
        private const float TimeTolerance = .0001f;
        public static bool TryGetLandingTime(float height, float verticalSpeed,
            float gravity, float step, out float crossing)
        {
            crossing = 0f;
            if (height < -.001f || gravity <= 0f || step <= 0f) return false;
            float discriminant = verticalSpeed * verticalSpeed + 2f * gravity * height;
            if (discriminant < 0f) return false;
            float time = (verticalSpeed + (float)Math.Sqrt(discriminant)) / gravity;
            if (time < -TimeTolerance || time > step + TimeTolerance) return false;
            crossing = Math.Max(0f, Math.Min(step, time));
            return true;
        }
    }
}
