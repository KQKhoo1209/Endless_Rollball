using System;
namespace EndlessRollball.Track
{
    /// <summary>A physical straight-track record, independent of pooled instances.</summary>
    public readonly struct TrackRouteSpan
    {
        public readonly int Index;
        public readonly float EntryZ, RootZ, ExitZ;
        public TrackRouteSpan(int index, float entryZ, float entryLocalZ, float exitLocalZ)
        {
            float length = exitLocalZ - entryLocalZ;
            if (!float.IsFinite(entryZ) || !float.IsFinite(entryLocalZ) ||
                !float.IsFinite(exitLocalZ) || length <= 0f)
                throw new ArgumentException("Route anchors require a finite positive span.");
            Index = index; EntryZ = entryZ; RootZ = entryZ - entryLocalZ;
            ExitZ = RootZ + exitLocalZ;
        }
        public bool Contains(float z) => z >= EntryZ && z < ExitZ;
    }
}
