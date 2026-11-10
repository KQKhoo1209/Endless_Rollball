using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    public sealed class EventSpawnAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource source;
        [SerializeField] private AudioClip clip;
        [SerializeField, Range(0f, 1f)] private float volume = .65f;
        [SerializeField, Range(.5f, 2f)] private float pitch = 1f;
        private GameplayRunController runController;
        private bool paused;

        private void Awake()
        {
            runController = FindFirstObjectByType<GameplayRunController>();
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if (source == null) return;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.clip = clip;
            source.pitch = pitch;
            source.Stop();
        }

        public void Play()
        {
            if (!isActiveAndEnabled || source == null || clip == null) return;
            source.volume = volume * Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
            source.Stop();
            source.Play();
            paused = false;
            RefreshPause();
        }

        private void Update() => RefreshPause();

        private void RefreshPause()
        {
            if (source == null) return;
            bool recovery = runController != null &&
                (runController.State == SessionFlowState.Recovering || runController.State == SessionFlowState.Ending);
            bool shouldPause = (runController != null && runController.State == SessionFlowState.Paused) ||
                (Time.timeScale <= 0f && !recovery);
            if (shouldPause == paused) return;
            paused = shouldPause;
            if (paused) source.Pause(); else source.UnPause();
        }

        private void OnDisable()
        {
            if (source != null) source.Stop();
            paused = false;
        }
    }
}
