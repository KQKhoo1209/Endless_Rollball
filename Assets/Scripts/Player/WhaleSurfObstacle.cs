using UnityEngine;

namespace EndlessRollball
{
    public sealed class WhaleSurfObstacle : MonoBehaviour
    {
        public enum MovementType
        {
            Stationary,
            TowardsPlayer,
            CrossChannel
        }

        [Header("Movement")]
        [SerializeField] private MovementType movement;
        [SerializeField, Min(.1f)] private float movementSpeed = 3f;

        [Tooltip("Start moving this many seconds before the whale " +
                 "would reach the boat's starting position.")]
        [SerializeField, Min(.1f)] private float approachLeadSeconds = 4f;

        private Rigidbody body;
        private Transform surfer;
        private Vector3 startPosition;
        private Vector3 crossingTarget;
        private float activationDistance;
        private bool prepared;
        private bool moving;

        public void ConfigureMovement(
            Transform surferTransform,
            float surferSpeed,
            float channelCentreX)
        {
            body = GetComponent<Rigidbody>();
            surfer = surferTransform;

            startPosition = transform.position;
            crossingTarget = startPosition;
            crossingTarget.x =
                channelCentreX * 2f - startPosition.x;

            activationDistance =
                Mathf.Max(.1f, surferSpeed) * approachLeadSeconds;

            moving = false;
            prepared = false;

            if (movement == MovementType.Stationary)
                return;

            if (body == null || surfer == null)
            {
                Debug.LogWarning(
                    $"{name}: moving boats need a root Rigidbody " +
                    "and a surfer reference.", this);
                return;
            }

            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // Reset the physics position when the course is reused.
            body.position = startPosition;
            body.rotation = transform.rotation;

            prepared = true;
        }

        private void FixedUpdate()
        {
            if (!prepared || surfer == null)
                return;

            if (!moving)
            {
                float distanceAhead =
                    startPosition.z - surfer.position.z;

                if (distanceAhead > activationDistance)
                    return;

                // Do not start an encounter already behind the whale.
                if (distanceAhead < 0f)
                {
                    prepared = false;
                    return;
                }

                moving = true;
            }

            Vector3 next = body.position;

            if (movement == MovementType.TowardsPlayer)
            {
                next.z -= movementSpeed * Time.fixedDeltaTime;

                // Stop updating once the boat is well behind.
                if (next.z < surfer.position.z - 20f)
                    prepared = false;
            }
            else if (movement == MovementType.CrossChannel)
            {
                next = Vector3.MoveTowards(
                    next,
                    crossingTarget,
                    movementSpeed * Time.fixedDeltaTime);

                if ((next - crossingTarget).sqrMagnitude < .0001f)
                    prepared = false;
            }

            body.MovePosition(next);
        }

        private void OnDisable()
        {
            prepared = false;
            moving = false;
        }
    }
}