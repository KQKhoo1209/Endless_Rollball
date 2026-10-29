using System.Collections.Generic;
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
        private EnvironmentTheme SelectStartingTheme(GameMode mode, DifficultyMode difficulty, int trackSeed)
        {
#if UNITY_EDITOR
            if (useReviewThemeOverride)
                return reviewTheme;
#endif
            return EnvironmentJourneyPlanner.SelectStartingTheme(mode, difficulty, trackSeed);
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
                ClearFlags = gameplayCamera != null ? gameplayCamera.clearFlags : CameraClearFlags.Skybox,
                Background = gameplayCamera != null ? gameplayCamera.backgroundColor : Color.black,
                DirectionalLight = directionalLight,
                DirectionalIntensity = directionalLight != null ? directionalLight.intensity : 0f
            };
        }

        private void ApplyPresentation()
        {
            bool isDungeon = CurrentTheme == EnvironmentTheme.Dungeon;
            TryGetScenery(CurrentTheme, out EnvironmentScenerySet scenery);
            RenderSettings.skybox = isDungeon ? null : scenery.skybox;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = currentSettings.ambientColor;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            if (directionalLight != null)
            {
                directionalLight.intensity = currentSettings.directionalLightIntensity;
            }
            else
            {
                Debug.LogWarning("EnvironmentManager has no directional light reference.", this);
            }

            if (gameplayCamera != null)
            {
                if (isDungeon)
                {
                    gameplayCamera.clearFlags = CameraClearFlags.SolidColor;
                    gameplayCamera.backgroundColor = dungeonBackgroundColor;
                }
                else
                {
                    gameplayCamera.clearFlags = CameraClearFlags.Skybox;
                }
            }
            else
            {
                Debug.LogWarning("EnvironmentManager has no gameplay camera reference.", this);
            }

            if (!isDungeon && (scenery == null || scenery.skybox == null))
            {
                Debug.LogWarning($"No skybox material is assigned for {CurrentTheme}.", this);
            }
        }

        private void RestorePresentation()
        {
            if (savedPresentation == null)
                return;
            RenderSettings.skybox = savedPresentation.Skybox;
            RenderSettings.ambientLight = savedPresentation.AmbientLight;
            RenderSettings.ambientSkyColor = savedPresentation.AmbientSky;
            RenderSettings.ambientEquatorColor = savedPresentation.AmbientEquator;
            RenderSettings.ambientGroundColor = savedPresentation.AmbientGround;
            RenderSettings.ambientMode = savedPresentation.AmbientMode;
            RenderSettings.ambientIntensity = savedPresentation.AmbientIntensity;
            RenderSettings.fog = savedPresentation.Fog;
            if (savedPresentation.Camera != null)
            {
                savedPresentation.Camera.clearFlags = savedPresentation.ClearFlags;
                savedPresentation.Camera.backgroundColor = savedPresentation.Background;
            }

            if (savedPresentation.DirectionalLight != null)
            {
                savedPresentation.DirectionalLight.intensity = savedPresentation.DirectionalIntensity;
            }

            savedPresentation = null;
        }

        [Header("Tunnel exit fade")]
        [SerializeField]
        private CanvasGroup journeyFadeGroup;
        [SerializeField]
        private GameObject journeyExitVeil;
        [SerializeField, Min(1f)]
        private float journeyFadeStartBeforeExit = 6f;
        [SerializeField, Min(0.05f)]
        private float journeyFadeOutDuration = 0.35f;
        [SerializeField, Min(0f)]
        private float journeyCoveredHold = 0.15f;
        [SerializeField, Min(0.05f)]
        private float journeyFadeInDuration = 0.5f;
        private enum JourneyFadeStage
        {
            Idle,
            FadingOut,
            Covered,
            WaitingForExit,
            FadingIn
        }

        private sealed class TransitionRequest
        {
            public int SegmentIndex;
            public int DestinationZone;
            public EnvironmentTheme DestinationTheme;
            public float EntryZ;
            public float ExitZ;
        }

        private readonly Queue<TransitionRequest> transitionRequests = new Queue<TransitionRequest>();
        private readonly HashSet<int> registeredTransitionIndices = new HashSet<int>();
        private JourneyFadeStage journeyFadeStage;
        private float journeyPhaseElapsed;
        private int journeyCoveredFrame = -1;
        private bool HasJourneyFadeReferences()
        {
            if (gameplayCamera == null || journeyFadeGroup == null || !journeyFadeGroup.isActiveAndEnabled || journeyExitVeil == null)
                return false;
            Canvas canvas = journeyFadeGroup.GetComponentInParent<Canvas>();
            return canvas != null && canvas.isActiveAndEnabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
        }

        private void RegisterJourneyTunnel(TrackSegment segment, int segmentIndex, int sourceZone)
        {
            if (!HasScheduledJourney || registeredTransitionIndices.Contains(segmentIndex))
                return;
            if (segment.EntryAnchor == null || segment.ExitAnchor == null || segment.ExitAnchor.position.z <= segment.EntryAnchor.position.z)
            {
                Debug.LogError("A scheduled tunnel needs valid forward +Z anchors.", this);
                return;
            }

            var request = new TransitionRequest
            {
                SegmentIndex = segmentIndex,
                DestinationZone = sourceZone + 1,
                DestinationTheme = GetThemeForZone(sourceZone + 1),
                EntryZ = segment.EntryAnchor.position.z,
                ExitZ = segment.ExitAnchor.position.z
            };
            transitionRequests.Enqueue(request);
            registeredTransitionIndices.Add(segmentIndex);
            if (transitionRequests.Count == 1)
                ArmNextTransitionVeil();
        }

        private void ArmNextTransitionVeil()
        {
            if (journeyExitVeil == null)
                return;
            if (transitionRequests.Count == 0)
            {
                journeyExitVeil.SetActive(false);
                return;
            }

            Vector3 position = journeyExitVeil.transform.position;
            position.z = transitionRequests.Peek().ExitZ - 0.05f;
            journeyExitVeil.transform.position = position;
            journeyExitVeil.SetActive(true);
        }

        public void UpdateJourneyPresentation(float playerZ, bool horizonReady = true)
        {
            if (!HasScheduledJourney || transitionRequests.Count == 0 || gameplayCamera == null || journeyFadeGroup == null)
                return;
            TransitionRequest request = transitionRequests.Peek();
            float cameraZ = gameplayCamera.transform.position.z;
            // Ignore the camera while it returns from an old run.
            if (playerZ < request.EntryZ || cameraZ > playerZ)
                return;
            switch (journeyFadeStage)
            {
                case JourneyFadeStage.Idle:
                {
                    float lead = Mathf.Clamp(journeyFadeStartBeforeExit, 1f, TrackSegment.ContractLength - 1f);
                    if (cameraZ < request.ExitZ - lead)
                        return;
                    journeyPhaseElapsed = 0f;
                    journeyFadeStage = JourneyFadeStage.FadingOut;
                    SetJourneyFadeAlpha(0f);
                    break;
                }

                case JourneyFadeStage.FadingOut:
                {
                    journeyPhaseElapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(journeyPhaseElapsed / Mathf.Max(0.05f, journeyFadeOutDuration));
                    SetJourneyFadeAlpha(Mathf.SmoothStep(0f, 1f, t));
                    if (t >= 1f)
                    {
                        SetJourneyFadeAlpha(1f);
                        journeyPhaseElapsed = 0f;
                        journeyCoveredFrame = Time.frameCount;
                        journeyFadeStage = JourneyFadeStage.Covered;
                    }

                    break;
                }

                case JourneyFadeStage.Covered:
                {
                    SetJourneyFadeAlpha(1f);
                    // Normal Update rendering must have seen full cover.
                    if (Time.frameCount <= journeyCoveredFrame)
                        return;
                    journeyPhaseElapsed += Time.deltaTime;
                    if (journeyPhaseElapsed < Mathf.Max(0f, journeyCoveredHold))
                        return;
                    StreamingHoldRequired = !horizonReady || !TryGetScenery(request.DestinationTheme, out _);
                    if (StreamingHoldRequired) return;
                    CurrentTheme = request.DestinationTheme;
                    currentSettings = GetSettings(CurrentTheme);
                    ApplyPresentation();
                    visibleZone = request.DestinationZone;
                    RevealVisibleZones();
                    PruneEndlessThemeHistory();
                    if (journeyExitVeil != null)
                        journeyExitVeil.SetActive(false);
                    journeyFadeStage = JourneyFadeStage.WaitingForExit;
                    Debug.Log($"Theme handoff under cover: zone {visibleZone}, " + $"{CurrentTheme}, camera Z={cameraZ:F1}.", this);
                    break;
                }

                case JourneyFadeStage.WaitingForExit:
                    SetJourneyFadeAlpha(1f);
                    if (cameraZ >= request.ExitZ)
                    {
                        journeyPhaseElapsed = 0f;
                        journeyFadeStage = JourneyFadeStage.FadingIn;
                    }

                    break;
                case JourneyFadeStage.FadingIn:
                {
                    journeyPhaseElapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(journeyPhaseElapsed / Mathf.Max(0.05f, journeyFadeInDuration));
                    SetJourneyFadeAlpha(1f - Mathf.SmoothStep(0f, 1f, t));
                    if (t >= 1f)
                    {
                        SetJourneyFadeAlpha(0f);
                        transitionRequests.Dequeue();
                        registeredTransitionIndices.Remove(request.SegmentIndex);
                        journeyPhaseElapsed = 0f;
                        journeyCoveredFrame = -1;
                        journeyFadeStage = JourneyFadeStage.Idle;
                        ArmNextTransitionVeil();
                    }

                    break;
                }
            }
        }

        private void SetJourneyFadeAlpha(float alpha)
        {
            if (journeyFadeGroup == null)
                return;
            journeyFadeGroup.alpha = Mathf.Clamp01(alpha);
            journeyFadeGroup.interactable = false;
            journeyFadeGroup.blocksRaycasts = false;
        }

        private void ResetJourneyPresentation()
        {
            SetJourneyFadeAlpha(0f);
            if (journeyExitVeil != null)
                journeyExitVeil.SetActive(false);
            transitionRequests.Clear();
            registeredTransitionIndices.Clear();
            journeyFadeStage = JourneyFadeStage.Idle;
            journeyPhaseElapsed = 0f;
            journeyCoveredFrame = -1;
        }
    }
}
