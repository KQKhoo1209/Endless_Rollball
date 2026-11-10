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

        [Header("Boat horn")]
        [SerializeField] private bool hornEnabled;
        [SerializeField, Min(.1f)] private float hornDetectionDistance = 18f;
        [SerializeField, Min(.02f)] private float hornCheckInterval = .02f;
        [SerializeField, Min(.1f)] private float hornCooldown = 6f;

        [Header("Moving water effects")]
        [SerializeField] private bool movingWaterEffects = true;
        private WaterMovementWake wake;
        private SurfaceContactRipples contactRipples;
        private Collider[] contactColliders;
        private WhaleSurfController surfController;
        private Rigidbody body;
        private Transform surfer;
        private Vector3 startPosition;
        private float waterSurfaceY;
        private Vector3 crossingTarget;
        private float activationDistance;
        private bool prepared;
        private bool moving;
        private float nextHornAt;
        private float nextHornCheckAt;
        private Collider surferCollider;
        internal bool CrossesChannel => movement==MovementType.CrossChannel;
        internal float EncounterTimeOffset(float surferSpeed) => movement==MovementType.TowardsPlayer
            ? movementSpeed*approachLeadSeconds/Mathf.Max(.1f,surferSpeed+movementSpeed) : 0f;

        internal void PrepareWake()
        {
            if (movingWaterEffects && movement != MovementType.Stationary && wake == null)
                wake = WaterMovementWake.Create(transform, transform.Find("Visual") ?? transform);
        }

        public void ConfigureMovement(
            Transform surferTransform,
            float surferSpeed,
            float channelCentreX,
            Material surfaceMaterial = null,
            float surfaceY = float.NaN)
        {
            body = GetComponent<Rigidbody>();
            surfer = surferTransform;
            surfController = surfer != null ? surfer.GetComponent<WhaleSurfController>() : null;
            surferCollider = surfer != null ? surfer.GetComponent<Collider>() : null;

            startPosition = transform.position;
            waterSurfaceY = float.IsNaN(surfaceY) ? startPosition.y : surfaceY;
            crossingTarget = startPosition;
            crossingTarget.x =
                channelCentreX * 2f - startPosition.x;

            activationDistance =
                Mathf.Max(.1f, surferSpeed) * approachLeadSeconds;

            moving = false;
            nextHornAt = 0f;
            nextHornCheckAt = 0f;
            prepared = false;
            PrepareWake();
            wake?.ResetAt(startPosition, waterSurfaceY, surfaceMaterial);
            if (contactRipples == null)
                contactRipples = GetComponent<SurfaceContactRipples>();
            contactRipples?.RestartAtWaterline(surfaceMaterial, waterSurfaceY);

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
            CheckHorn();
            if (!prepared || surfer == null || (surfController != null && surfController.IsRecovering))
            {
                wake?.Stop();
                return;
            }

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

            wake?.Move(next, (next - body.position) / Time.fixedDeltaTime, waterSurfaceY);
            body.MovePosition(next);
        }

        internal bool TryGetContactBounds(out Bounds bounds)
        {
            if (contactColliders == null)
                contactColliders = GetComponentsInChildren<Collider>(true);
            bounds = default;
            bool found = false;
            foreach (Collider contact in contactColliders)
            {
                if (!contact.enabled || !contact.gameObject.activeInHierarchy) continue;
                if (found) bounds.Encapsulate(contact.bounds);
                else bounds = contact.bounds;
                found = true;
            }
            return found;
        }

        private void CheckHorn()
        {
            var audio = BeachGameplayAudio.Instance;
            if (!hornEnabled || movement != MovementType.TowardsPlayer || !prepared ||
                surfer == null || surferCollider == null || surfController == null ||
                !surfController.IsRidingWater || audio == null || !audio.SurfingAudioActive ||
                Time.time < nextHornAt || Time.time < nextHornCheckAt) return;
            nextHornCheckAt = Time.time + hornCheckInterval;
            if (!TryGetContactBounds(out Bounds bounds)) return;

            // TowardsPlayer boats travel along world -Z. Cast from outside the bow
            // at the rider hull's height; the active surfing hull is a trigger.
            Vector3 origin = bounds.center;
            origin.z = bounds.min.z - .05f;
            origin.y = surferCollider.bounds.center.y;
            if (!Physics.Raycast(origin, Vector3.back, out RaycastHit hit,
                hornDetectionDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) return;
            if (hit.transform != surfer && !hit.transform.IsChildOf(surfer)) return;
            if (audio.TryBoatHorn()) nextHornAt = Time.time + hornCooldown;
        }

        private void OnDisable()
        {
            wake?.Clear();
            prepared = false;
            moving = false;
        }
    }
}
