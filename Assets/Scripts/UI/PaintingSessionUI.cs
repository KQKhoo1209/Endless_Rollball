using EndlessRollball.Imu;
using TMPro;
using UnityEngine;

public sealed class PaintingSessionUI : MonoBehaviour
{
    [SerializeField] private PaintingCanvas drawing;
    [SerializeField] private RectTransform drawingRect;
    [SerializeField] private UnityEngine.UI.Image brushCursor;
    [SerializeField] private TMP_Text connectionStatus;
    [SerializeField] private TMP_Text selectedSettings;
    [SerializeField] private TMP_Text inputMessage;
    [SerializeField] private UnityEngine.UI.Button reconnectButton;
    [SerializeField] private GameObject[] colourSelections;
    [SerializeField] private RectTransform handPreview;
    [SerializeField] private PreparationFlowController flow;
    [SerializeField, Min(3f)] private float rollRange = 25f;
    [SerializeField, Min(3f)] private float pitchRange = 20f;
    [SerializeField, Min(0f)] private float deadZone = 1.5f;
    [SerializeField] private bool invertHorizontal;
    [SerializeField] private bool invertVertical = true;
    private ImuRuntimeSession session;
    private bool hasReference;
    private Vector2 reference;
    private Vector2 referenceCursor;
    private Vector2 target = new Vector2(.5f, .5f);
    private Vector2 cursor = new Vector2(.5f, .5f);
    private Vector2 rotation;
    private Quaternion handHome;
    private bool handCached;
    private Color colour = new Color(.20f, .48f, .76f);
    private float nextStatus;

    private void OnEnable()
    {
        session = ImuRuntimeSession.Ensure();
        session.SampleReceived += OnSample;
        hasReference = false;
        drawing.Initialize();
        drawing.BreakStroke();
        if (!handCached && handPreview != null) { handHome = handPreview.localRotation; handCached = true; }
        if (session.HasFreshSample) OnSample(session.LatestSample);
        selectedSettings.text = GameSessionSettings.SelectedMode.HasValue
            ? GameSessionSettings.SelectedMode + " / " + (GameSessionSettings.SelectedMode == GameMode.Endless
                ? "Dynamic" : GameSessionSettings.SelectedDifficulty?.ToString() ?? "Choose difficulty")
            : "Choose a game mode before play";
        UpdateCursor();
        RefreshStatus();
    }

    private void OnDisable()
    {
        if (session != null) session.SampleReceived -= OnSample;
        hasReference = false;
        drawing.BreakStroke();
        if (handCached && handPreview != null) handPreview.localRotation = handHome;
    }

    private void OnSample(ImuSample sample)
    {
        if (session == null || !session.HasFreshSample) return;
        if (!hasReference)
        {
            reference = new Vector2(sample.Roll, sample.Pitch);
            referenceCursor = cursor;
            target = cursor;
            hasReference = true;
            drawing.BreakStroke();
        }
        rotation = new Vector2(Mathf.DeltaAngle(reference.x, sample.Roll), Mathf.DeltaAngle(reference.y, sample.Pitch));
        Vector2 mapped = new Vector2(Map(rotation.x, rollRange) * (invertHorizontal ? -1 : 1),
            Map(rotation.y, pitchRange) * (invertVertical ? -1 : 1));
        target = drawing.ClampPoint(referenceCursor + mapped * .45f);
    }

    private float Map(float degrees, float range)
    {
        float amount = Mathf.Abs(degrees);
        float zone = Mathf.Min(deadZone, range - .1f);
        return amount <= zone ? 0f : Mathf.Sign(degrees) * Mathf.Clamp01((amount - zone) / (range - zone));
    }

    private void Update()
    {
        bool fresh = session != null && session.HasFreshSample;
        if (!fresh)
        {
            hasReference = false;
            drawing.BreakStroke();
        }
        else if (hasReference)
        {
            cursor = drawing.ClampPoint(Vector2.Lerp(cursor, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime)));
            drawing.Paint(cursor, colour);
            UpdateCursor();
            if (handCached && handPreview != null)
                handPreview.localRotation = handHome * Quaternion.Euler(Mathf.Clamp(-rotation.y * .5f, -20, 20), 0, -rotation.x);
        }
        brushCursor.enabled = fresh && drawing.IsReady;
        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + .15f;
            RefreshStatus();
        }
    }

    private void UpdateCursor()
    {
        brushCursor.rectTransform.anchoredPosition = Vector2.Scale(cursor, drawingRect.rect.size);
        brushCursor.color = colour;
    }

    private void RefreshStatus()
    {
        bool fresh = session != null && session.HasFreshSample;
        connectionStatus.richText = false;
        connectionStatus.text = fresh ? "BLE Connected\n" + (session.ConnectedDeviceName ?? "---")
            : "BLE: " + (session == null ? "Not connected" : session.ConnectionState.ToString());
        inputMessage.text = !drawing.IsReady ? "Painting surface unavailable. You can continue to calibration."
            : fresh ? "Rotate your wrist gently to move the brush."
            : "Sensor input lost. Reconnect to continue painting, or skip to calibration.";
        reconnectButton.gameObject.SetActive(!fresh);
        reconnectButton.interactable = session != null && session.ConnectionState != ImuConnectionState.Scanning &&
            session.ConnectionState != ImuConnectionState.Connecting && session.ConnectionState != ImuConnectionState.AwaitingData;
    }

    private void Select(int index, Color value)
    {
        colour = value;
        drawing.BreakStroke();
        for (int i = 0; i < colourSelections.Length; i++) colourSelections[i].SetActive(i == index);
        UpdateCursor();
    }

    public void SelectBlue() => Select(0, new Color(.20f, .48f, .76f));
    public void SelectCoral() => Select(1, new Color(.85f, .36f, .29f));
    public void SelectPurple() => Select(2, new Color(.56f, .35f, .70f));
    public void Clear() => drawing.Clear();
    public void Reconnect() { hasReference = false; drawing.BreakStroke(); session?.Connect(); RefreshStatus(); }
    public void Back() => flow.ShowRatings();
    public void RestAndReturn() => flow.RestAndReturn();
    public void ContinueToCalibration() => flow.ShowNeutralCalibration();

    public void ResetAttempt()
    {
        drawing.ReleaseSurface();
        cursor = target = new Vector2(.5f, .5f);
        hasReference = false;
        SelectBlue();
    }
}
