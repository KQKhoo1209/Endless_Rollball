using UnityEngine;

/// <summary>Owns a session-only GPU drawing surface. No artwork is saved to disk.</summary>
public sealed class PaintingCanvas : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.RawImage surface;
    [SerializeField] private Shader strokeShader;
    [SerializeField] private int textureWidth = 1024;
    [SerializeField] private int textureHeight = 512;
    [SerializeField] private float brushRadius = 9f;
    private RenderTexture front;
    private RenderTexture back;
    private Material brush;
    private bool hasPoint;
    private Vector2 previous;
    private static readonly int Segment = Shader.PropertyToID("_Segment");
    private static readonly int StrokeColour = Shader.PropertyToID("_StrokeColour");
    private static readonly int SurfaceSize = Shader.PropertyToID("_SurfaceSize");
    private static readonly int Radius = Shader.PropertyToID("_Radius");
    public bool IsReady => front != null && front.IsCreated() && back != null && back.IsCreated() && brush != null;

    public bool Initialize()
    {
        if (IsReady) return true;
        if (surface == null || strokeShader == null || !strokeShader.isSupported) return false;
        ReleaseSurface();
        textureWidth = Mathf.Clamp(textureWidth, 256, 2048);
        textureHeight = Mathf.Clamp(textureHeight, 128, 2048);
        front = CreateTexture("Temporary Painting A");
        back = CreateTexture("Temporary Painting B");
        brush = new Material(strokeShader) { hideFlags = HideFlags.DontSave };
        brush.SetVector(SurfaceSize, new Vector4(textureWidth, textureHeight, 0, 0));
        brush.SetFloat(Radius, brushRadius);
        surface.texture = front;
        Clear();
        return IsReady;
    }

    private RenderTexture CreateTexture(string title)
    {
        var texture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32)
        {
            name = title, hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            useMipMap = false, autoGenerateMips = false
        };
        texture.Create();
        return texture;
    }

    public Vector2 ClampPoint(Vector2 uv)
    {
        float x = (brushRadius + 2f) / textureWidth;
        float y = (brushRadius + 2f) / textureHeight;
        return new Vector2(Mathf.Clamp(uv.x, x, 1f - x), Mathf.Clamp(uv.y, y, 1f - y));
    }

    public void Paint(Vector2 uv, Color colour)
    {
        if (!IsReady) return;
        uv = ClampPoint(uv);
        if (!hasPoint) { previous = uv; hasPoint = true; return; }
        if (Vector2.Scale(uv - previous, new Vector2(textureWidth, textureHeight)).sqrMagnitude < .5625f) return;
        brush.SetVector(Segment, new Vector4(previous.x, previous.y, uv.x, uv.y));
        brush.SetColor(StrokeColour, colour);
        Graphics.Blit(front, back, brush);
        var swap = front; front = back; back = swap;
        surface.texture = front;
        previous = uv;
    }

    public void BreakStroke() => hasPoint = false;

    public void Clear()
    {
        if (!IsReady) return;
        var saved = RenderTexture.active;
        try
        {
            RenderTexture.active = front;
            GL.Clear(false, true, new Color(1f, .995f, .98f, 1f));
            RenderTexture.active = back;
            GL.Clear(false, true, new Color(1f, .995f, .98f, 1f));
        }
        finally { RenderTexture.active = saved; }
        surface.texture = front;
        BreakStroke();
    }

    public void ReleaseSurface()
    {
        BreakStroke();
        if (surface != null) surface.texture = null;
        if (front != null) { front.Release(); Destroy(front); front = null; }
        if (back != null) { back.Release(); Destroy(back); back = null; }
        if (brush != null) { Destroy(brush); brush = null; }
    }

    private void OnDisable() => BreakStroke();
    private void OnDestroy() => ReleaseSurface();
}
