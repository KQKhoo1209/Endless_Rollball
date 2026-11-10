using EndlessRollball.Scenery;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    public sealed class BeachGameplayAudio : MonoBehaviour
    {
        public static BeachGameplayAudio Instance { get; private set; }
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private EnvironmentManager environmentManager;
        [SerializeField] private WhaleSurfController whale;
        [SerializeField] private AudioSource seaSource, surfSource, impactSource, hornSource;
        [SerializeField] private AudioSource woodSource;
        [SerializeField] private AudioSource crashSource;
        [SerializeField] private AudioClip seaAmbience, waterSurfing, palmLeaves, explosion, woodHit, boatHorn, boatCrash;
        [SerializeField, Range(0f, 1f)] private float seaVolume = .3f;
        [SerializeField, Range(0f, 1f)] private float surfingVolume = .4f;
        [SerializeField, Range(0f, 1f)] private float leavesVolume = .9f;
        [SerializeField, Range(0f, 1f)] private float explosionVolume = .65f;
        [SerializeField, Range(0f, 1f)] private float woodVolume = 1f;
        [SerializeField, Range(0f, 1f)] private float hornVolume = .55f;
        [SerializeField, Range(0f, 1f)] private float crashVolume = .65f;
        [SerializeField, Min(.05f)] private float fadeSeconds = .5f;
        private float gain;
        private bool paused;
        public bool SurfingAudioActive => isActiveAndEnabled && runController != null &&
            runController.State == SessionFlowState.SpecialEvent && whale != null &&
            whale.IsRidingWater && Time.timeScale > 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            Instance = this;
            gain = Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
            Configure(seaSource, seaAmbience, true);
            Configure(surfSource, waterSurfing, true);
            Configure(impactSource, null, false);
            Configure(hornSource, boatHorn, false);
            Configure(woodSource, woodHit, false);
            Configure(crashSource, boatCrash, false);
            foreach (AudioClip clip in new[] { seaAmbience, waterSurfing, palmLeaves, explosion, woodHit, boatHorn, boatCrash })
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        private static void Configure(AudioSource source, AudioClip clip, bool loop)
        {
            if (source == null) return;
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.clip = clip;
            source.volume = loop ? 0f : 1f;
            source.Stop();
        }

        private void Update()
        {
            // Failure recovery intentionally freezes physics while its hit sound
            // and presentation continue. Only an actual pause freezes this audio.
            bool recoveryPresentation = runController != null &&
                (runController.State == SessionFlowState.Recovering ||
                 runController.State == SessionFlowState.Ending);
            bool shouldPause = (runController != null && runController.State == SessionFlowState.Paused) ||
                (Time.timeScale <= 0f && !recoveryPresentation);
            if (shouldPause != paused)
            {
                paused = shouldPause;
                PauseSource(seaSource); PauseSource(surfSource);
                PauseSource(impactSource); PauseSource(hornSource);
                PauseSource(woodSource);
                PauseSource(crashSource);
            }
            if (paused) return;
            bool inBeach = environmentManager != null && environmentManager.CurrentTheme == EnvironmentTheme.Water;
            bool active = runController != null && (runController.State == SessionFlowState.Running ||
                runController.State == SessionFlowState.SpecialEvent || runController.State == SessionFlowState.Recovering);
            FadeLoop(seaSource, inBeach && active ? seaVolume * gain : 0f);
            FadeLoop(surfSource, inBeach && SurfingAudioActive ? surfingVolume * gain : 0f, true);
        }

        private void PauseSource(AudioSource source)
        {
            if (source == null) return;
            if (paused) source.Pause(); else source.UnPause();
        }

        private void FadeLoop(AudioSource source, float target, bool immediateStart = false)
        {
            if (source == null || source.clip == null) return;
            if (immediateStart && target > 0f && !source.isPlaying)
                source.volume = target;
            source.volume = Mathf.MoveTowards(source.volume, target,
                Time.unscaledDeltaTime * gain / Mathf.Max(.05f, fadeSeconds));
            if (target > 0f && !source.isPlaying) source.Play();
            else if (target <= 0f && source.volume <= 0f && source.isPlaying) source.Stop();
        }

        private void PlayImpact(AudioClip clip, float volume)
        {
            if (isActiveAndEnabled && !paused && impactSource != null && clip != null && gain > 0f)
                impactSource.PlayOneShot(clip, volume * gain);
        }

        public void PlayLeaves() => PlayImpact(palmLeaves, leavesVolume);
        public void PlayExplosion() => PlayImpact(explosion, explosionVolume);
        public void PlayWood()
        {
            if (!isActiveAndEnabled || paused || woodHit == null || gain <= 0f) return;
            if (woodSource == null)
            {
                PlayImpact(woodHit, woodVolume);
                return;
            }
            woodSource.clip = woodHit;
            woodSource.volume = woodVolume * gain;
            woodSource.Stop();
            woodSource.Play();
        }

        public void StartSurfingAudio()
        {
            if (!SurfingAudioActive || paused || surfSource == null || surfSource.clip == null || gain <= 0f) return;
            surfSource.volume = surfingVolume * gain;
            if (!surfSource.isPlaying) surfSource.Play();
        }
        public void PlayBoatCrash()
        {
            if (!isActiveAndEnabled || paused || boatCrash == null || gain <= 0f) return;
            if (crashSource == null)
            {
                PlayImpact(boatCrash, crashVolume);
                return;
            }
            crashSource.clip = boatCrash;
            crashSource.volume = crashVolume * gain;
            crashSource.Stop();
            crashSource.Play();
        }

        public bool TryBoatHorn()
        {
            if (!SurfingAudioActive || paused || hornSource == null || boatHorn == null ||
                hornSource.isPlaying || gain <= 0f) return false;
            hornSource.clip = boatHorn;
            hornSource.volume = hornVolume * gain;
            hornSource.Play();
            return true;
        }

        private void OnDisable()
        {
            seaSource?.Stop(); surfSource?.Stop(); impactSource?.Stop(); hornSource?.Stop();
            woodSource?.Stop();
            crashSource?.Stop();
            paused = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
