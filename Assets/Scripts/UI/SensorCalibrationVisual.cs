using UnityEngine;

/// <summary>Texture-free calibration reticle. All motion is supplied by real sensor updates.</summary>
[AddComponentMenu("Endless Rollball/UI/Sensor Calibration Visual")]
public sealed class SensorCalibrationVisual : UnityEngine.UI.MaskableGraphic
{
    private Vector2 tilt;
    private float rotation;
    private float progress;
    private bool connected;
    private bool stable;
    private bool complete;

    public void SetFeedback(Vector2 tiltInput, float rotationDegrees, float stableProgress,
        bool hasInput, bool isStable, bool calibrated)
    {
        if (tilt == tiltInput && rotation == rotationDegrees && progress == stableProgress &&
            connected == hasInput && stable == isStable && complete == calibrated) return;
        tilt = Vector2.ClampMagnitude(tiltInput, 1f);
        rotation = rotationDegrees;
        progress = Mathf.Clamp01(stableProgress);
        connected = hasInput;
        stable = isStable;
        complete = calibrated;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        Rect bounds = GetPixelAdjustedRect();
        Vector2 centre = bounds.center;
        Color waiting = new Color(1f, 1f, 1f, .7f);
        Color amber = new Color(1f, .72f, .25f, 1f);
        Color green = new Color(.36f, .91f, .66f, 1f);
        Color feedback = !connected ? waiting : stable || complete ? green : amber;
        Quad(mesh, bounds.min, bounds.max, new Color(.10f, .16f, .17f, .94f));

        // The neutral target and progress stay fixed; only the white cross rotates.
        Ring(mesh, centre, 23f, 2f, 1f, new Color(1f, 1f, 1f, .52f));
        Ring(mesh, centre, 35f, 4f, 1f, new Color(1f, 1f, 1f, .16f));
        if (connected) Ring(mesh, centre, 35f, 4f, progress, green);
        float radians = rotation * Mathf.Deg2Rad;
        Vector2 horizontal = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        Vector2 vertical = new Vector2(-horizontal.y, horizontal.x);
        Color cross = new Color(1f, 1f, 1f, connected ? .95f : .4f);
        CrossAxis(mesh, centre, horizontal, bounds, cross);
        CrossAxis(mesh, centre, vertical, bounds, cross);

        Vector2 point = centre + new Vector2(tilt.x * bounds.width * .32f, tilt.y * bounds.height * .32f);
        if (connected) Line(mesh, centre, point, 1.5f, new Color(feedback.r, feedback.g, feedback.b, .42f));
        Disc(mesh, point, 7f, feedback);
        if (complete)
        {
            Line(mesh, centre + new Vector2(-9, 0), centre + new Vector2(-2, -7), 3, green);
            Line(mesh, centre + new Vector2(-2, -7), centre + new Vector2(12, 10), 3, green);
        }
    }

    private static void CrossAxis(UnityEngine.UI.VertexHelper mesh, Vector2 centre,
        Vector2 direction, Rect bounds, Color tint)
    {
        // Clip each rotating arm to the rectangular visual area.
        float xLimit = (bounds.width * .5f - 16f) / Mathf.Max(.0001f, Mathf.Abs(direction.x));
        float yLimit = (bounds.height * .5f - 16f) / Mathf.Max(.0001f, Mathf.Abs(direction.y));
        float length = Mathf.Min(xLimit, yLimit);
        if (length <= 43f) return;
        Line(mesh, centre + direction * 43f, centre + direction * length, 2f, tint);
        Line(mesh, centre - direction * 43f, centre - direction * length, 2f, tint);
    }

    private static void Quad(UnityEngine.UI.VertexHelper mesh, Vector2 min, Vector2 max, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(min.x, min.y), tint, Vector2.zero);
        mesh.AddVert(new Vector3(min.x, max.y), tint, Vector2.zero);
        mesh.AddVert(new Vector3(max.x, max.y), tint, Vector2.zero);
        mesh.AddVert(new Vector3(max.x, min.y), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Line(UnityEngine.UI.VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
    {
        Vector2 offset = Vector2.Perpendicular(to - from).normalized * width * .5f;
        int start = mesh.currentVertCount;
        mesh.AddVert(from - offset, tint, Vector2.zero);
        mesh.AddVert(from + offset, tint, Vector2.zero);
        mesh.AddVert(to + offset, tint, Vector2.zero);
        mesh.AddVert(to - offset, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Ring(UnityEngine.UI.VertexHelper mesh, Vector2 centre, float radius,
        float width, float fill, Color tint)
    {
        const int segments = 80;
        int count = Mathf.CeilToInt(segments * fill);
        for (int i = 0; i < count; i++)
        {
            float a = (90f - 360f * i / segments) * Mathf.Deg2Rad;
            float b = (90f - 360f * Mathf.Min((i + 1f) / segments, fill)) * Mathf.Deg2Rad;
            Line(mesh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                centre + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width, tint);
        }
    }

    private static void Disc(UnityEngine.UI.VertexHelper mesh, Vector2 centre, float radius, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(centre, tint, Vector2.zero);
        for (int i = 0; i <= 24; i++)
        {
            float angle = i * Mathf.PI * 2f / 24;
            mesh.AddVert(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
