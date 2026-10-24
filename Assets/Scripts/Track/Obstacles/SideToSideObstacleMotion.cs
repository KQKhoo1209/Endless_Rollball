using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SideToSideObstacleMotion : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float halfTravel = 2.5f;
        [SerializeField, Min(0f)] private float speed = 1.5f;
        [SerializeField, Range(0f, 1f)] private float startPhase = 0.25f;
        [SerializeField] private Vector3 localHomePosition;
        [Tooltip("Offsets from Local Home Position in the Mover parent's local space. Two or more points are followed in order, then in reverse. Empty uses the existing left/right Half Travel limits.")]
        [SerializeField] private Vector3[] localWaypointOffsets = System.Array.Empty<Vector3>();

        private Rigidbody body;
        private float distance;
        private bool prepared;
        private bool running;
        private Vector3[] route;
        private float routeLength;

        public void Prepare(float configuredSpeed, float configuredPhase)
        {
            if (body == null)
                body = GetComponent<Rigidbody>();

            speed = Mathf.Max(0f, configuredSpeed);
            startPhase = Mathf.Clamp01(configuredPhase);
            // Snapshot the path per acquisition; pausing does not rebuild or reset it.
            route = localWaypointOffsets != null && localWaypointOffsets.Length >= 2
                ? (Vector3[])localWaypointOffsets.Clone()
                : new[] { Vector3.left * halfTravel, Vector3.right * halfTravel };
            routeLength = 0f;
            for (int i = 1; i < route.Length; i++)
                routeLength += Vector3.Distance(route[i - 1], route[i]);
            distance = startPhase * 2f * routeLength;
            running = false;
            prepared = true;
            ApplyPose();
        }

        public void SetRunning(bool value)
        {
            running = prepared && value;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            if (body == null)
                body = GetComponent<Rigidbody>();

            running = false;
            if (prepared)
                ApplyPose();
        }

        private void FixedUpdate()
        {
            if (!prepared || !running || body == null ||
                !body.isKinematic || routeLength <= 0.0001f || speed <= 0f)
            {
                return;
            }

            distance = Mathf.Repeat(
                distance + speed * Time.fixedDeltaTime,
                2f * routeLength);

            body.MovePosition(WorldPosition());
        }

        private void OnDisable()
        {
            running = false;
            prepared = false;
        }

        private void ApplyPose()
        {
            Vector3 position = WorldPosition();
            transform.position = position;
            if (body != null)
                body.position = position;
        }

        private Vector3 WorldPosition()
        {
            float remaining = routeLength > 0f
                ? Mathf.PingPong(distance, routeLength) : 0f;
            Vector3 offset = route != null && route.Length > 0 ? route[0] : Vector3.zero;
            if (route != null)
            {
                for (int i = 1; i < route.Length; i++)
                {
                    float length = Vector3.Distance(route[i - 1], route[i]);
                    if (length <= 0.0001f)
                        continue;
                    if (remaining <= length)
                    {
                        offset = Vector3.Lerp(route[i - 1], route[i], remaining / length);
                        break;
                    }
                    remaining -= length;
                    offset = route[i];
                }
            }

            Vector3 local = localHomePosition + offset;
            return transform.parent != null
                ? transform.parent.TransformPoint(local)
                : local;
        }
    }
}
