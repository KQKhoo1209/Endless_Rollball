using UnityEngine;

namespace EndlessRollball
{
    // An independent, silent instance of the existing water spray/ripple effect.
    // Entrance splashes must not trigger the whale's damage flinch or impact sound.
    internal sealed class CarrierEntranceSplash
    {
        private readonly Transform root;
        private readonly ParticleSystem[] particles;
        private readonly ParticleSystemRenderer[] renderers;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private static readonly int[] WaveProperties =
        {
            Shader.PropertyToID("_WaveHeight"), Shader.PropertyToID("_WaveFrequency"),
            Shader.PropertyToID("_WaveSpeed")
        };

        private CarrierEntranceSplash(GameObject instance)
        {
            root = instance.transform;
            root.name = "Breach Water Splash";
            foreach (var audio in instance.GetComponentsInChildren<AudioSource>(true))
            {
                audio.Stop();
                audio.enabled = false;
            }
            particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            renderers = instance.GetComponentsInChildren<ParticleSystemRenderer>(true);
            foreach (var system in particles)
            {
                var main = system.main;
                main.useUnscaledTime = false;
            }
            Clear();
        }

        internal static CarrierEntranceSplash Create(Transform owner)
        {
            var prefab = Resources.Load<GameObject>("WaterEffects/WhaleHitFeedback");
            return prefab == null ? null :
                new CarrierEntranceSplash(Object.Instantiate(prefab, owner));
        }

        internal void Play(Vector3 position, float scale, Material surface)
        {
            Clear();
            root.SetPositionAndRotation(position, Quaternion.identity);
            root.localScale = Vector3.one * scale;
            properties.Clear();
            if (surface != null)
                foreach (int property in WaveProperties)
                    if (surface.HasProperty(property))
                        properties.SetFloat(property, surface.GetFloat(property));
            foreach (var renderer in renderers) renderer.SetPropertyBlock(properties);
            foreach (var system in particles) system.Play(false);
        }

        internal void Clear()
        {
            foreach (var system in particles)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
