using UnityEngine;

namespace EndlessRollball.Avatars
{
    public sealed class AvatarPresentation : MonoBehaviour
    {
        [SerializeField] private GameObject appearanceVariant;
        [SerializeField] private GameObject defaultModel;
        [SerializeField] private GameObject accessory;
        [SerializeField] private GameObject aura;
        private Vector3 origin;
        private float reactionUntil;
        private void Awake() => origin = transform.localPosition;
        public void Apply(AvatarAppearance selection)
        {
            bool alternate = !string.IsNullOrEmpty(selection.appearance);
            if (defaultModel != null) defaultModel.SetActive(!alternate);
            if (appearanceVariant != null) appearanceVariant.SetActive(alternate);
            if (accessory != null) accessory.SetActive(!string.IsNullOrEmpty(selection.accessory));
            if (aura != null) aura.SetActive(!string.IsNullOrEmpty(selection.effect));
        }
        public void React() => reactionUntil = Time.unscaledTime + 1.5f;
        private void Update()
        {
            float t = Time.unscaledTime;
            float wave = Mathf.Sin(t * (t < reactionUntil ? 6f : 1.7f));
            transform.localPosition = origin + Vector3.up * (wave * (t < reactionUntil ? .08f : .025f));
            transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * .5f) * 8f, wave * (t < reactionUntil ? 5f : 1f));
            if (aura != null && aura.activeSelf) aura.transform.Rotate(Vector3.up, 22f * Time.unscaledDeltaTime, Space.Self);
        }
    }
}
