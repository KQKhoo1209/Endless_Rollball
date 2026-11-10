using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    // Authored on pooled obstacle prefabs; event timing belongs to their encounter owner.
    [DisallowMultipleComponent]
    public sealed class DungeonObstacleAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource primary;
        [SerializeField] private AudioSource landing;
        [SerializeField] private AudioClip ambientClip;
        [SerializeField] private AudioClip warningClip;
        [SerializeField] private AudioClip motionClip;
        [SerializeField] private AudioClip landingClip;
        [SerializeField] private bool loopWarning;
        [SerializeField, Range(.1f, 3f)] private float warningPitch = 1f;
        [SerializeField, Range(.1f, 3f)] private float motionPitch = 1f;
        [SerializeField, Range(0f, 1f)] private float volume = .75f;
        private GameplayRunController runController;
        private bool running = true;
        private bool paused;

        private void Awake()
        {
            runController = FindFirstObjectByType<GameplayRunController>();
            Configure(primary);
            Configure(landing);
            Preload(ambientClip); Preload(warningClip);
            Preload(motionClip); Preload(landingClip);
        }

        private static void Configure(AudioSource source)
        {
            if (source == null) return;
            source.playOnAwake = false;
            source.dopplerLevel = 0f;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 8f;
            source.maxDistance = 55f;
            source.Stop();
        }

        private static void Preload(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }

        public void ResetAudio()
        {
            StopAudio();
            running = true;
            Play(primary, ambientClip, 1f, true);
        }

        public void SetRunning(bool value)
        {
            running = value;
            Refresh();
        }

        public void BeginWarning() => Play(primary, warningClip, warningPitch, loopWarning);
        public void BeginMotion() => Play(primary, motionClip, motionPitch, false);
        public void SetLoopAudible(bool value)
        {
            if (primary != null) primary.mute = !value;
        }

        public void Land()
        {
            if (primary != null) primary.Stop();
            Play(landing, landingClip, 1f, false);
        }

        private void Play(AudioSource source, AudioClip clip, float pitch, bool loop)
        {
            if (!isActiveAndEnabled || source == null || clip == null) return;
            source.Stop();
            source.clip = clip;
            source.pitch = pitch;
            source.loop = loop;
            Refresh();
            source.Play();
            if (paused) source.Pause();
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            float gain = volume * Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
            if (primary != null) primary.volume = gain;
            if (landing != null) landing.volume = gain;
            bool recovery = runController != null &&
                (runController.State == SessionFlowState.Recovering || runController.State == SessionFlowState.Ending);
            bool shouldPause = !running ||
                (runController != null && runController.State == SessionFlowState.Paused) ||
                (Time.timeScale <= 0f && !recovery);
            if (paused == shouldPause) return;
            paused = shouldPause;
            SetPaused(primary); SetPaused(landing);
        }

        private void SetPaused(AudioSource source)
        {
            if (source == null) return;
            if (paused) source.Pause(); else source.UnPause();
        }

        public void StopAudio()
        {
            if (primary != null) primary.Stop();
            if (landing != null) landing.Stop();
            paused = false;
        }

        private void OnDisable() => StopAudio();
    }
}
