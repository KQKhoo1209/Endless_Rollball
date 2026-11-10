using EndlessRollball.Track;
using TMPro;
using UnityEngine;

namespace EndlessRollball.Avatars
{
    public sealed class SessionResultAvatarPresenter : MonoBehaviour
    {
        [SerializeField] private AvatarPreview preview;
        [SerializeField] private SessionFlowUI flow;
        [SerializeField] private TMP_Text avatarName, feedback, saveStatus;
        [SerializeField] private UnityEngine.UI.Button retrySaveButton;
        private SessionEndInfo current;
        public void Show(SessionEndInfo info, bool reducedFeedback)
        {
            current = info;
            var appearance = info.Avatar?.Appearance ?? new AvatarAppearance();
            preview.Show(appearance); avatarName.text = AvatarCatalog.Name(appearance.avatarId);
            feedback.text = SessionFeedbackRules.Message(info, reducedFeedback);
            RefreshSaveStatus();
            if (info.Outcome == SessionOutcome.Completed && !reducedFeedback &&
                GameSessionSettings.PainRating.GetValueOrDefault() < 4 && GameSessionSettings.StressRating.GetValueOrDefault() < 4) preview.React();
        }
        public void RetrySave()
        {
            if (current == null) return;
            current.TrySaveAvatarProgress(); RefreshSaveStatus();
            // The score label is updated by the owning flow after a successful retry.
            if (flow != null) flow.RefreshAvatarCoins();
        }
        private void RefreshSaveStatus()
        {
            bool failed = !current.AvatarProgressSaved;
            saveStatus.text = failed ? AvatarProgressRepository.Instance.IsAvailable
                ? "Progress could not be saved. Retry before leaving this result."
                : "Avatar progress is unavailable. Your existing save has been kept." : "";
            saveStatus.gameObject.SetActive(failed);
            retrySaveButton.gameObject.SetActive(failed && AvatarProgressRepository.Instance.IsAvailable);
        }
    }
}
