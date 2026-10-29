using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class JunctionTrafficController : MonoBehaviour
    {
        [Header("Traffic")]
        [SerializeField]
        private Transform trafficRoot;

        [Tooltip("Use your existing stationary vehicle obstacle prefabs.")]
        [SerializeField]
        private GameObject[] vehiclePrefabs = Array.Empty<GameObject>();

        [Header("Optional warning indicator")]
        [SerializeField]
        private GameObject redWarning;

        [Header("Temporary scene preview")]
        [Tooltip("Assign only on a scene instance while testing.")]
        [SerializeField]
        private Transform previewPlayer;

        [SerializeField]
        private int previewSeed = 24001;

        private sealed class LaneCar
        {
            public GameObject Root;
            public Rigidbody Body;
            public float LaneZ;
            public bool LeftToRight;
            public bool CrossEarly;
            public float Progress;
        }

        private LaneCar[] cars = Array.Empty<LaneCar>();
        private Transform player;
        private bool prepared;
        private bool running;
        private Light[] warningLights = Array.Empty<Light>();

        public Transform TrafficRoot => trafficRoot;
        public GameObject[] VehiclePrefabs => vehiclePrefabs;

        public static float PlannedSafePassage(int seed)
        {
            return new System.Random(seed).Next(2) == 0 ? 1.7f : -1.7f;
        }

        public Collider[] GetTrafficColliders()
        {
            var result = new List<Collider>();
            foreach (LaneCar car in cars)
                if (car.Root != null)
                    result.AddRange(car.Root.GetComponentsInChildren<Collider>(true));
            return result.ToArray();
        }

        // Useful for the later planner/scoring integration.
        public float SafePassageX { get; private set; }

        private void Start()
        {
            if (previewPlayer != null && !prepared)
            {
                Prepare(previewPlayer, previewSeed);
                SetRunning(true);
            }
        }

        private void OnDisable()
        {
            prepared = false;
            running = false;
            player = null;

            foreach (LaneCar car in cars)
                if (car.Root != null)
                    car.Root.SetActive(false);

            SetWarning(false);
        }

        public bool Prepare(Transform playerTransform, int seed)
        {
            ClearTraffic();
            warningLights = GetComponentsInChildren<Light>(true);
            SetWarning(false);

            if (playerTransform == null ||
                trafficRoot == null ||
                vehiclePrefabs == null ||
                vehiclePrefabs.Length == 0)
            {
                Debug.LogError(
                    "Assign a traffic container, vehicles and player.",
                    this);
                return false;
            }

            foreach (GameObject prefab in vehiclePrefabs)
            {
                if (prefab == null ||
                    prefab.GetComponentsInChildren<Rigidbody>(true).Length > 0 ||
                    prefab.GetComponentsInChildren<Collider>(true).Length == 0)
                {
                    Debug.LogError(
                        "Traffic sources need colliders and no Rigidbody.",
                        this);
                    return false;
                }
            }

            player = playerTransform;
            var random = new System.Random(seed);

            // One lane supplies the active avoidance challenge.
            // The other car crosses earlier and clears before arrival.
            int challengeLane = random.Next(2);

            cars = new LaneCar[2];

            cars[0] = CreateCar(
                vehiclePrefabs[random.Next(vehiclePrefabs.Length)],
                10f,
                false,
                challengeLane != 0,
                "NearLane_RightToLeft");

            cars[1] = CreateCar(
                vehiclePrefabs[random.Next(vehiclePrefabs.Length)],
                14f,
                true,
                challengeLane != 1,
                "FarLane_LeftToRight");

            SafePassageX = challengeLane == 0 ? 1.7f : -1.7f;

            prepared = true;
            running = false;

            SetWarning(false);

            return true;
        }

        public void SetRunning(bool value)
        {
            running = prepared && value;
        }

        private LaneCar CreateCar(
            GameObject prefab,
            float laneZ,
            bool leftToRight,
            bool crossEarly,
            string objectName)
        {
            var holder = new GameObject(objectName);
            holder.transform.SetParent(trafficRoot, false);

            GameObject vehicle = Instantiate(
                prefab, holder.transform, false);

            vehicle.transform.localPosition = Vector3.zero;
            vehicle.transform.localRotation = Quaternion.Euler(
                0f, leftToRight ? 90f : -90f, 0f);
            vehicle.transform.localScale = Vector3.one;
            vehicle.SetActive(true);

            Rigidbody body = holder.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var car = new LaneCar
            {
                Root = holder,
                Body = body,
                LaneZ = laneZ,
                LeftToRight = leftToRight,
                CrossEarly = crossEarly,
                Progress = 0f
            };

            Vector3 position = GetWorldPosition(car);
            holder.transform.position = position;
            body.position = position;

            return car;
        }

        private void FixedUpdate()
        {
            if (!running ||
                player == null ||
                !trafficRoot.gameObject.activeInHierarchy)
            {
                return;
            }

            float playerZ =
                transform.InverseTransformPoint(player.position).z;

            bool trafficRemaining = false;

            foreach (LaneCar car in cars)
            {
                float startZ;
                float finishZ;

                if (car.CrossEarly)
                {
                    // This vehicle clears three metres before
                    // the player reaches its crossing lane.
                    finishZ = car.LaneZ - 3f;
                    startZ = finishZ - 24f;
                }
                else
                {
                    startZ = car.LaneZ - 18f;
                    finishZ = car.LaneZ + 9f;
                }

                float candidate = Mathf.InverseLerp(
                    startZ, finishZ, playerZ);

                car.Progress = Mathf.Max(car.Progress, candidate);
                car.Body.MovePosition(GetWorldPosition(car));

                if (car.Progress < 1f)
                    trafficRemaining = true;
            }

            SetWarning(playerZ >= -24f && trafficRemaining);
        }

        private void SetWarning(bool visible)
        {
            if (redWarning != null && redWarning.activeSelf != visible)
                redWarning.SetActive(visible);
            foreach (Light warning in warningLights)
                if (warning != null && warning.enabled != visible)
                    warning.enabled = visible;
        }

        private Vector3 GetWorldPosition(LaneCar car)
        {
            float x = Mathf.Lerp(-12f, 12f, car.Progress);

            if (!car.LeftToRight)
                x = -x;

            return transform.TransformPoint(
                new Vector3(x, 0f, car.LaneZ));
        }

        private void ClearTraffic()
        {
            prepared = false;
            running = false;

            foreach (LaneCar car in cars)
            {
                if (car.Root == null)
                    continue;

                car.Root.SetActive(false);
                Destroy(car.Root);
            }

            cars = Array.Empty<LaneCar>();
        }
    }
}
