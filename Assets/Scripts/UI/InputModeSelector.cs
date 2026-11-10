using TMPro;
using UnityEngine;

/// <summary>Explicit input choice shared by mode/difficulty pages and the loaded run.</summary>
public sealed class InputModeSelector : MonoBehaviour
{
    [SerializeField] private TMP_Text buttonLabel;
    [SerializeField] private TMP_Text description;

    private void OnEnable()
    {
        GameSessionSettings.InputModeChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => GameSessionSettings.InputModeChanged -= Refresh;

    public void ToggleMode()
    {
        bool keyboard = GameSessionSettings.SelectedInputMode != GameInputMode.Keyboard;
        if (keyboard) EndlessRollball.Imu.ImuRuntimeSession.Instance?.Disconnect();
        else EndlessRollball.Imu.ImuRuntimeSession.Instance?.InvalidateCalibration();
        GameSessionSettings.SelectInputMode(keyboard ? GameInputMode.Keyboard : GameInputMode.Imu);
    }

    private void Refresh()
    {
        bool keyboard = GameSessionSettings.SelectedInputMode == GameInputMode.Keyboard;
        buttonLabel.text = keyboard ? "Input: Keyboard" : "Input: IMU";
        description.text = keyboard ? "Keyboard selected. Sensor preparation is skipped. Click to switch to IMU."
            : "IMU selected. Sensor preparation is required. Click to switch to Keyboard.";
    }
}
