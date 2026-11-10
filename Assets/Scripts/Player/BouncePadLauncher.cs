using UnityEngine;

namespace EndlessRollball
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BouncePadLauncher : MonoBehaviour
    {
        [SerializeField] private Animator padAnimator;
        [SerializeField, Min(1f)] private float launchHeight = 16f;
        [SerializeField] private CloudPlatformEvent cloudEvent;
        [Header("Launch sound")]
        [SerializeField] private AudioSource bounceSource;
        [SerializeField, Range(0f, 1f)] private float bounceVolume = .65f;

        private bool launched;

        private void Awake()
        {
            if (bounceSource == null) return;
            bounceSource.playOnAwake = false;
            bounceSource.loop = false;
            bounceSource.spatialBlend = 0f;
            bounceSource.dopplerLevel = 0f;
            if (bounceSource.clip != null && bounceSource.clip.loadState == AudioDataLoadState.Unloaded)
                bounceSource.clip.LoadAudioData();
        }

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnEnable()
        {
            launched = false;
            if (bounceSource != null) bounceSource.Stop();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryLaunch(other.GetComponentInParent<BallMovement>());
        }

        public void TryLaunch(BallMovement player)
        {
            if (launched || player == null) return;

            Rigidbody body = player.GetComponent<Rigidbody>();
            float gravity = -Physics.gravity.y;

            if (body == null || body.isKinematic || !body.useGravity || gravity <= 0f) return;

            if (cloudEvent == null || !cloudEvent.TryPreparePhysicalLaunch(player, launchHeight)) return;

            launched = true;

            if (padAnimator != null)
                padAnimator.Play("Bounce", 0, 0f);

            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            body.linearVelocity = velocity;

            float launchSpeed =
                Mathf.Sqrt(2f * gravity * launchHeight);

            body.AddForce(
                Vector3.up * launchSpeed,
                ForceMode.VelocityChange);

            // This path runs once, only after the cloud event accepted the launch.
            if (bounceSource != null && bounceSource.clip != null)
            {
                float gain = Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Master", 1f)) *
                    Mathf.Clamp01(PlayerPrefs.GetFloat("MenuAudio.Effects", .45f));
                bounceSource.volume = bounceVolume * gain;
                if (gain > 0f) bounceSource.Play();
            }
        }

        private void OnDisable()
        {
            if (bounceSource != null) bounceSource.Stop();
        }

        private void OnTriggerStay(Collider other)
        {
            OnTriggerEnter(other);
        }

        public bool HasLaunched => launched;

        public void Configure(CloudPlatformEvent manager)
        {
            cloudEvent = manager;
        }
    }
}
