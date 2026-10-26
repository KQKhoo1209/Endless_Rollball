using System;

namespace EndlessRollball.Track
{
    internal static class FiniteRoutePlanner
    {
        internal readonly struct Plan
        {
            public Plan(int gate, int approach, int limit, float finish)
            {
                GateIndex = gate;
                ApproachIndex = approach;
                SegmentLimit = limit;
                FinishZ = finish;
            }

            public int GateIndex { get; }
            public int ApproachIndex { get; }
            public int SegmentLimit { get; }
            public float FinishZ { get; }
        }

        internal static bool TryPlan(float origin, float playerStart, float speed, float duration, int finalZone, int minimumFinal, int approach, int runoff, out Plan plan, out string reason)
        {
            plan = default;
            reason = string.Empty;
            double gate = Math.Ceiling(((double)playerStart + speed * duration - origin) / TrackSegment.ContractLength - .5d);
            long minimum = (long)finalZone + minimumFinal + approach;
            if (double.IsNaN(gate) || double.IsInfinity(gate) || gate > (double)int.MaxValue - runoff - 1 || gate < minimum)
            {
                reason = "Duration cannot fit the configured journey and finish corridor within valid index limits.";
                return false;
            }

            float finish = (float)(origin + (gate + .5d) * TrackSegment.ContractLength);
            if (float.IsNaN(finish) || float.IsInfinity(finish))
            {
                reason = "Invalid finish position.";
                return false;
            }

            plan = new Plan((int)gate, (int)gate - approach, (int)gate + 1 + runoff, finish);
            return true;
        }
    }
}
