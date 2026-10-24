using UnityEngine;

namespace EndlessRollball.Track
{
    /// <summary>
    /// Deterministic, pool-resettable lateral movement for a kinematic obstacle.
    /// Place the prefab root on the road centre with unit scale; only Mover moves.
    /// This does not select, spawn, score or resolve an obstacle encounter.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SideToSideObstacleMotion : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float halfTravel = 2.5f;
        [SerializeField, Min(0f)] private float speed = 1.5f;
        [SerializeField, Range(0f, 1f)] private float startPhase = 0.25f;
        [SerializeField] private Vector3 localHomePosition;

        private Rigidbody body;
        private float distance;

        private void Awake() => body = GetComponent<Rigidbody>();

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (!body) body = GetComponent<Rigidbody>();
            ResetPose();
        }

        private void FixedUpdate()
        {
            if (!body || !body.isKinematic || halfTravel <= 0f) return;
            float cycle = 4f * halfTravel;
            distance = Mathf.Repeat(distance + speed * Time.fixedDeltaTime, cycle);
            body.MovePosition(WorldPosition());
        }

        private void OnDisable()
        {
            if (Application.isPlaying && body) ResetPose();
        }

        private void ResetPose()
        {
            distance = Mathf.Clamp01(startPhase) * 4f * halfTravel;
            Vector3 position = WorldPosition();
            transform.position = position;
            body.position = position;
        }

        private Vector3 WorldPosition()
        {
            float offset = halfTravel > 0f ? Mathf.PingPong(distance, 2f * halfTravel) - halfTravel : 0f;
            Vector3 local = localHomePosition + Vector3.right * offset;
            return transform.parent ? transform.parent.TransformPoint(local) : local;
        }
    }
}
