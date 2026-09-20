using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    public enum WaypointTraversalMode
    {
        PingPong,
        Loop
    }

    public sealed class MovingObstacle : MonoBehaviour
    {
        [SerializeField] private Vector3[] localWaypointOffsets =
        {
            new Vector3(-3f, 0f, 0f),
            new Vector3(3f, 0f, 0f)
        };
        [SerializeField] private WaypointTraversalMode traversalMode = WaypointTraversalMode.PingPong;
        [SerializeField, Min(0f)] private float speed = 1.5f;
        [SerializeField] private Vector3 originLocalPosition;
        [SerializeField] private bool hasConfiguredOrigin;

        private int waypointIndex;
        private int direction = 1;

        public int WaypointCount => localWaypointOffsets?.Length ?? 0;
        public int CurrentWaypointIndex => waypointIndex;
        public float CurrentSpeed => speed;
        public WaypointTraversalMode TraversalMode => traversalMode;
        public Vector3 Origin => originLocalPosition;

        private void Awake()
        {
            CaptureOrigin();
        }

        private void OnEnable()
        {
            CaptureOrigin();
            ResetMotion();
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Configure(float configuredSpeed)
        {
            speed = Mathf.Max(0f, configuredSpeed);
            CaptureOrigin();
            ResetMotion();
        }

        public void ConfigureForPrefab(
            Vector3[] configuredWaypointOffsets,
            float configuredSpeed,
            WaypointTraversalMode configuredTraversalMode)
        {
            localWaypointOffsets = configuredWaypointOffsets == null
                ? Array.Empty<Vector3>()
                : (Vector3[])configuredWaypointOffsets.Clone();
            speed = Mathf.Max(0f, configuredSpeed);
            traversalMode = configuredTraversalMode;
            originLocalPosition = transform.localPosition;
            hasConfiguredOrigin = true;
            ResetMotion();
        }

        public Vector3 GetWaypointOffset(int index)
        {
            if (localWaypointOffsets == null || index < 0 || index >= localWaypointOffsets.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return localWaypointOffsets[index];
        }

        public void Advance(float deltaTime)
        {
            if (deltaTime <= 0f || speed <= 0f || WaypointCount < 2)
            {
                return;
            }

            float remainingDistance = speed * deltaTime;
            int safety = 0;

            while (remainingDistance > 0f && safety++ < WaypointCount * 4)
            {
                int targetIndex = GetTargetWaypointIndex();
                Vector3 target = originLocalPosition + localWaypointOffsets[targetIndex];
                float distance = Vector3.Distance(transform.localPosition, target);

                if (distance <= 0.0001f)
                {
                    ReachWaypoint(targetIndex);
                    continue;
                }

                if (remainingDistance < distance)
                {
                    transform.localPosition = Vector3.MoveTowards(
                        transform.localPosition,
                        target,
                        remainingDistance);
                    break;
                }

                transform.localPosition = target;
                remainingDistance -= distance;
                ReachWaypoint(targetIndex);
            }
        }

        public void ResetMotion()
        {
            waypointIndex = 0;
            direction = 1;

            if (WaypointCount > 0)
            {
                transform.localPosition = originLocalPosition + localWaypointOffsets[0];
            }
            else
            {
                transform.localPosition = originLocalPosition;
            }
        }

        private void CaptureOrigin()
        {
            if (hasConfiguredOrigin)
            {
                return;
            }

            originLocalPosition = transform.localPosition;
            hasConfiguredOrigin = true;
        }

        private int GetTargetWaypointIndex()
        {
            if (traversalMode == WaypointTraversalMode.Loop)
            {
                return (waypointIndex + 1) % WaypointCount;
            }

            return Mathf.Clamp(waypointIndex + direction, 0, WaypointCount - 1);
        }

        private void ReachWaypoint(int reachedIndex)
        {
            waypointIndex = reachedIndex;

            if (traversalMode != WaypointTraversalMode.PingPong)
            {
                return;
            }

            if (waypointIndex == WaypointCount - 1)
            {
                direction = -1;
            }
            else if (waypointIndex == 0)
            {
                direction = 1;
            }
        }
    }
}
