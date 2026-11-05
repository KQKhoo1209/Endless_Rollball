using UnityEngine;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CloudBouncePlatform : MonoBehaviour
    {
        [SerializeField, Min(.1f)]
        private float bounceHeight = 3f;

        private BoxCollider nextPlatform;
        private bool finalPlatform;

        [SerializeField, Min(.1f)]
        private float finalBounceHeight = 1.2f;

        private bool finalExitLaunched;
        public event System.Action<Rigidbody> FinalLanded;
        public event System.Action<CloudBouncePlatform, Rigidbody> Landed;

        private void OnEnable()
        {
            finalExitLaunched = false;
        }

        private void Reset()
        {
            GetComponent<BoxCollider>().isTrigger = false;
        }

        public void ConfigureHop(
            float duration,
            BoxCollider target = null,
            bool isFinal = false)
        {
            nextPlatform = target;
            finalPlatform = isFinal;

            float gravity = Mathf.Abs(Physics.gravity.y);
            bounceHeight = gravity * duration * duration / 8f;
        }

        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody body = collision.rigidbody;

            if (body == null ||
                !body.TryGetComponent<BallMovement>(out var player))
                return;

            bool landedOnTop = false;

            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y < -.5f)
                {
                    landedOnTop = true;
                    break;
                }
            }

            if (!landedOnTop ||
                body.isKinematic || !body.useGravity)
                return;

            EndlessRollball.Infrastructure.RuntimeLog.Info(
    $"[CloudLanding] {name}, " +
    $"next={(nextPlatform != null ? nextPlatform.name : "none")}, " +
    $"final={finalPlatform}",
    this);

            if (finalPlatform && finalExitLaunched)
                return;

            Landed?.Invoke(this, body);

            if (finalPlatform)
            {
                finalExitLaunched = true;

                float finalGravity = -Physics.gravity.y;
                if (finalGravity <= 0f) return;

                Vector3 exitVelocity = body.linearVelocity;
                exitVelocity.y = 0f;
                exitVelocity.z = player.ForwardSpeed;
                body.linearVelocity = exitVelocity;

                float exitSpeed =
                    Mathf.Sqrt(2f * finalGravity * finalBounceHeight);

                body.AddForce(
                    Vector3.up * exitSpeed,
                    ForceMode.VelocityChange);

                FinalLanded?.Invoke(body);
                return;
            }

            float gravity = -Physics.gravity.y;
            if (gravity <= 0f) return;

            float launchSpeed =
                Mathf.Sqrt(2f * gravity * bounceHeight);

            if (nextPlatform != null)
            {
                float speed = player.ForwardSpeed;
                float distance =
                    nextPlatform.bounds.center.z - body.position.z;

                if (speed <= .01f || distance <= .01f)
                    return;

                float flightTime = distance / speed;

                float targetY =
                    nextPlatform.bounds.max.y + player.SphereRadius;

                float heightDifference = targetY - body.position.y;

                launchSpeed =
                    heightDifference / flightTime +
                    .5f * gravity * flightTime;

                // Compensate for fixed-step gravity integration.
                launchSpeed +=
                    .5f * gravity * Time.fixedDeltaTime;
            }

            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            velocity.z = player.ForwardSpeed;
            body.linearVelocity = velocity;

            body.AddForce(
                Vector3.up * launchSpeed,
                ForceMode.VelocityChange);
        }
    }
}