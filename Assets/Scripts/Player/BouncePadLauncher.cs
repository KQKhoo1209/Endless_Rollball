using UnityEngine;

namespace EndlessRollball
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BouncePadLauncher : MonoBehaviour
    {
        [SerializeField] private Animator padAnimator;
        [SerializeField, Min(1f)] private float launchHeight = 16f;
        [SerializeField] private CloudPlatformEvent cloudEvent;

        private bool launched;

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnEnable()
        {
            launched = false;
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