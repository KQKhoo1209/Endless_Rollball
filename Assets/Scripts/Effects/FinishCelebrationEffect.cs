using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EndlessRollball.Track
{
    // Runtime-only presentation. Never owns the score, physics, finish trigger or scene UI layout.
    internal sealed class FinishCelebrationEffect
    {
        private static readonly float[] BurstTimes =
        {
            0f,
            .55f,
            1.2f,
            1.9f,
            2.6f,
            3.2f,
            3.55f
        };
        private static readonly Color[] Colours =
        {
            new Color(1f, .72f, .15f),
            new Color(.2f, .9f, 1f),
            new Color(1f, .3f, .7f),
            new Color(.4f, 1f, .45f),
            new Color(.65f, .45f, 1f)
        };
        private GameObject fireworkRoot, fadeRoot;
        private ParticleSystem particles;
        private CanvasGroup fade;
        private Material material;
        private Texture2D spark;
        private Vector3 gatePosition;
        private int nextBurst;
        internal void Play(Scene scene, Vector3 gate, float duration)
        {
            Stop();
            gatePosition = gate;
            nextBurst = 0;
            BuildFade(scene);
            if (particles == null)
                BuildParticles(scene);
            if (particles != null)
            {
                fireworkRoot.SetActive(true);
                particles.Play();
                TickFireworks(0f, duration);
            }
        }

        internal void TickFireworks(float elapsed, float duration)
        {
            if (particles == null)
                return;
            float timeScale = Mathf.Max(.05f, duration) / 5f;
            while (nextBurst < BurstTimes.Length && elapsed >= BurstTimes[nextBurst] * timeScale)
            {
                EmitBurst(nextBurst, timeScale);
                nextBurst++;
            }
        }

        internal void SetFade(float alpha)
        {
            if (fade == null)
                return;
            fade.alpha = Mathf.Clamp01(alpha);
            fade.blocksRaycasts = true;
        }

        internal void ClearFireworks()
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (fireworkRoot != null)
                fireworkRoot.SetActive(false);
        }

        internal void Stop()
        {
            ClearFireworks();
            if (fade != null)
            {
                fade.alpha = 0f;
                fade.blocksRaycasts = false;
            }

            if (fadeRoot != null)
                fadeRoot.SetActive(false);
        }

        internal void Dispose()
        {
            Stop();
            if (fireworkRoot != null)
                Object.Destroy(fireworkRoot);
            if (fadeRoot != null)
                Object.Destroy(fadeRoot);
            if (material != null)
                Object.Destroy(material);
            if (spark != null)
                Object.Destroy(spark);
        }

        private void BuildFade(Scene scene)
        {
            if (fadeRoot != null)
            {
                fadeRoot.SetActive(true);
                return;
            }

            int order = 100;
            foreach (Canvas existing in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                if (existing.gameObject.scene == scene)
                    order = Mathf.Max(order, existing.sortingOrder + 10);
            fadeRoot = new GameObject("FinishResultFade_Runtime", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            SceneManager.MoveGameObjectToScene(fadeRoot, scene);
            Canvas canvas = fadeRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = Mathf.Min(32760, order);
            fade = fadeRoot.GetComponent<CanvasGroup>();
            fade.alpha = 0f;
            fade.interactable = false;
            fade.blocksRaycasts = true;
            var cover = new GameObject("BlackCover", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            cover.transform.SetParent(fadeRoot.transform, false);
            RectTransform rect = cover.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = cover.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;
        }

        private void BuildParticles(Scene scene)
        {
            Shader shader = Resources.Load<Shader>("ERB_FinishFirework");
            if (shader == null)
            {
                Debug.LogError("Finish fireworks shader is missing. Refresh/import ERB_FinishFirework.shader.");
                return; // Keep the result transition available even if artwork cannot load.
            }

            spark = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "FinishSpark_Runtime",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                    pixels[y * 32 + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 1.6f));
                }

            spark.SetPixels(pixels);
            spark.Apply(false, true);
            material = new Material(shader)
            {
                name = "FinishFirework_Runtime"
            };
            material.SetTexture("_MainTex", spark);
            fireworkRoot = new GameObject("FinishFireworks_Runtime");
            fireworkRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(fireworkRoot, scene);
            particles = fireworkRoot.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 30f;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = .25f;
            main.maxParticles = 512;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var collision = particles.collision;
            collision.enabled = false;
            var trigger = particles.trigger;
            trigger.enabled = false;
            particles.useAutoRandomSeed = false;
            particles.randomSeed = 41729u;
            var colour = particles.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.85f, .45f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, .25f));
            var trails = particles.trails;
            trails.enabled = true;
            trails.ratio = .75f;
            trails.lifetime = .12f;
            trails.dieWithParticles = true;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.trailMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void EmitBurst(int burst, float timeScale)
        {
            const int count = 96;
            Vector3 position = gatePosition + new Vector3(burst % 2 == 0 ? -2.8f : 2.8f, 3.8f + .4f * (burst % 3), -1.5f);
            Color colour = Colours[burst % Colours.Length];
            for (int i = 0; i < count; i++)
            {
                // Evenly spaced spherical burst; does not consume gameplay or Unity's global RNG.
                float y = 1f - 2f * (i + .5f) / count;
                float radial = Mathf.Sqrt(1f - y * y), angle = i * 2.399963f + burst * .7f;
                Vector3 direction = new Vector3(Mathf.Cos(angle) * radial, y, Mathf.Sin(angle) * radial);
                var particle = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = direction * (3.6f + 1.4f * Mathf.Repeat(i * .618034f, 1f)),
                    startLifetime = (1.2f + .2f * Mathf.Repeat(i * .37f, 1f)) * timeScale,
                    startSize = .14f + .08f * Mathf.Repeat(i * .43f, 1f),
                    startColor = colour
                };
                particles.Emit(particle, 1);
            }
        }
    }
}
