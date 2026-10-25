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
        [Tooltip("Presentation only. Task counting/calculation still run. Later preparation UI can call SetReducedFeedback.")]
        [SerializeField] private bool reducedFeedback;

        private GameplayRunController controller;
        private SessionTaskTracker tracker;
        private SessionScore score = new SessionScore(0, 0, 0, 0);
        private readonly Dictionary<TMP_Text, string> authoredLabels = new Dictionary<TMP_Text, string>();

        public bool IsConfigured => pauseButton != null && pausePanel != null &&
            resultPanel != null && pausePanel != resultPanel &&
            outcomeText != null && durationText != null;

        private void Awake()
        {
            CacheAuthoredLabels();
            if (IsConfigured) HidePanels();
        }

        public void BindRun(GameplayRunController owner, SessionTaskTracker tasks)
        {
            CacheAuthoredLabels();
            Unsubscribe();
            controller = owner;
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
            pausePanel.SetActive(false);
            resultPanel.SetActive(false);
            pauseButton.SetActive(false);
            if (gameplayHUD != null) gameplayHUD.SetActive(false);
        }

        public void ShowResult(SessionEndInfo info)
        {
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
            // A reward candidate is not a credited balance or a successful save.
            SetData(coinsText, score.OverallPercent.HasValue
                ? score.PotentialAvatarCoins.ToString(CultureInfo.InvariantCulture) + " (not yet credited)" : "—");
            resultPanel.SetActive(true);
            ApplyFeedbackVisibility();
        }

        public void SetReducedFeedback(bool value)
        {
            reducedFeedback = value;
            ApplyFeedbackVisibility();
        }

        private void Update()
        {
            if (controller == null || hudDurationText == null || resultPanel.activeSelf) return;
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
