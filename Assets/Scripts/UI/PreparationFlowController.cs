using EndlessRollball.Imu;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>One owner for connection, ratings, optional painting and final neutral calibration.</summary>
public sealed class PreparationFlowController : MonoBehaviour
{
    [SerializeField] private SensorCalibrationUI connection;
    [FormerlySerializedAs("sensor")]
    [SerializeField] private SensorCalibrationUI calibration;
    [SerializeField] private PainStressCheckUI ratings;
    [SerializeField] private PaintingSessionUI painting;
    [SerializeField] private GameObject guidePage;
    [SerializeField] private GameObject mainMenuPage;
    private bool finalCalibration;
    public bool IsFinalCalibration => finalCalibration;
    public bool IsConnectionView(SensorCalibrationUI view) => view == connection;

    private void ResetAttempt()
    {
        GameSessionSettings.ResetWellbeing();
        ratings.ResetRatings();
        painting.ResetAttempt();
        ImuRuntimeSession.Ensure().InvalidateCalibration();
        finalCalibration = false;
    }

    private void Show(GameObject page)
    {
        if (connection.gameObject != page) connection.gameObject.SetActive(false);
        if (calibration.gameObject != page) calibration.gameObject.SetActive(false);
        if (ratings.gameObject != page) ratings.gameObject.SetActive(false);
        if (painting.gameObject != page) painting.gameObject.SetActive(false);
        if (guidePage != page) guidePage.SetActive(false);
        if (mainMenuPage != page) mainMenuPage.SetActive(false);
        page.SetActive(true);
    }

    public void Begin() { ResetAttempt(); ShowConnection(); }
    public void PreviewRatings() { ResetAttempt(); ShowRatings(); }
    public void PreviewPainting()
    {
        ResetAttempt();
        GameSessionSettings.PainRating = GameSessionSettings.StressRating = 0;
        ShowPainting();
    }

    public void ShowConnection()
    {
        finalCalibration = false;
        connection.SetPreparationMode(true);
        Show(connection.gameObject);
    }

    public void ShowRatings() { finalCalibration = false; Show(ratings.gameObject); }

    public void ShowPainting()
    {
        if (GameSessionSettings.PainRating.GetValueOrDefault() >= 7) { ShowRatings(); return; }
        finalCalibration = false;
        Show(painting.gameObject);
    }

    public void ShowNeutralCalibration()
    {
        ImuRuntimeSession.Ensure().InvalidateCalibration();
        finalCalibration = true;
        calibration.SetPreparationMode(false);
        Show(calibration.gameObject);
    }

    public void ShowGuide()
    {
        if (!finalCalibration || !ImuRuntimeSession.Ensure().HasFreshInput) return;
        painting.ResetAttempt();
        Show(guidePage);
    }

    public void BackFromSensor(SensorCalibrationUI source)
    {
        if (source == calibration) ShowPainting();
        else RestAndReturn();
    }

    public void RestAndReturn()
    {
        painting.ResetAttempt();
        ratings.ResetRatings();
        GameSessionSettings.ResetWellbeing();
        ImuRuntimeSession.Instance?.Disconnect();
        finalCalibration = false;
        Show(mainMenuPage);
    }
}
