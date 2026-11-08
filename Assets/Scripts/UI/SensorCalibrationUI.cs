using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Presentation for UI-07A and UI-07D. The BLE adapter supplies real readiness
/// and sampling updates on Unity's main thread; this view never fabricates samples.
/// Pain/stress and painting remain separate preparation stages.
/// </summary>
public sealed class SensorCalibrationUI : MonoBehaviour
{
    [SerializeField] private GameObject connectPage;
    [SerializeField] private GameObject calibrationPage;
    [SerializeField] private bool combinedPanels;
    [SerializeField] private GameObject returnPage;
    [SerializeField] private TMP_Text connectionStatus;
    [SerializeField] private TMP_Text sensorDevice;
    private string connectedDeviceName;
    [SerializeField] private TMP_Text poweredStatus;
    [SerializeField] private TMP_Text detectedStatus;
    [SerializeField] private TMP_Text dataStatus;
    [SerializeField] private UnityEngine.UI.Image[] connectionCheckBoxes;
    [SerializeField] private GameObject[] connectionCheckTicks;
    [SerializeField] private GameObject[] connectionCheckWaiting;
    [SerializeField] private TMP_Text[] connectionCheckStatuses;
    [SerializeField] private TMP_Text connectionMessage;
    [SerializeField] private TMP_Text calibrationConnection;
    [SerializeField] private TMP_Text selectedSettings;
    [SerializeField] private TMP_Text calibrationMessage;
    [SerializeField] private TMP_Text progressLabel;
    [SerializeField] private UnityEngine.UI.Image progressFill;
    [SerializeField] private UnityEngine.UI.Button connectContinue;
    [SerializeField] private UnityEngine.UI.Button calibrationRetry;
    [SerializeField] private UnityEngine.UI.Button calibrationContinue;
    [SerializeField] private TMP_Text sensorDiagnostics;
    [SerializeField] private PreparationFlowController preparationFlow;
    [SerializeField] private TMP_Text preparationTitle;
    private bool connectionOnly;

    public void SetPreparationMode(bool connectOnly)
    {
        connectionOnly = connectOnly;
        SetText(preparationTitle, connectOnly ? "Connect Sensor" : "Neutral Calibration");
        Refresh();
    }

    public void InvalidateNeutralDisplay() { ResetCalibration(); Refresh(); }

    [Header("Live orientation preview")]
    [SerializeField] private SensorCalibrationVisual movementVisual;
    [SerializeField] private RectTransform handPreview;
    [SerializeField, Min(1f)] private float previewTiltRange = 30f;
    [SerializeField, Min(.1f)] private float previewTimeout = 1f;
    private Vector2 handHome;
    private Quaternion handHomeRotation;
    private bool handCached;
    private Quaternion previewReference;
    private Quaternion latestOrientation;
    private Vector2 latestTiltDegrees;
    private Vector2 neutralTiltDegrees;
    private string transportState;
    private string transportMessage;
    private bool hasOrientation;
    private bool sampleStable;
    private float lastOrientationTime;
    private Vector2 targetTilt;
    private Vector2 displayedTilt;
    private float targetRotation;
    private float displayedRotation;

    [Header("Future BLE / preparation flow integration")]
    [SerializeField] private UnityEvent connectionRequested = new UnityEvent();
    [SerializeField] private UnityEvent calibrationRequested = new UnityEvent();
    [SerializeField] private UnityEvent preparationRequested = new UnityEvent();
    [SerializeField] private UnityEvent readyRequested = new UnityEvent();

    private bool powered;
    private bool detected;
    private bool receiving;
    private bool calibrated;
    private bool sampling;
    private float progress;
    private bool Connected => powered && detected && receiving;
    public UnityEvent ConnectionRequested => connectionRequested;
    public UnityEvent CalibrationRequested => calibrationRequested;

    public void SetTransportStatus(string state, string message)
    {
        transportState = state;
        transportMessage = message;
        Refresh();
    }

    public void SetDiagnostics(string text) => SetText(sensorDiagnostics, text);

    public void SetDeviceName(string deviceName)
    {
        connectedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();
        RefreshDeviceName();
    }

