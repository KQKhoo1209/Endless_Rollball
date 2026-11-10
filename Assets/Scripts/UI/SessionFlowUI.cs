using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class SessionFlowUI : MonoBehaviour
    {
        [SerializeField] private GameObject pauseButton;
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text outcomeText;
        [SerializeField] private TMP_Text durationText;
        [Header("HUD")]
        [SerializeField] private GameObject gameplayHUD;
        [SerializeField] private TMP_Text hudScoreText;
        [SerializeField] private TMP_Text hudDurationText;
        [SerializeField] private TMP_Text hudModeText;
        [SerializeField] private TMP_Text hudDifficultyText;
        [Header("Performance")]
        [SerializeField] private TMP_Text navigationPercentText;
        [SerializeField] private TMP_Text navigationRatioText;
        [SerializeField] private TMP_Text jumpPercentText;
        [SerializeField] private TMP_Text jumpRatioText;
        [SerializeField] private TMP_Text overallText;
        [SerializeField] private TMP_Text scoreFormulaText;
        [SerializeField] private TMP_Text coinsText;
        [SerializeField] private EndlessRollball.Avatars.SessionResultAvatarPresenter avatarPresenter;
        private SessionEndInfo displayedResult;
        [Tooltip("Presentation only. Task counting/calculation still run. Later preparation UI can call SetReducedFeedback.")]
        [SerializeField] private bool reducedFeedback;

        private long displayedDurationSecond = -1;
        private TMP_Text displayedDurationLabel;
        private GameplayRunController controller;
        private SessionTaskTracker tracker;
        private SessionScore score = new SessionScore(0, 0, 0, 0);
        private readonly Dictionary<TMP_Text, string> authoredLabels = new Dictionary<TMP_Text, string>();

        public bool IsConfigured => pauseButton != null && pausePanel != null &&
            resultPanel != null && pausePanel != resultPanel &&
            outcomeText != null && durationText != null;

        private void Awake()
        {
            reducedFeedback = reducedFeedback || GameSessionSettings.UseReducedFeedback ||
                GameSessionSettings.PainRating.GetValueOrDefault() >= 7 || GameSessionSettings.StressRating.GetValueOrDefault() >= 7;
            CacheAuthoredLabels();
            if (IsConfigured) HidePanels();
        }

        public void BindRun(GameplayRunController owner, SessionTaskTracker tasks)
        {
            CacheAuthoredLabels();
            Unsubscribe();
            controller = owner;
            displayedDurationSecond = -1;
            tracker = tasks;
            tracker.ScoreChanged += RefreshScore;
            RefreshScore();
            SetData(hudModeText, (GameSessionSettings.SelectedMode ?? GameMode.Standard).ToString());
            SetData(hudDifficultyText, GameSessionSettings.SelectedMode == GameMode.Endless
                ? "Dynamic" : owner.TrackDifficulty.ToString());
            ApplyFeedbackVisibility();
        }

        public void HidePanels()
        {
            displayedDurationSecond = -1;
            pausePanel.SetActive(false);
            resultPanel.SetActive(false);
            if (gameplayHUD != null) gameplayHUD.SetActive(true);
            pauseButton.SetActive(true);
            ApplyFeedbackVisibility();
        }

        public void ShowPause()
        {
            resultPanel.SetActive(false);
            pauseButton.SetActive(false);
            pausePanel.SetActive(true);
        }

        internal void ShowEndingPresentation()
        {
            displayedDurationSecond = -1;
            pausePanel.SetActive(false);
            resultPanel.SetActive(false);
            pauseButton.SetActive(false);
            if (gameplayHUD != null) gameplayHUD.SetActive(false);
        }

        public void ShowResult(SessionEndInfo info)
        {
            displayedResult = info;
            pausePanel.SetActive(false);
            pauseButton.SetActive(false);
            if (gameplayHUD != null) gameplayHUD.SetActive(false);
            SetData(outcomeText, "(" + info.Outcome + ")");
            SetData(durationText, FormatDuration(info.ActiveDurationSeconds));
            score = info.Score;
            SetData(navigationPercentText, Percent(score.SafeNavigationPercent));
            SetData(navigationRatioText, $"{score.SuccessfulSteeringTaskCount} / {score.SteeringTaskCount}");
            SetData(jumpPercentText, Percent(score.JumpAndLandingPercent));
            SetData(jumpRatioText, $"{score.SuccessfulJumpLandingCount} / {score.RequiredJumpOpportunityCount}");
            SetData(overallText, Percent(score.OverallPercent));
            SetData(scoreFormulaText, "\n" + (!score.OverallPercent.HasValue ? "Insufficient session data"
                    : score.JumpAndLandingPercent.HasValue
                        ? $"= ({Number(score.SafeNavigationPercent.Value)} × 60%) + ({Number(score.JumpAndLandingPercent.Value)} × 40%)"
                        : "No required jumps: Overall Score = Safe Navigation"));
            RefreshAvatarCoins();
            resultPanel.SetActive(true);
            ApplyFeedbackVisibility();
            if (avatarPresenter != null) avatarPresenter.Show(info, reducedFeedback);
        }

        public void RefreshAvatarCoins()
        {
            if (displayedResult == null) return;
            SetData(coinsText, displayedResult.AvatarProgressSaved
                ? displayedResult.AvatarCoinsCredited.ToString(CultureInfo.InvariantCulture)
                : "Not saved - retry below");
        }

        public void SetReducedFeedback(bool value)
        {
            reducedFeedback = value || GameSessionSettings.PainRating.GetValueOrDefault() >= 7 || GameSessionSettings.StressRating.GetValueOrDefault() >= 7;
            ApplyFeedbackVisibility();
            if (displayedResult != null && resultPanel.activeSelf && avatarPresenter != null) avatarPresenter.Show(displayedResult, reducedFeedback);
        }

        public void RefreshDuration()
        {
            if (controller == null || hudDurationText == null || controller.State == SessionFlowState.Results) return;
            long second = TimeSpan.FromSeconds(Math.Max(0d, controller.ActiveDurationSeconds)).Ticks / TimeSpan.TicksPerSecond;
            if (second == displayedDurationSecond && displayedDurationLabel == hudDurationText) return;
            displayedDurationSecond = second;
            displayedDurationLabel = hudDurationText;
            SetData(hudDurationText, FormatDuration(controller.ActiveDurationSeconds));
        }

        private void RefreshScore()
        {
            if (tracker == null) return;
            score = tracker.CurrentScore;
            SetData(hudScoreText, Percent(score.OverallPercent));
        }

        private void CacheAuthoredLabels()
        {
            foreach (TMP_Text label in new[] { outcomeText, durationText, hudScoreText, hudDurationText,
                hudModeText, hudDifficultyText, navigationPercentText, navigationRatioText,
                jumpPercentText, jumpRatioText, overallText, scoreFormulaText, coinsText })
                if (label != null && !authoredLabels.ContainsKey(label)) authoredLabels.Add(label, label.text);
        }

        private void SetData(TMP_Text label, string value)
        {
            if (label == null) return;
            if (!authoredLabels.TryGetValue(label, out string prefix))
            {
                prefix = label.text;
                authoredLabels.Add(label, prefix);
            }
            // Never derive a prefix from a previous live value or replace the authored wording.
            string separator = prefix.Length == 0 || char.IsWhiteSpace(prefix[prefix.Length - 1]) ||
                value.StartsWith("\n", StringComparison.Ordinal) ? "" : " ";
            label.text = prefix + separator + value;
        }

        private void ApplyFeedbackVisibility()
        {
            foreach (TMP_Text text in new[] { hudScoreText, hudDurationText, navigationPercentText,
                navigationRatioText, jumpPercentText, jumpRatioText, overallText, scoreFormulaText, coinsText })
                if (text != null) text.gameObject.SetActive(!reducedFeedback);
        }

        private static string Percent(decimal? value) => value.HasValue
            ? decimal.Round(value.Value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
            : "—";
        private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "%";
        private static string FormatDuration(double seconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0d, seconds));
            return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}";
        }

        public void ReturnToMainMenu()
        {
            if (!resultPanel.activeSelf || (controller != null && controller.State != SessionFlowState.Results)) return;
            if (!Application.CanStreamedLevelBeLoaded("MainMenuScene"))
            {
                Debug.LogError("Enable MainMenuScene in the active build scene list.", this);
                return;
            }
            Time.timeScale = 1f;
            SceneManager.LoadScene("MainMenuScene", LoadSceneMode.Single);
        }

        private void Unsubscribe()
        {
            if (tracker != null) tracker.ScoreChanged -= RefreshScore;
        }
        private void OnDestroy() => Unsubscribe();
    }
}
