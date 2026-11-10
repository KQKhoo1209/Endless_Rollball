using System;
using TMPro;
using UnityEngine;
using EndlessRollball.UI;

namespace EndlessRollball.Avatars
{
    public sealed class AvatarMenuUI : MonoBehaviour
    {
        [Serializable] private sealed class AvatarCard
        {
            public string avatarId;
            public UnityEngine.UI.Button button;
            public TMP_Text label, status;
            public GameObject selectedIndicator, lockedIndicator;
        }
        [Serializable] private sealed class ItemRow
        {
            public string slot;
            public GameObject root;
            public UnityEngine.UI.Button useDefault, option;
            public TMP_Text defaultLabel, optionLabel;
            public TMP_Text defaultAction, optionAction, defaultState, optionState;
            public GameObject defaultIndicator, optionIndicator;
        }
        [SerializeField] private GameObject mainPage, selectionPage, customizationPage;
        [SerializeField] private AvatarPreview selectionPreview, customizationPreview;
        [SerializeField] private TMP_Text selectionTitle, selectionBalance, selectionStatus, customizationTitle, customizationBalance, customizationStatus;
        [SerializeField] private AvatarCard[] cards;
        [SerializeField] private ItemRow[] rows;
        [SerializeField] private UnityEngine.UI.Button customizeButton, saveButton;
        [SerializeField] private TMP_Text customizationAvatarName, draftStatus;
        private AvatarAppearance draft;
        private AvatarProgressRepository Store => AvatarProgressRepository.Instance;

        private void Awake()
        {
            foreach (var card in cards) { var id = card.avatarId; card.button.onClick.AddListener(() => Select(id)); }
            foreach (var row in rows)
            {
                var captured = row;
                row.useDefault.onClick.AddListener(() => UseDefault(captured.slot));
                row.option.onClick.AddListener(() => Choose(captured.slot));
            }
        }
        public void Open()
        {
            mainPage.SetActive(false); customizationPage.SetActive(false); selectionPage.SetActive(true);
            RefreshSelection();
        }
        public void Close()
        { selectionPage.SetActive(false); customizationPage.SetActive(false); mainPage.SetActive(true); }
        private void Select(string id)
        {
            UIButtonClickAudio.PlayDefault();
            if (!Store.IsUnlocked(id)) { selectionStatus.text = AvatarCatalog.Requirement(id); return; }
            bool saved = Store.Select(id); RefreshSelection();
            selectionStatus.text = saved ? "Selected for your next session result." : Store.Error;
        }
        private void RefreshSelection()
        {
            selectionTitle.text = AvatarCatalog.Name(Store.Selected);
            selectionBalance.text = "Avatar Coins: " + Store.Balance;
            selectionStatus.text = Store.IsAvailable ? "Choose a companion to share feedback after your session." : Store.Error;
            foreach (var card in cards)
            {
                bool unlocked = Store.IsUnlocked(card.avatarId);
                bool selected = Store.Selected == card.avatarId;
                string status = unlocked ? selected ? "Selected" : "Available - Select" : "Locked - " + AvatarCatalog.Requirement(card.avatarId);
                card.label.text = AvatarCatalog.Name(card.avatarId) + (card.status == null ? "\n" + status : "");
                if (card.status != null) card.status.text = status;
                if (card.selectedIndicator != null) card.selectedIndicator.SetActive(selected && unlocked);
                if (card.lockedIndicator != null) card.lockedIndicator.SetActive(!unlocked);
                card.button.interactable = Store.IsAvailable;
            }
            customizeButton.interactable = Store.IsAvailable;
            selectionPreview.Show(Store.Appearance(Store.Selected));
        }
        public void Customize()
        {
            draft = Store.Appearance(Store.Selected);
            selectionPage.SetActive(false); customizationPage.SetActive(true); RefreshCustomization();
            customizationStatus.text = Store.IsAvailable ? "Choose items to preview, then Save Appearance." : Store.Error;
        }
        public void BackToSelection() { draft = null; Open(); }
        public void Save()
        {
            bool saved = Store.SaveAppearance(draft); RefreshCustomization();
            customizationStatus.text = saved ? "Appearance saved for this avatar." : Store.Error;
        }
        public void PreviewReaction() => customizationPreview.React();
        private Cosmetic Item(string slot) => Array.Find(AvatarCatalog.Items, i => i.Avatar == draft.avatarId && i.Slot == slot);
        private void UseDefault(string slot)
        { UIButtonClickAudio.PlayDefault(); draft.Set(slot, ""); RefreshCustomization(); customizationStatus.text = "Preview updated. Save Appearance to keep it."; }
        private void Choose(string slot)
        {
            UIButtonClickAudio.PlayDefault(); var item = Item(slot); if (item == null) return;
            if (!Store.Owns(item.Id))
            {
                bool bought = Store.Purchase(item.Id); RefreshCustomization();
                customizationStatus.text = bought ? "Purchased " + item.Name + ". Select it to preview, then Save Appearance." : Store.Error;
                return;
            }
            draft.Set(slot, item.Id); RefreshCustomization();
            customizationStatus.text = "Preview updated. Save Appearance to keep it.";
        }
        private void RefreshCustomization()
        {
            customizationTitle.text = "Avatar Customization";
            if (customizationAvatarName != null) customizationAvatarName.text = AvatarCatalog.Name(draft.avatarId);
            customizationBalance.text = "Avatar Coins: " + Store.Balance;
            saveButton.interactable = Store.IsAvailable;
            var saved = Store.Appearance(draft.avatarId);
            bool dirty = saved.appearance != draft.appearance || saved.accessory != draft.accessory || saved.effect != draft.effect;
            if (draftStatus != null) draftStatus.text = !Store.IsAvailable ? "Appearance unavailable"
                : dirty ? "Unsaved changes" : "Appearance saved";
            foreach (var row in rows)
            {
                var item = Item(row.slot); row.root.SetActive(item != null); if (item == null) continue;
                bool owned = Store.Owns(item.Id);
                bool previewDefault = string.IsNullOrEmpty(draft.Get(row.slot));
                bool savedDefault = string.IsNullOrEmpty(saved.Get(row.slot));
                bool previewOption = draft.Get(row.slot) == item.Id;
                bool savedOption = saved.Get(row.slot) == item.Id;
                string action = owned ? previewOption ? "Owned - Previewing" : "Owned - Use" : "Buy - " + item.Price + " coins";
                row.defaultLabel.text = row.slot == "appearance" ? "Default Appearance" : "None";
                row.optionLabel.text = item.Name + (row.optionAction == null ? "\n" + action : "");
                if (row.defaultAction != null) row.defaultAction.text = previewDefault ? "Free - Previewing" : "Free - Use";
                if (row.optionAction != null) row.optionAction.text = action;
                if (row.defaultState != null) row.defaultState.text = ChoiceState(savedDefault, previewDefault);
                if (row.optionState != null) row.optionState.text = !owned && Store.Balance < item.Price
                    ? "Need " + (item.Price - Store.Balance) + " more coins" : ChoiceState(savedOption, previewOption);
                if (row.defaultIndicator != null) row.defaultIndicator.SetActive(previewDefault);
                if (row.optionIndicator != null) row.optionIndicator.SetActive(previewOption);
                row.option.interactable = Store.IsAvailable && (owned || Store.Balance >= item.Price);
                row.useDefault.interactable = Store.IsAvailable;
            }
            customizationPreview.Show(draft);
        }

        private static string ChoiceState(bool equipped, bool previewing)
            => equipped ? previewing ? "Equipped" : "Equipped - not in preview" : previewing ? "Previewing" : "";
    }
}
