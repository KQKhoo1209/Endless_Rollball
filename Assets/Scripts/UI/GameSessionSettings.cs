using UnityEngine;
using EndlessRollball.Track;

public enum GameMode
{
    Standard,
    FastPaced,
    Endless
}

public static class GameSessionSettings
{
    // The ? allows null: nothing selected yet.
    public static GameMode? SelectedMode { get; set; }

    public static DifficultyMode? SelectedDifficulty { get; set; }

    public static bool HasValidSelection =>
        SelectedMode.HasValue &&
        (SelectedMode == GameMode.Endless ||
         SelectedDifficulty.HasValue);

    // Easy is a fallback for testing GameplayScene directly.
    // StartGame() prevents incomplete menu selections from starting.
    public static DifficultyMode TrackDifficulty =>
        SelectedMode == GameMode.Endless
            ? DifficultyMode.Endless
            : SelectedDifficulty ?? DifficultyMode.Easy;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDefaults()
    {
        SelectedMode = null;
        SelectedDifficulty = null;
    }
}
