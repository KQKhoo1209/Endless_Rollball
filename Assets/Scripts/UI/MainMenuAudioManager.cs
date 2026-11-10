using UnityEngine;

namespace EndlessRollball.UI
{
    [DisallowMultipleComponent]
    public sealed class MainMenuAudioManager : MonoBehaviour
    {
        private const string MasterKey = "MenuAudio.Master";
        private const string MusicKey = "MenuAudio.Music";
        private const string EffectsKey = "MenuAudio.Effects";

        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioClip menuMusic;
        [SerializeField] private AudioClip buttonClick;
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float musicVolume = .3f;
        [SerializeField, Range(0f, 1f)] private float effectsVolume = .45f;
        [SerializeField] private UnityEngine.UI.Slider masterSlider;
        [SerializeField] private UnityEngine.UI.Slider musicSlider;
        [SerializeField] private UnityEngine.UI.Slider effectsSlider;

        private void Awake()
        {
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, masterVolume));
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, musicVolume));
            effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, effectsVolume));
            if (musicSource != null)
            {
                musicSource.playOnAwake = false;
                musicSource.spatialBlend = 0f;
                musicSource.dopplerLevel = 0f;
                musicSource.loop = true;
                musicSource.clip = menuMusic;
            }
            if (masterSlider != null) masterSlider.SetValueWithoutNotify(masterVolume);
            if (musicSlider != null) musicSlider.SetValueWithoutNotify(musicVolume);
            if (effectsSlider != null) effectsSlider.SetValueWithoutNotify(effectsVolume);
            ApplyVolumes();
        }

        private void OnEnable()
        {
            if (masterSlider != null) masterSlider.onValueChanged.AddListener(SetMasterVolume);
            if (musicSlider != null) musicSlider.onValueChanged.AddListener(SetMusicVolume);
            if (effectsSlider != null) effectsSlider.onValueChanged.AddListener(SetEffectsVolume);
            if (musicSource != null && menuMusic != null) musicSource.Play();
        }

        public void PlayButtonClick()
        {
            UIButtonClickAudio.Play(buttonClick, masterVolume * effectsVolume);
        }

        public void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterKey, masterVolume);
            ApplyVolumes();
        }

        public void SetMusicVolume(float value)
        {
            musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicKey, musicVolume);
            ApplyVolumes();
        }

        public void SetEffectsVolume(float value)
        {
            effectsVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(EffectsKey, effectsVolume);
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            if (musicSource != null) musicSource.volume = masterVolume * musicVolume;
            UIButtonClickAudio.SetVolume(masterVolume * effectsVolume);
        }

        private void OnDisable()
        {
            if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(SetMasterVolume);
            if (musicSlider != null) musicSlider.onValueChanged.RemoveListener(SetMusicVolume);
            if (effectsSlider != null) effectsSlider.onValueChanged.RemoveListener(SetEffectsVolume);
            if (musicSource != null) musicSource.Stop();
            PlayerPrefs.Save();
        }
    }
}
