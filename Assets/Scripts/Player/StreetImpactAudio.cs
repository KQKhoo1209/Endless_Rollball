using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    public sealed class StreetImpactAudio : MonoBehaviour
    {
        [SerializeField] private AudioSource impactSource;
        [SerializeField] private AudioClip carCollision;
        [SerializeField] private AudioClip coneHit;
        [SerializeField] private AudioClip cargoHit;
        [SerializeField] private AudioClip fenceHit;
        [SerializeField] private AudioClip ballLanding;
        [SerializeField] private AudioClip dungeonRockHit;
        [SerializeField] private AudioClip dungeonBurn;
        [SerializeField, Range(0f, 1f)] private float dungeonRockVolume = .75f;
        [SerializeField, Range(0f, 1f)] private float dungeonBurnVolume = .65f;
        [SerializeField, Range(0f, 1f)] private float carVolume = .75f;
        [SerializeField, Range(0f, 1f)] private float coneVolume = .5f;
        [SerializeField, Range(0f, 1f)] private float cargoVolume = .65f;
        [SerializeField, Range(0f, 1f)] private float fenceVolume = .6f;
        [SerializeField, Range(0f, 1f)] private float landingVolume = .4f;
        [SerializeField, Min(.01f)] private float repeatDelay = .15f;
        private readonly Dictionary<int, float> lastHits = new Dictionary<int, float>();

        private void Awake()
        {
            if (impactSource == null) return;
            impactSource.playOnAwake = false;
            impactSource.loop = false;
            impactSource.spatialBlend = 0f;
            impactSource.dopplerLevel = 0f;
            impactSource.volume = 1f;
            Preload(carCollision);
            Preload(coneHit);
            Preload(cargoHit);
            Preload(fenceHit);
            Preload(ballLanding);
            Preload(dungeonRockHit);
            Preload(dungeonBurn);
        }

        private static void Preload(AudioClip clip)
        {
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        }

        public void PlayCar(Collider shape) => Play(carCollision, carVolume, shape);
        public void PlayCone(Collider shape) => Play(coneHit, coneVolume, shape);
        public void PlayCargo(Collider shape) => Play(cargoHit, cargoVolume, shape);
        public void PlayFence(Collider shape) => Play(fenceHit, fenceVolume, shape);
        public void PlayLanding(Collider shape) => Play(ballLanding, landingVolume, shape);
        public void PlayDungeonRock(Collider shape) => Play(dungeonRockHit, dungeonRockVolume, shape);
        public void PlayDungeonBurn(Collider shape) => Play(dungeonBurn, dungeonBurnVolume, shape);

        private void Play(AudioClip clip, float volume, Collider shape)
        {
            if (!isActiveAndEnabled || impactSource == null || clip == null || shape == null) return;
            int key = shape.attachedRigidbody != null
                ? shape.attachedRigidbody.GetInstanceID() : shape.GetInstanceID();
            float now = Time.unscaledTime;
            if (lastHits.TryGetValue(key, out float previous) && now - previous < repeatDelay) return;
            // Colliders are pooled; bound retained contact history over long endless runs.
            if (lastHits.Count >= 64 && !lastHits.ContainsKey(key)) lastHits.Clear();
            lastHits[key] = now;
            float gain = Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
            if (gain > 0f) impactSource.PlayOneShot(clip, volume * gain);
        }

        private void OnDisable()
        {
            lastHits.Clear();
            if (impactSource != null) impactSource.Stop();
        }
    }
}
