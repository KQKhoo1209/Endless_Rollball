using UnityEngine;
using UnityEngine.Rendering;

namespace EndlessRollball
{
    // Authored waterline effects are shared by stationary obstacles. Older prefabs
    // without the authored child retain their original procedural ripple setup.
    [DisallowMultipleComponent]
    public sealed class SurfaceContactRipples : MonoBehaviour
    {
        [Header("Legacy ripple settings")]
        [SerializeField] private Material rippleMaterial;
        [SerializeField] private Material surfaceMaterial;
        [SerializeField] private Vector3 surfaceOffset = new Vector3(0, .08f, 0);
        [SerializeField, Min(.1f)] private float lifetime = 2f;
        [SerializeField, Min(.1f)] private float ringsPerSecond = 1f;
        [SerializeField, Min(.1f)] private float initialDiameter = .8f;
        [SerializeField, Min(1f)] private float expansion = 3f;

        private ParticleSystem[] effects;
        private Transform effectRoot;
        private float surfaceLift;
        private static readonly int Height = Shader.PropertyToID("_WaveHeight");
        private static readonly int Frequency = Shader.PropertyToID("_WaveFrequency");
        private static readonly int Speed = Shader.PropertyToID("_WaveSpeed");

        private void OnEnable() => RestartAtWaterline(surfaceMaterial);

        internal void RestartAtWaterline(Material activeSurface, float waterY = float.NaN)
        {
            if (activeSurface != null)
                surfaceMaterial = activeSurface;

            if (effects == null)
            {
                Transform authored = transform.Find("Waterline Contact");
                if (authored != null)
                {
                    effectRoot = authored;
                    effects = authored.GetComponentsInChildren<ParticleSystem>(true);
                }
                else if (rippleMaterial != null)
                {
                    effects = new[] { BuildLegacyRipple() };
                    effectRoot = effects[0].transform;
                }
                else
                    return;
                surfaceLift = effectRoot.position.y - transform.position.y;
            }
            if (!float.IsNaN(waterY))
            {
                Vector3 position = effectRoot.position;
                position.y = waterY + surfaceLift;
                effectRoot.position = position;
            }

            // The course may reposition pooled obstacles after enabling them.
            // Clear at the final placement so no particles remain at the old position.
            var properties = new MaterialPropertyBlock();
            if (surfaceMaterial != null)
            {
                if (surfaceMaterial.HasProperty(Height))
                    properties.SetFloat(Height, surfaceMaterial.GetFloat(Height));
                if (surfaceMaterial.HasProperty(Frequency))
                    properties.SetFloat(Frequency, surfaceMaterial.GetFloat(Frequency));
                if (surfaceMaterial.HasProperty(Speed))
                    properties.SetFloat(Speed, surfaceMaterial.GetFloat(Speed));
            }

            foreach (ParticleSystem effect in effects)
            {
                effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                effect.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(properties);
                if (effect.gameObject.activeInHierarchy)
                    effect.Play(false);
            }
        }

        private ParticleSystem BuildLegacyRipple()
        {
            var child = new GameObject("Surface Contact Ripples");
            child.transform.SetParent(transform, false);
            child.transform.localPosition = surfaceOffset;
            var particles = child.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = lifetime;
            main.startLifetime = lifetime;
            main.startSpeed = 0;
            main.startSize = initialDiameter;
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = false;
            main.maxParticles = 16;

            var emission = particles.emission;
            emission.rateOverTime = ringsPerSecond;
            var shape = particles.shape;
            shape.enabled = false;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .3f, 1, expansion));

            var colour = particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.8f, .15f), new GradientAlphaKey(0, 1) });
            colour.color = gradient;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = rippleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.minParticleSize = 0;
            renderer.maxParticleSize = 1;
            return particles;
        }

        private void OnDisable()
        {
            if (effects == null) return;
            foreach (ParticleSystem effect in effects)
                effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