    private void RefreshDeviceName()
    {
        if (sensorDevice == null) return;
        // A Bluetooth name is device-provided text, not TMP formatting.
        sensorDevice.richText = false;
        sensorDevice.text = "Sensor Device: " +
            (Connected && !string.IsNullOrEmpty(connectedDeviceName) ? connectedDeviceName : "---");
    }

    public void SetNeutralTilt(Vector2 neutral)
    {
        neutralTiltDegrees = neutral;
        targetTilt = Vector2.zero;
        targetRotation = 0f;
    }

    public void BeginCalibrationDisplay()
    {
        ResetCalibration();
        sampling = true;
        Refresh();
    }

    public void RestoreNeutralCalibration(Vector2 neutral)
    {
        if (!Connected || !hasOrientation) return;
        calibrated = true;
        sampling = false;
        progress = 1f;
        SetNeutralTilt(neutral);
        Refresh();
    }

    // Already converted from the firmware convention; no sensor quaternion is assigned to a Transform.
    public void SetImuTiltDegrees(float roll, float pitch, bool stable)
    {
        if (!Connected || float.IsNaN(roll) || float.IsInfinity(roll) ||
            float.IsNaN(pitch) || float.IsInfinity(pitch)) return;
        if (!hasOrientation) neutralTiltDegrees = new Vector2(roll, pitch);
        latestTiltDegrees = new Vector2(roll, pitch);
        hasOrientation = true;
        sampleStable = stable;
        lastOrientationTime = Time.unscaledTime;
        Vector2 delta = new Vector2(Mathf.DeltaAngle(neutralTiltDegrees.x, roll),
            Mathf.DeltaAngle(neutralTiltDegrees.y, pitch));
        targetTilt = Vector2.ClampMagnitude(delta / previewTiltRange, 1f);
        targetRotation = delta.x;
        if (sampling && !stable) progress = 0f;
        Refresh();
    }

    // Transport owner must call SetConnectionState(..., false) on stale data or loss.
    public void SetConnectionState(bool controllerPowered, bool bluetoothDetected, bool dataReceived)
    {
        powered = controllerPowered;
        detected = bluetoothDetected;
        receiving = dataReceived;
        if (!Connected) ResetCalibration();
        Refresh();
    }

    // Invoke only with progress based on real stable samples, never elapsed UI time.
    public void SetCalibrationProgress(float stableProgress, bool neutralPoseValid)
    {
        if (!Connected || !sampling) return;
        if (movementVisual != null && !hasOrientation) return;
        if (movementVisual != null && !sampleStable)
        {
            progress = 0f;
            Refresh();
            return;
        }
        progress = float.IsNaN(stableProgress) || float.IsInfinity(stableProgress)
            ? 0f : Mathf.Clamp01(stableProgress);
        calibrated = neutralPoseValid && progress >= 1f && (movementVisual == null || sampleStable);
        if (calibrated)
        {
            sampling = false;
            // The comfortable held pose becomes the centre, regardless of its absolute angle.
            previewReference = latestOrientation;
            neutralTiltDegrees = latestTiltDegrees;
            targetTilt = Vector2.zero;
            targetRotation = 0f;
        }
        Refresh();
    }

    /// <summary>
    /// Feed coordinate-corrected Unity orientation and stability from the future BLE adapter.
    /// Call on Unity's main thread. Tilt is a visual mapping, not physical position tracking.
    /// Sampling progress and completion still come from the calibration owner.
    /// </summary>
    public void SetImuOrientation(Quaternion orientation, bool stable)
    {
        if (!Connected) return;
        float length = orientation.x * orientation.x + orientation.y * orientation.y +
            orientation.z * orientation.z + orientation.w * orientation.w;
        if (float.IsNaN(length) || float.IsInfinity(length) || length < .000001f) return;
        orientation = orientation.normalized;
        if (!hasOrientation) previewReference = orientation;
        latestOrientation = orientation;
        hasOrientation = true;
        sampleStable = stable;
        lastOrientationTime = Time.unscaledTime;
        Vector3 angles = (Quaternion.Inverse(previewReference) * orientation).eulerAngles;
        float roll = Mathf.DeltaAngle(0f, angles.z);
        float pitch = Mathf.DeltaAngle(0f, angles.x);
        targetTilt = Vector2.ClampMagnitude(new Vector2(roll, -pitch) / previewTiltRange, 1f);
        targetRotation = roll;
        if (sampling && !stable) progress = 0f;
        Refresh();
    }

