using System;

namespace EndlessRollball
{
    // Pure timeline simulation, shared by the playable controller and regression tests.
    // Landings split exactly at chart timestamps, independent of frame/physics frequency.
    public sealed class CloudMusicSimulation
    {
        private readonly CloudMusicChart chart;
        private readonly float halfWidth, halfDepth, cornerRadius, recoveryDepth;
        private double time;
        private float x, height, fallSpeed, fallGravity;
        private int next = 1;
        public double Time => time;
        public float X => x;
        public float Z => (float)Math.Min(time, chart.PlayableEnd) * chart.forwardSpeed;
        public float Height => height;
        public int Next => next;
        public int Landings { get; private set; }
        public int Recoveries { get; private set; }
        public bool Falling { get; private set; }
        public bool Complete => time >= chart.duration;
        public event Action<int> Landed;
        public event Action<int> Missed;

        public CloudMusicSimulation(CloudMusicChart chart, float halfWidth, float halfDepth,
            float cornerRadius, float recoveryDepth)
        {
            chart.Validate();
            this.chart = chart;
            this.halfWidth = halfWidth;
            this.halfDepth = halfDepth;
            this.cornerRadius = cornerRadius;
            this.recoveryDepth = recoveryDepth;
        }

        public void AdvanceTo(double targetTime, float steering)
        {
            targetTime = Math.Max(time, Math.Min(chart.duration, targetTime));
            steering = Math.Max(-1f, Math.Min(1f, steering));
            while (time < targetTime)
            {
                if (!Falling && next > chart.LastPlayableIndex)
                {
                    time = targetTime;
                    height = 0f;
                    return;
                }
                double until = Math.Min(targetTime, time + .02);
                if (!Falling) until = Math.Min(until, chart.times[next]);
                float step = (float)(until - time);
                float limit = chart.laneSpacing + halfWidth;
                x = Math.Max(-limit, Math.Min(limit, x + steering * chart.steeringSpeed * step));
                time = until;
                if (Falling)
                {
                    height += fallSpeed * step - .5f * fallGravity * step * step;
                    fallSpeed -= fallGravity * step;
                    if (height < -recoveryDepth)
                    {
                        next = chart.NextTarget(time);
                        // Resume the existing musical arc over the next safe target.
                        // Music time never jumps backwards/forwards to accommodate a miss.
                        x = chart.lanes[Math.Min(next, chart.LastPlayableIndex)] * chart.laneSpacing;
                        Falling = false;
                        Recoveries++;
                        if (next > chart.LastPlayableIndex) height = 0f;
                        else SampleArc();
                    }
                    continue;
                }
                SampleArc();
                if (time < chart.times[next] - .00000001) continue;
                float targetX = chart.lanes[next] * chart.laneSpacing;
                float landingScale = next == chart.LastPlayableIndex ? 1.5f : 1f;
                if (CloudLandingMath.IsInsideRoundedPlatform(x - targetX, 0f,
                    halfWidth * landingScale, halfDepth * landingScale, cornerRadius * landingScale))
                {
                    Landings++;
                    Landed?.Invoke(next);
                    next++;
                }
                else
                {
                    float interval = chart.times[next] - chart.times[next - 1];
                    fallSpeed = -4f * chart.bounceHeight / interval;
                    fallGravity = 8f * chart.bounceHeight / (interval * interval);
                    Falling = true;
                    Missed?.Invoke(next);
                }
            }
        }

        private void SampleArc()
        {
            float interval = chart.times[next] - chart.times[next - 1];
            float progress = (float)((time - chart.times[next - 1]) / interval);
            progress = Math.Max(0f, Math.Min(1f, progress));
            height = 4f * chart.bounceHeight * progress * (1f - progress);
        }
    }
}
