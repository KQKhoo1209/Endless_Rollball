using EndlessRollball.Track;
using UnityEngine;
using UnityEngine.Rendering;

namespace EndlessRollball.Scenery
{
    public sealed partial class EnvironmentManager
    {
#if UNITY_EDITOR
        [Header("Editor-only environment review")]
        [SerializeField] private bool useReviewThemeOverride;
        [SerializeField] private EnvironmentTheme reviewTheme =
            EnvironmentTheme.Street;
#endif

        public EnvironmentTheme CurrentTheme { get; private set; }

        private sealed class PresentationSnapshot
        {
            public Material Skybox;
            public AmbientMode AmbientMode;
            public Color AmbientLight;
            public Color AmbientSky;
            public Color AmbientEquator;
            public Color AmbientGround;
            public float AmbientIntensity;
            public bool Fog;

            public Camera Camera;
            public CameraClearFlags ClearFlags;
            public Color Background;

            public Light DirectionalLight;
            public float DirectionalIntensity;
        }

        private PresentationSnapshot savedPresentation;

        private EnvironmentTheme SelectStartingTheme(
            GameMode mode,
            DifficultyMode difficulty,
            int trackSeed)
        {
#if UNITY_EDITOR
            if (useReviewThemeOverride)
                return reviewTheme;
#endif

            if (mode == GameMode.Standard &&
                difficulty == DifficultyMode.Easy)
            {
                return EnvironmentTheme.Street;
            }

            bool useEasyPool =
                mode == GameMode.Endless ||
                difficulty == DifficultyMode.Easy ||
                difficulty == DifficultyMode.Endless;

            int themeCount = useEasyPool ? 2 : 3;

            // Separate from both track selection and scenery shuffling.
            var themeRandom = new System.Random(
                unchecked(trackSeed ^ 0x2468ACE));

            return (EnvironmentTheme)themeRandom.Next(themeCount);
        }

        private void CapturePresentation()
        {
            if (savedPresentation != null)
                return;

            savedPresentation = new PresentationSnapshot
            {
                Skybox = RenderSettings.skybox,
                AmbientMode = RenderSettings.ambientMode,
                AmbientLight = RenderSettings.ambientLight,
                AmbientSky = RenderSettings.ambientSkyColor,
                AmbientEquator = RenderSettings.ambientEquatorColor,
                AmbientGround = RenderSettings.ambientGroundColor,
                AmbientIntensity = RenderSettings.ambientIntensity,
                Fog = RenderSettings.fog,

                Camera = gameplayCamera,
                ClearFlags = gameplayCamera != null
                    ? gameplayCamera.clearFlags
                    : CameraClearFlags.Skybox,
                Background = gameplayCamera != null
                    ? gameplayCamera.backgroundColor
                    : Color.black,

                DirectionalLight = directionalLight,
                DirectionalIntensity = directionalLight != null
                    ? directionalLight.intensity
                    : 0f
            };
        }

        private void ApplyPresentation()
        {
            bool isDungeon =
                CurrentTheme == EnvironmentTheme.Dungeon;

            RenderSettings.skybox = isDungeon
                ? null
                : currentSettings.skyboxMaterial;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = currentSettings.ambientColor;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;

            if (directionalLight != null)
            {
                directionalLight.intensity =
                    currentSettings.directionalLightIntensity;
            }
            else
            {
                Debug.LogWarning(
                    "EnvironmentManager has no directional light reference.",
                    this);
            }

            if (gameplayCamera != null)
            {
                if (isDungeon)
                {
                    gameplayCamera.clearFlags =
                        CameraClearFlags.SolidColor;

                    gameplayCamera.backgroundColor =
                        dungeonBackgroundColor;
                }
                else
                {
                    gameplayCamera.clearFlags =
                        CameraClearFlags.Skybox;
                }
            }
            else
            {
                Debug.LogWarning(
                    "EnvironmentManager has no gameplay camera reference.",
                    this);
            }

            if (!isDungeon && currentSettings.skyboxMaterial == null)
            {
                Debug.LogWarning(
                    $"No skybox material is assigned for {CurrentTheme}.",
                    this);
            }
        }

        private void RestorePresentation()
        {
            if (savedPresentation == null)
                return;

            RenderSettings.skybox = savedPresentation.Skybox;

            RenderSettings.ambientLight =
                savedPresentation.AmbientLight;
            RenderSettings.ambientSkyColor =
                savedPresentation.AmbientSky;
            RenderSettings.ambientEquatorColor =
                savedPresentation.AmbientEquator;
            RenderSettings.ambientGroundColor =
                savedPresentation.AmbientGround;
            RenderSettings.ambientMode =
                savedPresentation.AmbientMode;
            RenderSettings.ambientIntensity =
                savedPresentation.AmbientIntensity;

            RenderSettings.fog = savedPresentation.Fog;

            if (savedPresentation.Camera != null)
            {
                savedPresentation.Camera.clearFlags =
                    savedPresentation.ClearFlags;

                savedPresentation.Camera.backgroundColor =
                    savedPresentation.Background;
            }

            if (savedPresentation.DirectionalLight != null)
            {
                savedPresentation.DirectionalLight.intensity =
                    savedPresentation.DirectionalIntensity;
            }

            savedPresentation = null;
        }
    }
}