    public void Open()
    {
        if (preparationFlow != null) { preparationFlow.Begin(); return; }
        if (returnPage != null) returnPage.SetActive(false);
        gameObject.SetActive(true);
        ShowConnection();
    }

    public void Close()
    {
        if (preparationFlow != null) { preparationFlow.BackFromSensor(this); return; }
        gameObject.SetActive(false);
        if (returnPage != null) returnPage.SetActive(true);
    }

    public void ShowConnection()
    {
        if (connectPage != null) connectPage.SetActive(true);
        if (calibrationPage != null) calibrationPage.SetActive(combinedPanels);
        Refresh();
    }

    // Allows layout inspection before the intermediate preparation screens exist.
    // This does not mark pain/stress, painting, connection or calibration complete.
    public void ShowCalibration()
    {
        if (connectPage != null) connectPage.SetActive(combinedPanels);
        if (calibrationPage != null) calibrationPage.SetActive(true);
        Refresh();
    }

    public void RetryConnection()
    {
        transportState = "Scanning";
        transportMessage = "Looking for sensor. Keep your controller powered on.";
        SetConnectionState(false, false, false);
        SetText(connectionMessage, "Looking for sensor. Keep your controller powered on.");
        connectionRequested.Invoke();
    }

    public void RetryCalibration()
    {
        if (preparationFlow != null && connectionOnly) return;
        if (!Connected || (movementVisual != null && !hasOrientation)) return;
        ResetCalibration();
        if (hasOrientation) previewReference = latestOrientation;
        if (hasOrientation) neutralTiltDegrees = latestTiltDegrees;
        targetTilt = Vector2.zero;
        targetRotation = 0f;
        sampling = true;
        Refresh();
        calibrationRequested.Invoke();
    }

    public void ContinueToPreparation()
    {
        if (preparationFlow != null) { if (Connected) preparationFlow.ShowRatings(); return; }
        if (Connected) preparationRequested.Invoke();
    }

    public void ContinueWhenReady()
    {
        if (preparationFlow != null)
        {
            if (connectionOnly) { if (Connected) preparationFlow.ShowRatings(); }
            else if (Connected && calibrated) preparationFlow.ShowGuide();
            return;
        }
        if (Connected && calibrated) readyRequested.Invoke();
    }

    private void OnEnable()
    {
        if (preparationFlow != null)
        {
            connectionOnly = preparationFlow.IsConnectionView(this);
            SetText(preparationTitle, connectionOnly ? "Connect Sensor" : "Neutral Calibration");
        }
        if (!handCached && handPreview != null)
        {
            handHome = handPreview.anchoredPosition;
            handHomeRotation = handPreview.localRotation;
            handCached = true;
        }
        Refresh();
    }

    private void Update()
    {
        if (hasOrientation && Time.unscaledTime - lastOrientationTime > previewTimeout)
            SetConnectionState(powered, detected, false);
        float blend = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        displayedTilt = Vector2.Lerp(displayedTilt, targetTilt, blend);
        displayedRotation = Mathf.LerpAngle(displayedRotation, targetRotation, blend);
        if (handCached && handPreview != null)
        {
            handPreview.anchoredPosition = handHome + Vector2.Scale(displayedTilt, new Vector2(60f, 45f));
            handPreview.localRotation = handHomeRotation * Quaternion.Euler(-displayedTilt.y * 16f, 0f, -displayedRotation);
        }
        if (movementVisual != null)
            movementVisual.SetFeedback(displayedTilt, displayedRotation, progress,
                Connected && hasOrientation, sampleStable, calibrated);
    }

    private void OnDisable()
    {
        if (handCached && handPreview != null)
        {
            handPreview.anchoredPosition = handHome;
            handPreview.localRotation = handHomeRotation;
        }
        ResetCalibration();
        hasOrientation = false;
        targetTilt = displayedTilt = Vector2.zero;
        targetRotation = displayedRotation = 0f;
    }

