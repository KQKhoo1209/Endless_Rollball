using TMPro;
using UnityEngine;

public sealed class InputModeGuideUI : MonoBehaviour
{
    [SerializeField] private TMP_Text controlHint;
    private void OnEnable()
    {
        controlHint.text = GameSessionSettings.SelectedInputMode == GameInputMode.Keyboard
            ? "Keyboard: A / Left and D / Right steer and turn avatars. Up arrow jumps. Forward movement is automatic."
            : "IMU: Roll your wrist left / right to steer and turn avatars. Tilt upward to jump. Forward movement is automatic.";
    }
}
