using UnityEngine;

namespace EndlessRollball
{
    // Reuses a single impact effect and restores the visual pose after each flinch.
    internal sealed class WhaleHitFeedback
    {
        private readonly Transform visual;
        private readonly Vector3 restPosition;
        private readonly Vector3 restScale;
        private readonly Transform effectRoot;
        private readonly ParticleSystem[] particles;
        private readonly ParticleSystemRenderer[] renderers;
        private readonly AudioSource audio;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private static readonly int WaveHeight = Shader.PropertyToID("_WaveHeight");
        private static readonly int WaveFrequency = Shader.PropertyToID("_WaveFrequency");
        private static readonly int WaveSpeed = Shader.PropertyToID("_WaveSpeed");
        private bool reacting;
        private bool audioPaused;
        private float startedAt;
        private float duration;
        private float dip;

        private WhaleHitFeedback(Transform owner, Transform model, GameObject prefab)
        {
            visual = model;
            restPosition = model.localPosition;
            restScale = model.localScale;
            var instance = Object.Instantiate(prefab, owner);
            instance.name = "Whale Hit Feedback";
            effectRoot = instance.transform;
            particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            renderers = instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
            audio = instance.GetComponent<AudioSource>();
            Clear();
        }

        internal static WhaleHitFeedback Create(Transform owner, Transform visual)
        {
            var prefab = Resources.Load<GameObject>("WaterEffects/WhaleHitFeedback");
            if (prefab == null)
            {
                Debug.LogWarning("Assign the WhaleHitFeedback prefab in Resources/WaterEffects.", owner);
                return null;
            }
            return new WhaleHitFeedback(owner, visual, prefab);
        }

        internal void React(Vector3 contact, Material surface, float volume, float flinchSeconds, float depth)
        {
            Clear();
            effectRoot.SetPositionAndRotation(contact, Quaternion.identity);
            properties.Clear();
            if (surface != null)
            {
                if (surface.HasProperty(WaveHeight))
                    properties.SetFloat(WaveHeight, surface.GetFloat(WaveHeight));
                if (surface.HasProperty(WaveFrequency))
                    properties.SetFloat(WaveFrequency, surface.GetFloat(WaveFrequency));
                if (surface.HasProperty(WaveSpeed))
                    properties.SetFloat(WaveSpeed, surface.GetFloat(WaveSpeed));
            }
            foreach (var renderer in renderers)
                renderer.SetPropertyBlock(properties);
            foreach (var system in particles)
                system.Play(false);
            if (audio != null)
            {
                audio.volume = volume;
                audio.Play();
            }
            startedAt = Time.time;
            duration = Mathf.Max(.1f, flinchSeconds);
            dip = depth;
            reacting = true;
        }

        internal void Tick(bool paused)
        {
            if (audio != null)
            {
                if (paused && !audioPaused && audio.isPlaying)
                {
                    audio.Pause();
                    audioPaused = true;
                }
                else if (!paused && audioPaused)
                {
                    audio.UnPause();
                    audioPaused = false;
                }
            }
            if (!reacting || paused) return;

            float progress = Mathf.Clamp01((Time.time - startedAt) / duration);
            float pulse = Mathf.Sin(progress * Mathf.PI);
            visual.localPosition = restPosition + Vector3.down * (dip * pulse);
            visual.localScale = Vector3.Scale(restScale,
                new Vector3(1f + .05f * pulse, 1f - .12f * pulse, 1f + .05f * pulse));
            if (progress >= 1f)
            {
                RestorePose();
                reacting = false;
            }
        }

        internal void Clear()
        {
            RestorePose();
            reacting = false;
            audioPaused = false;
            foreach (var system in particles)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (audio != null)
                audio.Stop();
        }

        private void RestorePose()
        {
            if (visual == null) return;
            visual.localPosition = restPosition;
            visual.localScale = restScale;
        }
    }
}
