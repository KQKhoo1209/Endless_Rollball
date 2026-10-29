using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using EndlessRollball.Track;

public sealed class GameSetupUI : MonoBehaviour
{
    [SerializeField]
    private GameObject gameModePage;
    [SerializeField]
    private GameObject difficultyPage;
    [SerializeField]
    private Button modeContinueButton;
    [SerializeField]
    private Button difficultyContinueButton;
    private bool isLoading;
    private void Awake()
    {
        GameSessionSettings.ResetSelection();
    }

    private void Start() => RefreshSelection();
    private void SelectMode(GameMode mode)
    {
        GameSessionSettings.SelectedMode = mode;
        GameSessionSettings.SelectedDifficulty = null;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        if (modeContinueButton != null)
            modeContinueButton.interactable = GameSessionSettings.SelectedMode.HasValue;
        if (difficultyContinueButton != null)
            difficultyContinueButton.interactable = GameSessionSettings.HasValidSelection;
        foreach (SelectedSettingsDisplay display in FindObjectsByType<SelectedSettingsDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            display.RefreshDisplay();
    }

    public void SelectStandard()
    {
        SelectMode(GameMode.Standard);
    }

    public void SelectFastPaced()
    {
        SelectMode(GameMode.FastPaced);
    }

    public void SelectEndless()
    {
        SelectMode(GameMode.Endless);
    }

    public void SelectEasy()
    {
        GameSessionSettings.SelectedDifficulty = DifficultyMode.Easy;
        RefreshSelection();
    }

    public void SelectNormal()
    {
        GameSessionSettings.SelectedDifficulty = DifficultyMode.Normal;
        RefreshSelection();
    }

    public void SelectHard()
    {
        GameSessionSettings.SelectedDifficulty = DifficultyMode.Hard;
        RefreshSelection();
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
        RefreshSelection();
    }

    public void StartGame()
    {
        if (isLoading)
            return;
        if (!GameSessionSettings.HasValidSelection)
        {
            Debug.LogWarning("Select a game mode and difficulty first.");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded("GameplayScene"))
        {
            Debug.LogError("Add GameplayScene to the active build scene list.");
            return;
        }

        isLoading = true;
        Time.timeScale = 1f;
        GameSessionSettings.MarkGameplayLoadRequested();
        Debug.Log($"Starting {GameSessionSettings.SelectedMode} / " + $"{GameSessionSettings.TrackDifficulty}");
        GameplayLoadingTransition.LoadGameplay();
    }
}
