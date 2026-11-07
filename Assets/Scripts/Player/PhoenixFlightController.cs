using UnityEngine;
using EndlessRollball.Track;

namespace EndlessRollball
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class PhoenixFlightController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BallMovement player;
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private Transform ballAnchor;
        [SerializeField] private GameObject visual;
        [SerializeField] private PhoenixLavaCourse course;

        [Header("Movement")]
        [SerializeField, Min(.1f)] private float forwardSpeed = 6f;
        [SerializeField, Min(.1f)] private float steeringSpeed = 5f;
        [SerializeField, Min(.1f)] private float steeringAcceleration = 20f;
        [SerializeField, Min(2f)] private float channelWidth = 12f;
        [SerializeField] private float flightHeight = .6f;

        [Header("Bridge handoffs")]
        [SerializeField, Min(2f)] private float boardingTakeoffDistance = 8f;
        [SerializeField, Min(2f)] private float waitingPhoenixDistance = 5f;
        [SerializeField, Min(2f)] private float exitStopDistance = 6f;
        [SerializeField, Range(2f, 16f)] private float bridgeLandingDistance = 6f;
        [SerializeField, Min(.2f)] private float handoffJumpSeconds = .9f;
        [SerializeField, Min(.1f)] private float handoffJumpHeight = 2f;

        [Header("Flying camera")]
        [SerializeField] private Vector3 cameraExtraOffset = new Vector3(0f, 2f, -6f);
        [SerializeField] private float cameraLookAhead = 8f;

        private enum RoutePhase { None, Boarding, WaitingForFlight, Flying, WaitingForExit, ExitJump }
        private RoutePhase phase;
        private Rigidbody phoenixBody;
        private CapsuleCollider hull;
        private Rigidbody sphereBody;
        private bool flying;
        [SerializeField] private PhoenixFlightVisual flightVisual;

        private bool integratedRoute;
        private bool waitingAtEntry;
        private float lateralVelocity;
        private float channelCentreX;
        private float startedAt;
        private Vector3 anchorOffset;
        private Vector3 entryPosition;
        private Quaternion entryRotation;
        private bool savedMovementEnabled;
        private bool savedGravity;
        private bool savedKinematic;
        private bool savedDetectCollisions;
        private RigidbodyConstraints savedConstraints;
        private RigidbodyInterpolation savedInterpolation;
        [SerializeField] private int flightHits;
        private float nextHitAllowedAt;
        private float routeDuration;
        private float flightStartNotBefore;
        private float routeElapsed;
        private float routeExitZ;
        private float routeStopZ;
        private float jumpElapsed;
        private Vector3 jumpStart;
        private Vector3 jumpEnd;
        private float retirePhoenixAtZ = float.PositiveInfinity;

        public int FlightHits => flightHits;
        public bool IsFlying => flying;
        public float BoardingTakeoffDistance => boardingTakeoffDistance;
        public float BoardingSeconds => handoffJumpSeconds;

        private void Awake()
        {
            phoenixBody = GetComponent<Rigidbody>();
            hull = GetComponent<CapsuleCollider>();
            phoenixBody.isKinematic = true;
            phoenixBody.useGravity = false;
            phoenixBody.interpolation = RigidbodyInterpolation.Interpolate;
            hull.isTrigger = true;
            hull.enabled = false;
            if (flightVisual != null) flightVisual.PrepareModel();
            if (visual != null) visual.SetActive(false);
            if (course != null) course.Hide();
        }

        private void Update()
        {
            if (!flying && !waitingAtEntry && visual != null && player != null &&
                player.transform.position.z > retirePhoenixAtZ)
            {
                visual.SetActive(false);
                retirePhoenixAtZ = float.PositiveInfinity;
            }
        }

        public bool PrepareLavaRoute(float duration)
        {
            if (player == null || runController == null || ballAnchor == null ||
                visual == null || course == null ||
                !player.TryGetComponent(out Rigidbody _)) return false;
            if (flightVisual != null) flightVisual.PrepareModel();
            course.SetDuration(duration);
            return course.Prepare();
        }

        public void ShowWaitingPhoenix(float entryZ)
        {
            if (flying || waitingAtEntry || player == null || ballAnchor == null || visual == null)
                return;
            channelCentreX = player.Corridor.CentreX;
            anchorOffset = transform.InverseTransformPoint(ballAnchor.position);
            Vector3 offset = phoenixBody.rotation * anchorOffset;
            Vector3 position = new Vector3(channelCentreX - offset.x, flightHeight,
                entryZ + waitingPhoenixDistance - offset.z);
            phoenixBody.position = position;
            // Keep the anchor and preview in sync before the next physics step.
            transform.position = position;
            hull.enabled = false;
            flightVisual?.SetSteering(0f);
            visual.SetActive(true);
            waitingAtEntry = true;
            retirePhoenixAtZ = float.PositiveInfinity;
        }

        public void HideWaitingPhoenix()
        {
            if (flying) return;
            waitingAtEntry = false;
            if (visual != null) visual.SetActive(false);
        }

        private bool CapturePlayer(bool bridgeEntry)
        {
            if (flying || player == null || runController == null || ballAnchor == null ||
                visual == null || course == null || !course.Prepare()) return false;
            sphereBody = player.GetComponent<Rigidbody>();
            if (sphereBody == null || !runController.TryBeginSpecialEvent(
                keepPhysicsRunning: true, allowRespawnFlashing: bridgeEntry)) return false;

            entryPosition = sphereBody.position;
            entryRotation = sphereBody.rotation;
            savedMovementEnabled = player.enabled;
            savedGravity = sphereBody.useGravity;
            savedKinematic = sphereBody.isKinematic;
            savedDetectCollisions = sphereBody.detectCollisions;
            savedConstraints = sphereBody.constraints;
            savedInterpolation = sphereBody.interpolation;
            channelCentreX = player.Corridor.CentreX;
            anchorOffset = transform.InverseTransformPoint(ballAnchor.position);
            player.DiscardBufferedInput();
            player.enabled = false;
            if (!sphereBody.isKinematic)
            {
                sphereBody.linearVelocity = Vector3.zero;
                sphereBody.angularVelocity = Vector3.zero;
            }
            sphereBody.useGravity = false;
            sphereBody.isKinematic = true;
            sphereBody.detectCollisions = false;
            sphereBody.interpolation = RigidbodyInterpolation.Interpolate;
            lateralVelocity = 0f;
            flightVisual?.SetSteering(0f);
            flightHits = 0;
            nextHitAllowedAt = 0f;
            startedAt = Time.time;
            flying = true;
            runController.SetCarrierCameraFraming(true, cameraExtraOffset, cameraLookAhead);
            waitingAtEntry = false;
            visual.SetActive(true);
            return true;
        }

        [ContextMenu("Begin Flying")]
        public void BeginFlying()
        {
            if (!Application.isPlaying || !CapturePlayer(false)) return;
            integratedRoute = false;
            phase = RoutePhase.Flying;
            Vector3 offset = phoenixBody.rotation * anchorOffset;
            Vector3 start = entryPosition - offset;
            start.y = flightHeight;
            phoenixBody.position = start;
            transform.position = start;
            sphereBody.position = start + offset;
            routeElapsed = 0f;
            StartCourse();
        }

        public bool BeginLavaRoute(float exitZ, float duration, float minimumSecondsUntilFlight = 0f)
        {
            if (!waitingAtEntry || duration <= 0f) return false;
            Vector3 offset = phoenixBody.rotation * anchorOffset;
            float stopZ = exitZ - exitStopDistance;
            float distance = stopZ - (phoenixBody.position.z + offset.z);
            if (distance <= 0f || !CapturePlayer(true)) return false;

            integratedRoute = true;
            flightStartNotBefore = Time.time + Mathf.Max(0f, minimumSecondsUntilFlight);
            routeDuration = duration;
            routeElapsed = 0f;
            routeExitZ = exitZ;
            routeStopZ = stopZ;
            forwardSpeed = distance / duration;
            course.SetDuration(duration);
            course.Hide();
            hull.enabled = false;
            phase = RoutePhase.Boarding;
            jumpElapsed = 0f;
            jumpStart = sphereBody.position;
            jumpEnd = phoenixBody.position + offset;
            if (!runController.Generator.CommitPhoenixLava())
            {
                sphereBody.position = entryPosition;
                player.transform.position = entryPosition;
                FinishRoute();
                return false;
            }
            return true;
        }

        private void StartCourse()
        {
            hull.enabled = true;
            course.Show(phoenixBody.position, channelCentreX, forwardSpeed,
                channelWidth, flightHeight, transform, runController.Generator.LavaSurfaceHeight);

        }

        private void LateUpdate()
        {
            if (!flying || sphereBody == null || ballAnchor == null ||
                runController == null ||
                runController.State != SessionFlowState.SpecialEvent || Time.timeScale <= 0f)
                return;

            bool carrying = phase == RoutePhase.WaitingForFlight ||
                            phase == RoutePhase.Flying || phase == RoutePhase.WaitingForExit;
            if (!carrying) return;

            // Run after PhoenixFlightVisual so the sphere follows the animated
            // feet, including their bob and bank, without interpolation lag.
            sphereBody.interpolation = RigidbodyInterpolation.None;
            sphereBody.position = ballAnchor.position;
        }

        private void FixedUpdate()
        {
            if (!flying || runController.State != SessionFlowState.SpecialEvent) return;
            if (phase == RoutePhase.Boarding || phase == RoutePhase.ExitJump)
            {
                player.DiscardBufferedInput();
                jumpElapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(jumpElapsed / handoffJumpSeconds);
                Vector3 position = Vector3.Lerp(jumpStart, jumpEnd, t);
                position.y += PhoenixLavaMath.JumpLift(t, handoffJumpHeight);
                sphereBody.MovePosition(position);
                if (t >= 1f)
                {
                    if (phase == RoutePhase.Boarding)
                    {
                        phase = RoutePhase.WaitingForFlight;
                        sphereBody.position = jumpEnd;
                    }
                    else
                    {
                        sphereBody.position = jumpEnd;
                        player.transform.position = jumpEnd;
                        FinishRoute();
                    }
                }
                return;
            }
            if (phase == RoutePhase.WaitingForFlight)
            {
                player.DiscardBufferedInput();
                if (Time.time >= flightStartNotBefore)
                {
                    phase = RoutePhase.Flying;
                    runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.Event);
                    StartCourse();
                }
                return;
            }
            if (phase == RoutePhase.WaitingForExit)
            {
                player.DiscardBufferedInput();
                if (runController.Generator.TryGetPhoenixExitFloor(out float height))
                    BeginExitJump(height);
                return;
            }

            float step = integratedRoute
                ? Mathf.Min(Time.fixedDeltaTime, Mathf.Max(0f, routeDuration - routeElapsed))
                : Time.fixedDeltaTime;
            routeElapsed += step;
            course.Tick(routeElapsed);
            course.ScorePassed(runController.Tasks,routeElapsed);
            GameplayCommand command = player.ReadEventCommand();
            lateralVelocity = Mathf.MoveTowards(lateralVelocity,
                command.Steering * steeringSpeed, steeringAcceleration * step);
            Vector3 next = phoenixBody.position;
            next.x += lateralVelocity * step;
            next.z += forwardSpeed * step;
            next.y = flightHeight;
            Vector3 anchorWorldOffset = phoenixBody.rotation * anchorOffset;
            if (integratedRoute)
            {
                next.z = Mathf.Min(next.z, routeStopZ - anchorWorldOffset.z);
                if (routeElapsed >= Mathf.Max(0f, routeDuration - 4f))
                {
                    next.x = Mathf.MoveTowards(phoenixBody.position.x,
                        channelCentreX - anchorWorldOffset.x, steeringSpeed * step);
                    lateralVelocity = 0f;
                }
            }
            float halfWidth = Mathf.Max(0f, channelWidth * .5f - hull.bounds.extents.x - .05f);
            float hullOffsetX = transform.TransformVector(hull.center).x;
            next.x = Mathf.Clamp(next.x, channelCentreX - halfWidth - hullOffsetX,
                channelCentreX + halfWidth - hullOffsetX);
            flightVisual?.SetSteering((next.x - phoenixBody.position.x) /
                (Mathf.Max(.1f, steeringSpeed) * Time.fixedDeltaTime));
            phoenixBody.MovePosition(next);
            if (integratedRoute && routeElapsed >= routeDuration)
            {
                phase = RoutePhase.WaitingForExit;
                flightVisual?.SetSteering(0f);
                hull.enabled = false;
                course.Hide();
            }
        }

        private void BeginExitJump(float floorHeight)
        {
            // The phoenix is stationary before the bridge throughout this jump.
            sphereBody.interpolation = RigidbodyInterpolation.Interpolate;
            phase = RoutePhase.ExitJump;
            flightVisual?.SetSteering(0f);
            runController?.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.Return);
            jumpElapsed = 0f;
            jumpStart = sphereBody.position;
            jumpEnd = new Vector3(channelCentreX, floorHeight + player.SphereRadius + .05f,
                routeExitZ + bridgeLandingDistance);
        }

        private void RestorePlayer(bool resume)
        {
            sphereBody.constraints = savedConstraints;
            sphereBody.isKinematic = savedKinematic;
            sphereBody.useGravity = savedGravity;
            sphereBody.detectCollisions = savedDetectCollisions;
            sphereBody.interpolation = savedInterpolation;
            if (!savedKinematic && resume)
            {
                player.ResumeMotion();
                sphereBody.linearVelocity = new Vector3(0f, 0f, player.ForwardSpeed);
            }
            player.enabled = savedMovementEnabled;
            player.DiscardBufferedInput();
        }

        private void FinishRoute()
        {
            flying = false;
            integratedRoute = false;
            phase = RoutePhase.None;
            RestorePlayer(true);
            hull.enabled = false;
            course.Hide();
            // Keep the stopped phoenix visible behind the player until out of view.
            retirePhoenixAtZ = phoenixBody.position.z + 40f;
            runController.Generator.SetPhoenixRunning(false);
            runController.EndSpecialEvent(Mathf.Max(0f, Time.time - startedAt));
            runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.RoadAfterEvent);
            runController.SetCarrierCameraFraming(false, Vector3.zero, 0f);
        }

        [ContextMenu("Cancel Flying - Return To Bridge")]
        public void CancelFlying()
        {
            if (!flying) return;
            if (integratedRoute)
            {
                // The old entry may already have been recycled. Return forward.
                if (runController.Generator.TryGetPhoenixExitFloor(out float height))
                {
                    hull.enabled = false;
                    course.Hide();
                    BeginExitJump(height);
                }
                else Debug.LogWarning("The exit bridge is not ready; continue flying before cancelling.", this);
                return;
            }
            sphereBody.position = entryPosition;
            sphereBody.rotation = entryRotation;
            player.transform.SetPositionAndRotation(entryPosition, entryRotation);
            FinishRoute();
            visual.SetActive(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!flying || phase != RoutePhase.Flying || Time.time < nextHitAllowedAt) return;
            PhoenixLavaHazard obstacle = other.GetComponentInParent<PhoenixLavaHazard>();
            if (obstacle == null) return;
            nextHitAllowedAt = Time.time + 1.5f;
            flightHits++;
            runController.Tasks.RecordEventNavigation(obstacle.GetInstanceID(),false);
            if (flightVisual != null) flightVisual.React();
            EndlessRollball.Infrastructure.RuntimeLog.Info($"[PhoenixHit] {obstacle.name}; total hits: {flightHits}", this);
        }

        private void OnDisable()
        {
            waitingAtEntry = false;
            if (flying && sphereBody != null)
            {
                flying = false;
                integratedRoute = false;
                phase = RoutePhase.None;
                RestorePlayer(false);
                if (runController != null)
                {
                    runController.Generator.SetPhoenixRunning(false);
                    runController.EndSpecialEvent(Mathf.Max(0f, Time.time - startedAt));
            runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.RoadAfterEvent);
                }
            }
            if (hull != null) hull.enabled = false;
            if (runController != null)
                runController.SetCarrierCameraFraming(false, Vector3.zero, 0f);
            if (flightVisual != null) flightVisual.PrepareModel();
            if (visual != null) visual.SetActive(false);
            if (course != null) course.Hide();
        }
    }
}
