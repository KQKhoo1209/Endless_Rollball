using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    public sealed class StreetCarHornAudio : MonoBehaviour
    {
        public static StreetCarHornAudio Instance { get; private set; }
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private AudioClip hornClip;
        [Header("Moving vehicle loop")]
        [SerializeField] private AudioClip movingClip;
        [SerializeField, Range(0f, 1f)] private float movingVolume = .4f;
        [SerializeField, Range(.1f, 2f)] private float movingPlaybackSpeed = .5f;
        [SerializeField, Range(0f, 1f)] private float hornVolume = .65f;
        [SerializeField, Min(.1f)] private float detectionDistance = 8f;
        [SerializeField, Min(.05f)] private float checkInterval = .1f;
        [SerializeField, Min(.1f)] private float hornCooldown = 3f;
        [SerializeField, Min(.05f)] private float rayHeight = .5f;
        [SerializeField] private LayerMask detectionLayers = ~0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Awake()
        {
            Instance = this;
            if (hornClip != null && hornClip.loadState == AudioDataLoadState.Unloaded)
                hornClip.LoadAudioData();
            if (movingClip != null && movingClip.loadState == AudioDataLoadState.Unloaded)
                movingClip.LoadAudioData();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public Emitter CreateEmitter(Transform car, Transform player, Collider[] shapes)
        {
            if ((hornClip == null && movingClip == null) || car == null || player == null) return null;
            return new Emitter(this, car, player, shapes);
        }

        // Each moving car owns its source and timing; parked vehicles get no emitter.
        public sealed class Emitter
        {
            private readonly StreetCarHornAudio owner;
            private readonly Transform car, player;
            private readonly AudioSource source;
            private readonly AudioSource movingSource;
            private readonly float noseOffset;
            private float nextCheck, nextHorn;

            internal Emitter(StreetCarHornAudio owner, Transform car, Transform player, Collider[] shapes)
            {
                this.owner = owner;
                this.car = car;
                this.player = player;
                Vector3 forward = car.forward;
                float nose = 0f;
                foreach (Collider shape in shapes)
                {
                    if (shape == null || shape.isTrigger) continue;
                    Bounds bounds = shape.bounds;
                    float extent = Vector3.Dot(new Vector3(Mathf.Abs(forward.x),
                        Mathf.Abs(forward.y), Mathf.Abs(forward.z)), bounds.extents);
                    nose = Mathf.Max(nose, Vector3.Dot(bounds.center - car.position, forward) + extent);
                }
                noseOffset = nose + .05f;
                source = car.gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.clip = owner.hornClip;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 3f;
                source.maxDistance = 30f;
                if (owner.movingClip != null)
                {
                    movingSource = car.gameObject.AddComponent<AudioSource>();
                    movingSource.playOnAwake = false;
                    movingSource.loop = true;
                    movingSource.clip = owner.movingClip;
                    movingSource.pitch = owner.movingPlaybackSpeed;
                    movingSource.spatialBlend = 1f;
                    movingSource.dopplerLevel = 0f;
                    movingSource.rolloffMode = AudioRolloffMode.Linear;
                    movingSource.minDistance = 3f;
                    movingSource.maxDistance = 30f;
                    movingSource.volume = owner.movingVolume *
                        Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                        Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
                }
            }

            public void Tick(bool movingAndVisible)
            {
                if (owner == null || !owner.isActiveAndEnabled || owner.runController == null ||
                    owner.runController.State != SessionFlowState.Running || !movingAndVisible ||
                    car == null || !car.gameObject.activeInHierarchy || player == null)
                {
                    Stop();
                    return;
                }
                float now = Time.time;
                if (movingSource != null && !movingSource.isPlaying && movingSource.volume > 0f)
                    movingSource.Play();
                if (owner.hornClip == null) return;
                if (now < nextCheck || now < nextHorn || source.isPlaying) return;
                nextCheck = now + owner.checkInterval;
                Vector3 origin = car.position + car.forward * noseOffset;
                origin.y = car.position.y + owner.rayHeight;
                if (!Physics.Raycast(origin, car.forward, out RaycastHit hit,
                    owner.detectionDistance, owner.detectionLayers, QueryTriggerInteraction.Ignore)) return;
                if (hit.transform != player && !hit.transform.IsChildOf(player)) return;

                float gain = Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                    Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
                nextHorn = now + owner.hornCooldown;
                if (gain > 0f)
                {
                    source.volume = owner.hornVolume * gain;
                    source.Play();
                }
            }

            public void Stop()
            {
                if (source != null && source.isPlaying) source.Stop();
                if (movingSource != null && movingSource.isPlaying) movingSource.Stop();
            }
        }
    }
}
