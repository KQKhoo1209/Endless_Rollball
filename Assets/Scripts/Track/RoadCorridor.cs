using UnityEngine;

namespace EndlessRollball.Track
{
    /// <summary>Shared physical corridor; widths describe the full usable road, not centre travel.</summary>
    public readonly struct RoadCorridor
    {
        public const float BoundarySkin = .02f;
        public RoadCorridor(float width, float centreX = 0f)
        {
            Width = Mathf.Max(1f, width);
            CentreX = centreX;
        }

        public float Width { get; }
        public float CentreX { get; }

        public float CentreLimit(float radius) => Mathf.Max(0f, Width * .5f - radius - BoundarySkin);
        public float Clamp(float x, float radius) => Mathf.Clamp(x, CentreX - CentreLimit(radius), CentreX + CentreLimit(radius));
        public bool Contains(float x, float radius) => Mathf.Abs(x - CentreX) <= CentreLimit(radius) + .025f;
    }
}
