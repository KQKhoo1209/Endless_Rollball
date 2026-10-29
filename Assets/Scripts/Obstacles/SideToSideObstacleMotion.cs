using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SideToSideObstacleMotion : MonoBehaviour
    {
        [SerializeField, Min(0f)]
        private float halfTravel = 2.5f;
        [SerializeField, Min(0f)]
        private float speed = 1.5f;
        [SerializeField, Range(0f, 1f)]
        private float startPhase = 0.25f;
        [SerializeField]
        private Vector3 localHomePosition;
        [Tooltip("Offsets from Local Home Position in the Mover parent's local space. Two or more points are followed in order, then in reverse. Empty uses the existing left/right Half Travel limits.")]
        [SerializeField]
        private Vector3[] localWaypointOffsets = System.Array.Empty<Vector3>();
        private Rigidbody body;
        private float distance;
        private bool prepared;
        private bool running;
        private Vector3[] route;
        private float routeLength;
        internal Bounds AuthoredTravelBounds
        {
            get
            {
                Vector3[] points = prepared && route != null ? route : localWaypointOffsets != null && localWaypointOffsets.Length >= 2 ? localWaypointOffsets : new[]
                {
                    Vector3.left * halfTravel,
                    Vector3.right * halfTravel
                };
                var bounds = new Bounds(localHomePosition + points[0], Vector3.zero);
                for (int i = 1; i < points.Length; i++)
                    bounds.Encapsulate(localHomePosition + points[i]);
                return bounds;
            }
        }

        private bool shapeMeasured;
        private float minShape, maxShape;
        internal void InvalidateGeometry() => shapeMeasured = false;
        private void OnValidate() => InvalidateGeometry();
        private void MeasureShape()
        {
            if (shapeMeasured) return;
            shapeMeasured = true;
            minShape = float.PositiveInfinity;
            maxShape = float.NegativeInfinity;
            foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>(true))
            {
                if (!box.enabled || box.isTrigger) continue;
                Matrix4x4 matrix = transform.parent != null ? transform.parent.worldToLocalMatrix * box.transform.localToWorldMatrix : box.transform.localToWorldMatrix;
                Vector3 translation = transform.parent != null ? transform.localPosition : transform.position;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = box.center + Vector3.Scale(box.size * .5f, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    float x = matrix.MultiplyPoint3x4(point).x - translation.x;
                    minShape = Mathf.Min(minShape, x);
                    maxShape = Mathf.Max(maxShape, x);
                }
            }
        }

        internal bool TryPlanBounds(float corridorHalfWidth, float passageX, float passageHalfWidth, out Bounds travel)
            => EvaluateRoute(corridorHalfWidth, passageX, passageHalfWidth, null, out travel);

        // The identical arithmetic serves bounds-only candidates and the acquired instance.
        private bool EvaluateRoute(float corridorHalfWidth, float passageX, float passageHalfWidth, Vector3[] output, out Bounds travel)
        {
            MeasureShape();
            float left = -corridorHalfWidth + RoadCorridor.BoundarySkin;
            float right = corridorHalfWidth - RoadCorridor.BoundarySkin;
            if (passageHalfWidth > 0f)
            {
                if (passageX >= 0f) right = Mathf.Min(right, passageX - passageHalfWidth);
                else left = Mathf.Max(left, passageX + passageHalfWidth);
            }
            float minPosition = left - minShape, maxPosition = right - maxShape;
            travel = default;
            if (float.IsInfinity(minShape) || minPosition > maxPosition) return false;
            bool authored = localWaypointOffsets != null && localWaypointOffsets.Length >= 2;
            int count = authored ? localWaypointOffsets.Length : 2;
            for (int i = 0; i < count; i++)
            {
                Vector3 point = authored ? localWaypointOffsets[i] : (i == 0 ? Vector3.left : Vector3.right) * halfTravel;
                point.x = Mathf.Clamp(localHomePosition.x + point.x, minPosition, maxPosition) - localHomePosition.x;
                if (output != null) output[i] = point;
                Vector3 position = localHomePosition + point;
                if (i == 0) travel = new Bounds(position, Vector3.zero);
                else travel.Encapsulate(position);
            }
            return true;
        }

        public void Prepare(float configuredSpeed, float configuredPhase, float corridorHalfWidth = 4.5f, float passageX = 0f, float passageHalfWidth = 0f)
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            speed = Mathf.Max(0f, configuredSpeed);
            startPhase = Mathf.Clamp01(configuredPhase);
            // Snapshot the path per acquisition; pausing does not rebuild or reset it.
            int pointCount = localWaypointOffsets != null && localWaypointOffsets.Length >= 2 ? localWaypointOffsets.Length : 2;
            if (route == null || route.Length != pointCount) route = new Vector3[pointCount];
            if (!EvaluateRoute(corridorHalfWidth, passageX, passageHalfWidth, route, out _))
            {
                prepared = running = false;
                Debug.LogError("Moving obstacle no longer fits its planned corridor passage.", this);
                return;
            }
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
            if (!prepared || !running || body == null || !body.isKinematic || routeLength <= 0.0001f || speed <= 0f)
            {
                return;
            }

            distance = Mathf.Repeat(distance + speed * Time.fixedDeltaTime, 2f * routeLength);
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
            float remaining = routeLength > 0f ? Mathf.PingPong(distance, routeLength) : 0f;
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
            return transform.parent != null ? transform.parent.TransformPoint(local) : local;
        }
    }
}
