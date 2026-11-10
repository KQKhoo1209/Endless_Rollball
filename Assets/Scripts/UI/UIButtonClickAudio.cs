using UnityEngine;

namespace EndlessRollball.UI
{
    // Scene/prefab relay plus one shared voice that survives scene-changing buttons.
    public sealed class UIButtonClickAudio : MonoBehaviour
    {
        [SerializeField] private AudioClip clickClip;
        private static AudioClip defaultClip;
        private static AudioSource source;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            defaultClip = null;
            source = null;
        }

        private void Awake()
        {
            if (clickClip == null) return;
            defaultClip = clickClip;
            if (clickClip.loadState == AudioDataLoadState.Unloaded) clickClip.LoadAudioData();
        }

        public void PlayClick() => Play(clickClip);
        public static void PlayDefault() => Play();

        public static void Play(AudioClip clip = null, float volume = -1f)
        {
            if (clip == null) clip = defaultClip;
            if (clip == null) return;
            float gain = volume >= 0f ? Mathf.Clamp01(volume) :
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
            if (gain <= 0f) return;
            if (source == null)
            {
                var root = new GameObject("UI Click Audio", typeof(AudioSource));
                DontDestroyOnLoad(root);
                source = root.GetComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                source.ignoreListenerPause = true;
            }
            source.volume = gain;
            source.PlayOneShot(clip);
        }

        public static void SetVolume(float value)
        {
            if (source != null) source.volume = Mathf.Clamp01(value);
        }
    }
}
