using TMPro;
using UnityEngine;

/// <summary>Temporary self-reported preparation ratings; no persistence or scoring.</summary>
public sealed class PainStressCheckUI : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Slider painSlider;
    [SerializeField] private UnityEngine.UI.Slider stressSlider;
    [SerializeField] private TMP_Text painValue;
    [SerializeField] private TMP_Text stressValue;
    [SerializeField] private TMP_Text supportMessage;
    [SerializeField] private TMP_Text feedbackMessage;
    [SerializeField] private UnityEngine.UI.Button normalButton;
    [SerializeField] private UnityEngine.UI.Button reducedButton;
    [SerializeField] private UnityEngine.UI.Button continueButton;
    [SerializeField] private UnityEngine.UI.Image restBackground;
    [SerializeField] private TMP_Text restLabel;
    [SerializeField] private GameObject sensorPage;
    [SerializeField] private GameObject guidePage;
    [SerializeField] private GameObject mainMenuPage;
    [SerializeField] private UnityEngine.UI.Button buttonStyleReference;
    [SerializeField] private GameObject normalSelectedIndicator;
    [SerializeField] private GameObject reducedSelectedIndicator;
    [SerializeField] private PreparationFlowController preparationFlow;
    private int? pain = 0;
    private int? stress = 0;
    private bool preferReduced;
    private bool ForceReduced => pain.GetValueOrDefault() >= 7 || stress.GetValueOrDefault() >= 7;
    private bool HighPain => pain.GetValueOrDefault() >= 7;
    private bool BothAnswered => pain.HasValue && stress.HasValue;

    public void OpenFresh()
    {
        if (preparationFlow != null) { preparationFlow.PreviewRatings(); return; }
        GameSessionSettings.ResetWellbeing();
        pain = stress = 0;
        preferReduced = false;
        painSlider.SetValueWithoutNotify(0);
        stressSlider.SetValueWithoutNotify(0);
        if (mainMenuPage != null) mainMenuPage.SetActive(false);
        if (sensorPage != null) sensorPage.SetActive(false);
        if (guidePage != null) guidePage.SetActive(false);
        gameObject.SetActive(true);
        Refresh();
    }

    public void SetPain(float value)
    {
        pain = Mathf.Clamp(Mathf.RoundToInt(value), 0, 10);
        painSlider.SetValueWithoutNotify(pain.Value);
        Refresh();
    }

    public void SetStress(float value)
    {
        stress = Mathf.Clamp(Mathf.RoundToInt(value), 0, 10);
        stressSlider.SetValueWithoutNotify(stress.Value);
        Refresh();
    }

    public void DecreasePain() => SetPain(pain.GetValueOrDefault() - 1);
    public void IncreasePain() => SetPain(pain.GetValueOrDefault() + 1);
    public void DecreaseStress() => SetStress(stress.GetValueOrDefault() - 1);
    public void IncreaseStress() => SetStress(stress.GetValueOrDefault() + 1);
    public void SelectNormal() { if (!ForceReduced) preferReduced = false; Refresh(); }
    public void SelectReduced() { preferReduced = true; Refresh(); }

    public void Back()
    {
        if (preparationFlow != null) { preparationFlow.ShowConnection(); return; }
        gameObject.SetActive(false);
        if (sensorPage != null) sensorPage.SetActive(true);
    }

    public void RestAndReturn()
    {
        if (preparationFlow != null) { preparationFlow.RestAndReturn(); return; }
        GameSessionSettings.ResetWellbeing();
        EndlessRollball.Imu.ImuRuntimeSession.Instance?.Disconnect();
        pain = stress = 0;
        preferReduced = false;
        gameObject.SetActive(false);
        if (sensorPage != null) sensorPage.SetActive(false);
        if (guidePage != null) guidePage.SetActive(false);
        if (mainMenuPage != null) mainMenuPage.SetActive(true);
    }

    public void Continue()
    {
        if (!BothAnswered || HighPain) return;
        GameSessionSettings.PainRating = pain;
        GameSessionSettings.StressRating = stress;
        GameSessionSettings.UseReducedFeedback = ForceReduced || preferReduced;
        if (preparationFlow != null) { preparationFlow.ShowPainting(); return; }
        gameObject.SetActive(false);
        if (guidePage != null) guidePage.SetActive(true);
    }

    private void OnEnable() => Refresh();

    public void ResetRatings()
    {
        pain = stress = 0;
        preferReduced = false;
        painSlider.SetValueWithoutNotify(0);
        stressSlider.SetValueWithoutNotify(0);
        Refresh();
    }

    private void Refresh()
    {
        if (painValue == null || stressValue == null) return;
        painValue.text = pain.GetValueOrDefault() + " / 10";
        stressValue.text = stress.GetValueOrDefault() + " / 10";
        bool reduced = ForceReduced || preferReduced;
        normalButton.interactable = !ForceReduced;
        ApplyButtonStyle(normalButton);
        ApplyButtonStyle(reducedButton);
        if (normalSelectedIndicator != null) normalSelectedIndicator.SetActive(!reduced);
        if (reducedSelectedIndicator != null) reducedSelectedIndicator.SetActive(reduced);
        continueButton.interactable = BothAnswered && !HighPain;
        ApplyButtonStyle(continueButton);
        if (buttonStyleReference != null)
        {
            restBackground.color = buttonStyleReference.GetComponent<UnityEngine.UI.Image>().color;
            restLabel.color = buttonStyleReference.GetComponentInChildren<TMP_Text>().color;
        }
        restLabel.text = "Rest and Return";
        feedbackMessage.text = ForceReduced
            ? "Reduced Feedback selected for this session because a rating is 7 or above."
            : reduced ? "Reduced Feedback selected. Scores and route progress will be hidden."
            : "Normal Feedback selected. Scores and route progress will be shown.";
        supportMessage.text = HighPain ? "Your comfort comes first. Please choose Rest and Return."
            : !BothAnswered ? "Please answer both ratings. Take your time; you can rest instead."
            : pain.Value >= 4 && stress.Value >= 4 ? "Begin only when comfortable. You can rest instead."
            : pain.Value >= 4 ? "Use only movements that feel comfortable. You can pause or stop."
            : stress.Value >= 4 ? "Take your time to get ready. There is no score target to reach."
            : "Thank you for checking in. Continue when you feel comfortable.";
    }

    private void ApplyButtonStyle(UnityEngine.UI.Button button)
    {
        if (EndlessRollball.UI.UIButtonStyle.ApplyShared(button)) return;
        if (buttonStyleReference == null) return;
        button.GetComponent<UnityEngine.UI.Image>().color = buttonStyleReference.GetComponent<UnityEngine.UI.Image>().color;
        button.GetComponentInChildren<TMP_Text>().color = buttonStyleReference.GetComponentInChildren<TMP_Text>().color;
        button.colors = buttonStyleReference.colors;
    }
}
