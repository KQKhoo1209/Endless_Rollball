using EndlessRollball.Imu;
using EndlessRollball.Track;
using EndlessRollball.Tutorial;
using TMPro;
using UnityEngine;

public sealed class ImuGameplayRecoveryUI : MonoBehaviour
{
    [SerializeField] private ImuGameplayInput input;
    [SerializeField] private GameplayRunController run;
    [SerializeField] private TutorialController tutorial;
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text message;
    [SerializeField] private UnityEngine.UI.Button reconnect;
    [SerializeField] private UnityEngine.UI.Button calibrate;
    [SerializeField] private UnityEngine.UI.Button resume;
    private bool recovering;
    private float nextTextAt;

    private void Update()
    {
        var session = ImuRuntimeSession.Instance;
        bool paused = run != null ? run.State == SessionFlowState.Paused : tutorial != null && tutorial.IsPaused;
        bool ended = run != null && (run.State == SessionFlowState.Results || run.State == SessionFlowState.Ending || run.State == SessionFlowState.SetupError);
        bool show = !ended && input != null && input.UsesImu &&
            (!input.IsInputReady || recovering && paused);
        if (!show)
        {
            recovering = false;
            if (panel.activeSelf) panel.SetActive(false);
            return;
        }
        recovering = true;
        if (!panel.activeSelf) panel.SetActive(true);
        reconnect.interactable = session != null && session.ConnectionState != ImuConnectionState.Scanning &&
            session.ConnectionState != ImuConnectionState.Connecting && session.ConnectionState != ImuConnectionState.AwaitingData;
        calibrate.interactable = session != null && session.ConnectionState == ImuConnectionState.Receiving && !session.IsSampling;
        resume.interactable = input.IsInputReady && paused;
        if (Time.unscaledTime < nextTextAt) return;
        nextTextAt = Time.unscaledTime + .1f;
        message.text = session == null ? "Connect your sensor to continue."
            : session.HasFreshInput ? "Sensor ready. Select Resume when you are comfortable."
            : session.IsSampling ? "Hold your wrist comfortably still.\nCalibration: " + Mathf.RoundToInt(session.CalibrationProgress * 100f) + "%"
            : session.ConnectionState == ImuConnectionState.Receiving ? "Sensor connected. Rest your wrist and select Calibrate."
            : session.StatusMessage;
    }
    public void Reconnect() => ImuRuntimeSession.Ensure().Connect();
    public void Calibrate() => ImuRuntimeSession.Ensure().Calibrate();
    public void Resume()
    {
        if (input == null || !input.IsInputReady) return;
        input.ClearTransientInput();
        if (run != null) run.ResumeSession();
        else if (tutorial != null) tutorial.ResumeAfterSensorCalibration();
    }
    public void Exit()
    {
        if (run != null) run.EndSessionFromPause();
        else if (tutorial != null) tutorial.ExitAfterSensorLoss();
    }
}
