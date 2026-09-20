using UnityEngine;
using UnityEngine.SceneManagement;
using EndlessRollball.Track;

public sealed class GameSetupUI : MonoBehaviour
{
    [SerializeField] private GameObject gameModePage;
    [SerializeField] private GameObject difficultyPage;

    private bool isLoading;

    public void SelectStandard()
    {
        GameSessionSettings.SelectedMode = GameMode.Standard;
    }

    public void SelectFastPaced()
    {
        GameSessionSettings.SelectedMode = GameMode.FastPaced;
    }

    public void SelectEndless()
    {
        GameSessionSettings.SelectedMode = GameMode.Endless;
    }

    public void SelectEasy()
    {
        GameSessionSettings.SelectedDifficulty = DifficultyMode.Easy;
    }

    public void SelectNormal()
    {
        GameSessionSettings.SelectedDifficulty = DifficultyMode.Normal;
    }

    public void SelectHard()
    {
        GameSessionSettings.SelectedDifficulty = DifficultyMode.Hard;
    }

    public void ContinueFromMode()
    {
        if (!GameSessionSettings.SelectedMode.HasValue)
        {
            Debug.LogWarning("Select a game mode first.");
            return;
        }

        if (GameSessionSettings.SelectedMode == GameMode.Endless)
        {
            StartGame();
            return;
        }

        // Standard and Fast-Paced require difficulty selection.
        gameModePage.SetActive(false);
        difficultyPage.SetActive(true);

        if(GameSessionSettings.SelectedMode != GameMode.Endless && GameSessionSettings.SelectedDifficulty.HasValue)
        {
            StartGame();
            return;
        }
    }

    public void StartGame()
    {
        if (isLoading)
            return;

        if (!GameSessionSettings.HasValidSelection)
        {
            Debug.LogWarning(
                "Select a game mode and difficulty first.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded("GameplayScene"))
        {
            Debug.LogError(
                "Add GameplayScene to the active build scene list.");
            return;
        }

        isLoading = true;
        Time.timeScale = 1f;

        Debug.Log(
            $"Starting {GameSessionSettings.SelectedMode} / " +
            $"{GameSessionSettings.TrackDifficulty}");

        SceneManager.LoadSceneAsync("GameplayScene");
    }
}
