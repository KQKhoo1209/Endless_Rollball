using TMPro;
using UnityEngine;

public sealed class SelectedSettingsDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text modeText;
    [SerializeField] private TMP_Text difficultyText;

    private void OnEnable()
    {
        RefreshDisplay();
    }

    public void RefreshDisplay()
    {
        string mode = GameSessionSettings.SelectedMode switch
        {
            GameMode.Standard => "Standard",
            GameMode.FastPaced => "Fast-Paced",
            GameMode.Endless => "Endless",
            _ => ""
        };

        string difficulty =
            GameSessionSettings.SelectedMode == GameMode.Endless
                ? "Dynamic"
                : GameSessionSettings.SelectedDifficulty.ToString() ?? "";

        if (modeText != null)
            modeText.text = $"Selected Mode: {mode}";

        if (difficultyText != null)
            difficultyText.text = $"Selected : {mode} / {difficulty}";
    }
}