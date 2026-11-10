using EndlessRollball.Scenery;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    public sealed class StreetBallRollingAudio : MonoBehaviour
    {
        [SerializeField] private BallMovement ballMovement;
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private EnvironmentManager environmentManager;
        [SerializeField] private AudioSource rollingSource;
        [SerializeField] private AudioClip rollingClip;
        [SerializeField, Range(0f, 1f)] private float rollingVolume = .35f;
        [SerializeField, Min(.01f)] private float minimumSpeed = .2f;
        [SerializeField, Min(.1f)] private float referenceSpeed = 7f;
        [SerializeField, Min(.01f)] private float fadeInSeconds = .1f;
        [Header("Theme pitch multipliers")]
        [SerializeField, Range(.5f, 2f)] private float streetPitch = 1f;
        [SerializeField, Range(.5f, 2f)] private float beachPitch = 1.1f;
        [SerializeField, Range(.5f, 2f)] private float dungeonPitch = .85f;
        [Tooltip("Response time for smoothing audible pitch changes, in seconds.")]
        [SerializeField, Min(.01f)] private float pitchBlendSeconds = .25f;

        private Rigidbody ballBody;
        private Collider cachedGround;
        private TrackSegment cachedSegment;
        private float effectsGain;

        private void Awake()
        {
            if (ballMovement != null) ballBody = ballMovement.GetComponent<Rigidbody>();
            if (rollingSource == null) return;
            rollingSource.playOnAwake = false;
            rollingSource.loop = true;
            rollingSource.spatialBlend = 0f;
            rollingSource.dopplerLevel = 0f;
            rollingSource.volume = 0f;
            rollingSource.clip = rollingClip;
            rollingSource.Stop();
            // Load the short loop before the first road contact, not at the first Play.
            if (rollingClip != null && rollingClip.loadState == AudioDataLoadState.Unloaded)
                rollingClip.LoadAudioData();
        }

        private void OnEnable()
        {
            effectsGain = Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
        }

        private void LateUpdate()
        {
            if (rollingSource == null) return;
            if (rollingClip == null || ballBody == null || ballMovement == null ||
                !ballMovement.isActiveAndEnabled || runController == null ||
                runController.State != SessionFlowState.Running ||
                environmentManager == null || !environmentManager.isActiveAndEnabled ||
                !ballMovement.IsGrounded || Time.timeScale <= 0f || effectsGain <= 0f)
            {
                StopRolling();
                return;
            }

            Collider ground = ballMovement.GroundCollider;
            if (ground != cachedGround)
            {
                cachedGround = ground;
                cachedSegment = ground != null ? ground.GetComponentInParent<TrackSegment>() : null;
            }
            // Excludes carriers and other non-track surfaces during special events.
            if (ground == null || cachedSegment == null)
            {
                StopRolling();
                return;
            }

            Vector3 velocity = ballBody.linearVelocity;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (speed < minimumSpeed || rollingClip.loadState != AudioDataLoadState.Loaded)
            {
                StopRolling();
                return;
            }

            float speedRatio = Mathf.Clamp01(speed / Mathf.Max(.1f, referenceSpeed));
            float targetVolume = rollingVolume * effectsGain * speedRatio;
            float themePitch = environmentManager.CurrentTheme switch
            {
                EnvironmentTheme.Water => beachPitch,
                EnvironmentTheme.Dungeon => dungeonPitch,
                _ => streetPitch
            };
            float targetPitch = themePitch * Mathf.Lerp(.8f, 1.15f, speedRatio);
            // Start silent loops at their target pitch; blend while already audible.
            rollingSource.pitch = rollingSource.isPlaying
                ? Mathf.Lerp(rollingSource.pitch, targetPitch,
                    1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(.01f, pitchBlendSeconds)))
                : targetPitch;
            rollingSource.volume = Mathf.MoveTowards(rollingSource.volume, targetVolume,
                rollingVolume * effectsGain * Time.deltaTime / Mathf.Max(.01f, fadeInSeconds));
            if (!rollingSource.isPlaying) rollingSource.Play();
        }

        private void StopRolling()
        {
            rollingSource.volume = 0f;
            if (rollingSource.isPlaying) rollingSource.Stop();
        }

        private void OnDisable()
        {
            if (rollingSource != null) StopRolling();
            cachedGround = null;
            cachedSegment = null;
        }
    }
}