    private void ResetCalibration()
    {
        progress = 0f;
        calibrated = false;
        sampling = false;
    }

    private void Refresh()
    {
        RefreshDeviceName();
        if (!Connected)
        {
            hasOrientation = false;
            sampleStable = false;
            targetTilt = Vector2.zero;
            targetRotation = 0f;
        }
        SetText(connectionStatus, "Connection Status: " + (transportState ?? (Connected ? "Connected" : detected ? "Waiting for sensor data" : "Not connected")));
        bool visualChecks = connectionCheckBoxes != null && connectionCheckBoxes.Length == 3;
        SetText(poweredStatus, (visualChecks ? "" : powered ? "[READY]  " : "[WAIT]  ") + "Sensor detected");
        SetText(detectedStatus, (visualChecks ? "" : detected ? "[READY]  " : "[WAIT]  ") + "Bluetooth connected");
        SetText(dataStatus, (visualChecks ? "" : receiving ? "[READY]  " : "[WAIT]  ") + "Live data received");
        SetConnectionCheck(0, powered);
        SetConnectionCheck(1, detected);
        SetConnectionCheck(2, receiving);
        SetText(connectionMessage, preparationFlow != null && connectionOnly && Connected
            ? "All three checks are complete. Continue to Pain & Stress."
            : !string.IsNullOrEmpty(transportMessage) ? transportMessage : Connected
            ? (combinedPanels ? "Sensor connected. Calibrate after preparation." : "Controller connected. Continue to the pain and stress check.")
            : "Power on your controller and select Retry Connection.");
        SetText(calibrationConnection, "BLE connection: " + (Connected ? "Connected" : "Not connected"));
        SetText(selectedSettings, GameSessionSettings.SelectedMode.HasValue
            ? "Selected: " + GameSessionSettings.SelectedMode.Value + " / " +
              (GameSessionSettings.SelectedMode == GameMode.Endless ? "Dynamic" :
               GameSessionSettings.SelectedDifficulty.HasValue ? GameSessionSettings.SelectedDifficulty.Value.ToString() : "Choose difficulty")
            : "Selected: choose a game mode before your session");
        if (progressFill != null) progressFill.fillAmount = progress;
        SetText(progressLabel, movementVisual != null ? "Sensor readiness" : Mathf.RoundToInt(progress * 100f) + "%  |  Sensor readiness");
        SetText(calibrationMessage, !Connected ? "Connect your sensor to begin."
            : preparationFlow != null && connectionOnly ? "Final neutral calibration follows painting or skipping."
            : movementVisual != null && !hasOrientation ? "Waiting for sensor input."
            : calibrated ? "Neutral position saved."
            : hasOrientation && !sampleStable ? "Movement detected. Settle your hand."
            : sampling ? "Hold your hand comfortably still."
            : "Ready to calibrate. Rest your hand, then select Calibrate.");
        if (connectContinue != null && !combinedPanels) connectContinue.interactable = Connected;
        if (calibrationRetry != null) calibrationRetry.interactable = Connected && (movementVisual == null || hasOrientation) && !connectionOnly;
        if (calibrationContinue != null) calibrationContinue.interactable = Connected && (connectionOnly || calibrated);
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null) target.text = value;
    }

    private void SetConnectionCheck(int index, bool done)
    {
        if (connectionCheckBoxes == null || index >= connectionCheckBoxes.Length) return;
        if (connectionCheckBoxes[index] != null)
            connectionCheckBoxes[index].color = done ? new Color(.22f, .65f, .38f) : new Color(.78f, .80f, .76f);
        if (connectionCheckTicks != null && index < connectionCheckTicks.Length && connectionCheckTicks[index] != null)
            connectionCheckTicks[index].SetActive(done);
        if (connectionCheckWaiting != null && index < connectionCheckWaiting.Length && connectionCheckWaiting[index] != null)
            connectionCheckWaiting[index].SetActive(!done);
        if (connectionCheckStatuses != null && index < connectionCheckStatuses.Length)
            SetText(connectionCheckStatuses[index], done ? "Done" : "Waiting");
    }
}
