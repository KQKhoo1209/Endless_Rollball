using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using EndlessRollball.Track;
using EndlessRollball.Tutorial;

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
    [SerializeField] private Button standardModeButton, fastPacedModeButton, endlessModeButton;
    [SerializeField] private Button easyDifficultyButton, normalDifficultyButton, hardDifficultyButton;
    [SerializeField] private PreparationFlowController preparationFlow;
    [SerializeField] private GameObject guidancePage;
    private Button[] optionButtons;
    private ColorBlock[] optionColours;
    private bool isLoading;
    private void Awake()
    {
        optionButtons = new[] { standardModeButton, fastPacedModeButton, endlessModeButton,
            easyDifficultyButton, normalDifficultyButton, hardDifficultyButton };
        optionColours = new ColorBlock[optionButtons.Length];
        for (int i = 0; i < optionButtons.Length; i++)
            if (optionButtons[i] != null) optionColours[i] = optionButtons[i].colors;
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
        HighlightChoice(0, GameSessionSettings.SelectedMode == GameMode.Standard);
        HighlightChoice(1, GameSessionSettings.SelectedMode == GameMode.FastPaced);
        HighlightChoice(2, GameSessionSettings.SelectedMode == GameMode.Endless);
        HighlightChoice(3, GameSessionSettings.SelectedDifficulty == DifficultyMode.Easy);
        HighlightChoice(4, GameSessionSettings.SelectedDifficulty == DifficultyMode.Normal);
        HighlightChoice(5, GameSessionSettings.SelectedDifficulty == DifficultyMode.Hard);
        foreach (SelectedSettingsDisplay display in FindObjectsByType<SelectedSettingsDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            display.RefreshDisplay();
    }

    private void HighlightChoice(int index, bool selected)
    {
        if (optionButtons == null || optionButtons[index] == null) return;
        var colours = optionColours[index];
        // Retain the hover appearance independently of pointer position or EventSystem focus.
        if (selected) colours.normalColor = colours.selectedColor = colours.highlightedColor;
        optionButtons[index].colors = colours;
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
            ContinueFromDifficulty();
            return;
        }

        // Standard and Fast-Paced require difficulty selection.
        gameModePage.SetActive(false);
        difficultyPage.SetActive(true);
        RefreshSelection();
    }

    public void ContinueFromDifficulty()
    {
        if (!GameSessionSettings.HasValidSelection) return;
        gameModePage.SetActive(false);
        difficultyPage.SetActive(false);
        if (GameSessionSettings.SelectedInputMode == GameInputMode.Keyboard)
        {
            if (preparationFlow != null) preparationFlow.ShowKeyboardGuide();
            else if (guidancePage != null) guidancePage.SetActive(true);
        }
        else if (preparationFlow != null) preparationFlow.Begin();
    }

    public void BackFromGuide()
    {
        if (GameSessionSettings.SelectedInputMode == GameInputMode.Imu && preparationFlow != null)
        {
            preparationFlow.ShowNeutralCalibration();
            return;
        }
        if (preparationFlow != null)
        {
            preparationFlow.ReturnToSetup();
            RefreshSelection();
            return;
        }
        if (guidancePage != null) guidancePage.SetActive(false);
        bool showMode = !GameSessionSettings.SelectedMode.HasValue || GameSessionSettings.SelectedMode == GameMode.Endless;
        gameModePage.SetActive(showMode);
        difficultyPage.SetActive(!showMode);
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

        if (GameSessionSettings.SelectedInputMode == GameInputMode.Imu && preparationFlow != null &&
            !EndlessRollball.Imu.ImuRuntimeSession.Ensure().HasFreshInput)
        {
            preparationFlow.Begin();
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded("GameplayScene"))
        {
            TutorialSceneTransition.ShowMenuError("Add GameplayScene to the active build scene list, then retry.", StartGame);
            return;
        }

        if (!TutorialProgress.IsCompleted)
        {
            Time.timeScale = 1f;
            isLoading = TutorialSceneTransition.Load(TutorialSceneTransition.SceneName, message =>
            {
                isLoading = false;
                TutorialSceneTransition.ShowMenuError(message, StartGame);
            });
            return;
        }

        isLoading = true;
        Time.timeScale = 1f;
        GameSessionSettings.MarkGameplayLoadRequested();
        EndlessRollball.Infrastructure.RuntimeLog.Info($"Starting {GameSessionSettings.SelectedMode} / " + $"{GameSessionSettings.TrackDifficulty}");
        GameplayLoadingTransition.LoadGameplay(message =>
        {
            isLoading = false;
            TutorialSceneTransition.ShowMenuError(message, StartGame);
        });
    }
}
