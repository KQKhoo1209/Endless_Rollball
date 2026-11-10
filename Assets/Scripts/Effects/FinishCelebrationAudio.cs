using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class FinishCelebrationAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource fireworksSource;
        [SerializeField] private AudioSource finishSource;
        [SerializeField] private AudioClip fireworksClip;
        [SerializeField] private AudioClip finishClip;
        [SerializeField, Range(0f, 1f)] private float fireworksVolume = .5f;
        [SerializeField, Range(0f, 1f)] private float finishVolume = .75f;

        private void Awake()
        {
            Configure(fireworksSource, fireworksClip);
            Configure(finishSource, finishClip);
        }

        private static void Configure(AudioSource source, AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
            if (source == null) return;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            // Finish presentation deliberately freezes the gameplay clock.
            source.ignoreListenerPause = true;
            source.clip = clip;
            source.Stop();
        }

        public void Play()
        {
            Stop();
            float gain = Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
            Play(fireworksSource, fireworksVolume * gain);
            Play(finishSource, finishVolume * gain);
        }

        private static void Play(AudioSource source, float volume)
        {
            if (source == null || source.clip == null || volume <= 0f) return;
            source.volume = volume;
            source.Play();
        }

        public void StopFireworks()
        {
            if (fireworksSource != null) fireworksSource.Stop();
        }

        public void Stop()
        {
            StopFireworks();
            if (finishSource != null) finishSource.Stop();
        }

        private void OnDisable() => Stop();
    }
}
