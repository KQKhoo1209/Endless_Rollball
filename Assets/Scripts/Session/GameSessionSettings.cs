using UnityEngine;
using EndlessRollball.Track;

public enum GameMode
{
    Standard,
    FastPaced,
    Endless
}

public enum GameInputMode { Imu, Keyboard }

public static class GameSessionSettings
{
    public static GameInputMode SelectedInputMode { get; private set; } = GameInputMode.Imu;
    public static event System.Action InputModeChanged;
    public static void SelectInputMode(GameInputMode mode)
    {
        if (SelectedInputMode == mode) return;
        SelectedInputMode = mode;
        InputModeChanged?.Invoke();
    }
    public static double? GameplayLoadRequestedAt { get; private set; }
    public static string GameplayEntryRoute { get; private set; } = "direct";
    public static void MarkGameplayLoadRequested(string route = "menu")
    { GameplayEntryRoute = route; GameplayLoadRequestedAt = Time.realtimeSinceStartupAsDouble; }
    // The ? allows null: nothing selected yet.
    public static GameMode? SelectedMode { get; set; }
    public static DifficultyMode? SelectedDifficulty { get; set; }
    public static int? PainRating { get; set; }
    public static int? StressRating { get; set; }
    public static bool UseReducedFeedback { get; set; }
    public static void ResetWellbeing()
    {
        PainRating = StressRating = null;
        UseReducedFeedback = false;
    }
    public static bool HasValidSelection => SelectedMode.HasValue && (SelectedMode == GameMode.Endless || SelectedDifficulty.HasValue);
    // Easy is a fallback for testing GameplayScene directly.
    // StartGame() prevents incomplete menu selections from starting.
    public static DifficultyMode TrackDifficulty => SelectedMode == GameMode.Endless ? DifficultyMode.Endless : SelectedDifficulty ?? DifficultyMode.Easy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDefaults()
    {
        SelectedInputMode = GameInputMode.Imu;
        InputModeChanged = null;
        ResetSelection();
    }

    public static void ResetSelection()
    {
        ResetWellbeing();
        GameplayLoadRequestedAt = null;
        GameplayEntryRoute = "direct";
        SelectedMode = null;
        SelectedDifficulty = null;
    }
}
