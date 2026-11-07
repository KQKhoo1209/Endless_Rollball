using UnityEngine;
using EndlessRollball.Track;

namespace EndlessRollball
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class WhaleSurfController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BallMovement player;
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private Transform ballAnchor;
        [SerializeField] private GameObject visual;
        [SerializeField] private WaterSurfCourse course;

        [Header("Swimming animation")]
        [SerializeField, Min(0f)] private float waitingSwimSpeed = .5f;
        [SerializeField, Min(0f)] private float travellingSwimSpeed = 1f;
        [SerializeField, Min(0f)] private float recoveringSwimSpeed = .25f;
        private Animator swimAnimator;
        private static readonly int SwimSpeed = Animator.StringToHash("SwimSpeed");

        [Header("Moving water effects")]
        [SerializeField] private bool movingWaterEffects = true;
        private WaterMovementWake wake;

        [Header("Obstacle hit feedback")]
        [SerializeField, Range(0f, 1f)] private float hitSoundVolume = .55f;
        [SerializeField, Min(.1f)] private float hitFlinchDuration = .35f;
        [SerializeField, Min(0f)] private float hitFlinchDepth = .1f;
        private WhaleHitFeedback hitFeedback;

        [Header("Obstacle recovery")]
        [SerializeField, Min(.2f)] private float sinkSeconds = .7f;
        [SerializeField, Min(1f)] private float sinkDepth = 3f;
        [SerializeField, Min(.5f)] private float recoveryClearance = 1.5f;
        [SerializeField, Min(.1f)] private float recoveryProtectionSeconds = 1.5f;
        private float recoveryElapsed;
        private Vector3 recoveryStart;
        private Vector3 recoveryDestination;
        private bool recoverToExit;

        [Header("Movement")]
        [SerializeField, Min(.1f)] private float forwardSpeed = 6f;
        [SerializeField, Min(.1f)] private float steeringSpeed = 5f;
        [SerializeField, Min(.1f)] private float steeringAcceleration = 20f;
        [SerializeField, Min(2f)] private float channelWidth = 12f;
        [SerializeField] private float waterHeight;

        [Header("Bridge handoffs")]
        [SerializeField, Min(2f)] private float boardingTakeoffDistance = 8f;
        [SerializeField, Min(2f)] private float waitingWhaleDistance = 5f;
        [SerializeField, Min(2f)] private float exitStopDistance = 6f;
        [SerializeField, Range(2f, 16f)] private float bridgeLandingDistance = 6f;
        [SerializeField, Min(.2f)] private float handoffJumpSeconds = .9f;
        [SerializeField, Min(.1f)] private float handoffJumpHeight = 2f;

        [Header("Surfing camera")]
        [SerializeField] private Vector3 cameraExtraOffset = new Vector3(0f, 1.5f, -6f);
        [SerializeField] private float cameraLookAhead = 0f;

        private enum RoutePhase { None, Boarding, Surfing, Recovering, WaitingForExit, ExitJump }
        private RoutePhase phase;
        private Rigidbody whaleBody;
        private CapsuleCollider hull;
        private Rigidbody sphereBody;
        private bool surfing;
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
        private int surfHits;
        private float nextHitAllowedAt;
        private float routeDuration;
        private float routeElapsed;
        private float routeExitZ;
        private float routeStopZ;
        private float jumpElapsed;
        private Vector3 jumpStart;
        private Vector3 jumpEnd;
        private float retireWhaleAtZ = float.PositiveInfinity;

        public int SurfHits => surfHits;
        public bool IsSurfing => surfing;
        public bool IsRecovering => surfing && phase == RoutePhase.Recovering;
        public float BoardingTakeoffDistance => boardingTakeoffDistance;
        public float BoardingSeconds => handoffJumpSeconds;

        private void Awake()
        {
            if (visual != null)
                swimAnimator = visual.GetComponentInChildren<Animator>(true);
            whaleBody = GetComponent<Rigidbody>();
            hull = GetComponent<CapsuleCollider>();
            whaleBody.isKinematic = true;
            whaleBody.useGravity = false;
            whaleBody.interpolation = RigidbodyInterpolation.Interpolate;
            hull.isTrigger = true;
            hull.enabled = false;
            if (visual != null) visual.SetActive(false);
            if (course != null) course.Hide();
        }

        private void Update()
        {
            UpdateSwimmingAnimation();
            hitFeedback?.Tick(Time.timeScale <= 0f || (runController != null &&
                runController.State == SessionFlowState.Paused));
            if (!surfing && !waitingAtEntry && visual != null && player != null &&
                player.transform.position.z > retireWhaleAtZ)
            {
                visual.SetActive(false);
                retireWhaleAtZ = float.PositiveInfinity;
            }
        }

        private void UpdateSwimmingAnimation()
        {
            if (swimAnimator == null || !swimAnimator.isActiveAndEnabled)
                return;

            float speed = waitingSwimSpeed;
            if (phase == RoutePhase.Surfing)
                speed = travellingSwimSpeed;
            else if (phase == RoutePhase.Recovering)
                speed = recoveringSwimSpeed;
            swimAnimator.SetFloat(SwimSpeed, speed, .2f, Time.deltaTime);
        }

        public bool PrepareBeachRoute(float duration)
        {
            if (movingWaterEffects && wake == null && visual != null)
                wake = WaterMovementWake.Create(transform, visual.transform);
            if (player == null || runController == null || ballAnchor == null ||
                visual == null || course == null ||
                !player.TryGetComponent(out Rigidbody _)) return false;
            PrepareHitFeedback();
            course.SetDuration(duration);
            return course.Prepare();
        }

        public void ShowWaitingWhale(float entryZ)
        {
            if (surfing || waitingAtEntry || player == null || ballAnchor == null || visual == null)
                return;
            channelCentreX = player.Corridor.CentreX;
            anchorOffset = transform.InverseTransformPoint(ballAnchor.position);
            Vector3 offset = whaleBody.rotation * anchorOffset;
            Vector3 position = new Vector3(channelCentreX - offset.x, waterHeight,
                entryZ + waitingWhaleDistance - offset.z);
            whaleBody.position = position;
            // Keep the anchor and preview in sync before the next physics step.
            transform.position = position;
            hull.enabled = false;
            visual.SetActive(true);
            waitingAtEntry = true;
            retireWhaleAtZ = float.PositiveInfinity;
        }

        public void HideWaitingWhale()
        {
            if (surfing) return;
            waitingAtEntry = false;
            if (visual != null) visual.SetActive(false);
        }

        private bool CapturePlayer(bool bridgeEntry)
        {
            if (surfing || player == null || runController == null || ballAnchor == null ||
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
            surfHits = 0;
            nextHitAllowedAt = 0f;
            startedAt = Time.time;
            surfing = true;
            runController.SetWhaleCameraFraming(true, cameraExtraOffset, cameraLookAhead);
            waitingAtEntry = false;
            visual.SetActive(true);
            return true;
        }

        [ContextMenu("Begin Surfing")]
        public void BeginSurfing()
        {
            if (!Application.isPlaying || !CapturePlayer(false)) return;
            integratedRoute = false;
            phase = RoutePhase.Surfing;
            Vector3 offset = whaleBody.rotation * anchorOffset;
            Vector3 start = entryPosition - offset;
            start.y = waterHeight;
            whaleBody.position = start;
            transform.position = start;
            sphereBody.position = start + offset;
            StartCourse(true);
        }

        public bool BeginBeachRoute(float exitZ, float duration)
        {
            if (!waitingAtEntry || duration <= 0f) return false;
            Vector3 offset = whaleBody.rotation * anchorOffset;
            float stopZ = exitZ - exitStopDistance;
            float distance = stopZ - (whaleBody.position.z + offset.z);
            if (distance <= 0f || !CapturePlayer(true)) return false;

            integratedRoute = true;
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
            jumpEnd = whaleBody.position + offset;
            runController.Generator.SetBeachSurfRunning(true);
            return true;
        }

        private void PrepareHitFeedback()
        {
            if (hitFeedback == null && visual != null)
                hitFeedback = WhaleHitFeedback.Create(transform, visual.transform);
        }

        private void StartCourse(bool showWater)
        {
            PrepareHitFeedback();
            hitFeedback?.Clear();
            if (movingWaterEffects && wake == null && visual != null)
                wake = WaterMovementWake.Create(transform, visual.transform);
            hull.enabled = true;
            course.Show(whaleBody.position, channelCentreX, forwardSpeed,
                channelWidth, waterHeight, transform);
            wake?.ResetAt(whaleBody.position, course.SurfaceHeight, course.SurfaceMaterial);
            course.SetWaterSurfaceVisible(showWater);
        }

        private void FixedUpdate()
        {
            if (!surfing || runController.State != SessionFlowState.SpecialEvent) return;
            if (phase == RoutePhase.Recovering)
            {
                TickRecovery();
                return;
            }
            if (phase == RoutePhase.Boarding || phase == RoutePhase.ExitJump)
            {
                player.DiscardBufferedInput();
                jumpElapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(jumpElapsed / handoffJumpSeconds);
                Vector3 position = Vector3.Lerp(jumpStart, jumpEnd, t);
                position.y += 4f * handoffJumpHeight * t * (1f - t);
                sphereBody.MovePosition(position);
                if (t >= 1f)
                {
                    if (phase == RoutePhase.Boarding)
                    {
                        phase = RoutePhase.Surfing;
                        runController?.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.Event);
                        StartCourse(false);
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
            if (phase == RoutePhase.WaitingForExit)
            {
                player.DiscardBufferedInput();
                if (runController.Generator.TryGetBeachSurfExitFloor(out float height))
                    BeginExitJump(height);
                return;
            }

            float step = integratedRoute
                ? Mathf.Min(Time.fixedDeltaTime, Mathf.Max(0f, routeDuration - routeElapsed))
                : Time.fixedDeltaTime;
            if (integratedRoute) routeElapsed += step;
            course.ScorePassed(runController.Tasks, hull.bounds.min.z);
            GameplayCommand command = player.ReadEventCommand();
            lateralVelocity = Mathf.MoveTowards(lateralVelocity,
                command.Steering * steeringSpeed, steeringAcceleration * step);
            Vector3 next = whaleBody.position;
            next.x += lateralVelocity * step;
            next.z += forwardSpeed * step;
            next.y = waterHeight;
            Vector3 anchorWorldOffset = whaleBody.rotation * anchorOffset;
            if (integratedRoute)
            {
                next.z = Mathf.Min(next.z, routeStopZ - anchorWorldOffset.z);
                if (routeElapsed >= Mathf.Max(0f, routeDuration - 4f))
                {
                    next.x = Mathf.MoveTowards(whaleBody.position.x,
                        channelCentreX - anchorWorldOffset.x, steeringSpeed * step);
                    lateralVelocity = 0f;
                }
            }
            float halfWidth = Mathf.Max(0f, channelWidth * .5f - hull.bounds.extents.x - .05f);
            float hullOffsetX = transform.TransformVector(hull.center).x;
            next.x = Mathf.Clamp(next.x, channelCentreX - halfWidth - hullOffsetX,
                channelCentreX + halfWidth - hullOffsetX);
            Vector3 velocity = (next - whaleBody.position) / Time.fixedDeltaTime;
            // The imported whale rig has a 90-degree root rotation.
            // Its visible head faces along the course's world +Z direction.
            wake?.Move(next, velocity, course.SurfaceHeight, Vector3.forward);
            whaleBody.MovePosition(next);
            sphereBody.MovePosition(next + anchorWorldOffset);
            if (integratedRoute && routeElapsed >= routeDuration)
            {
                phase = RoutePhase.WaitingForExit;
                wake?.Stop();
                hull.enabled = false;
                course.Hide();
            }
        }

        private void BeginRecovery(WhaleSurfObstacle obstacle)
        {
            Bounds footprint = hull.bounds;
            Vector3 hullOffset = footprint.center - whaleBody.position;
            Vector3 anchorWorldOffset = whaleBody.rotation * anchorOffset;
            float limit = integratedRoute
                ? routeStopZ - anchorWorldOffset.z + hullOffset.z
                : float.PositiveInfinity;
            recoverToExit = !course.TryFindRecovery(obstacle, footprint, channelCentreX,
                channelWidth, limit, recoveryClearance, out Vector3 centre);
            recoveryDestination = recoverToExit
                ? new Vector3(channelCentreX - anchorWorldOffset.x, waterHeight,
                    routeStopZ - anchorWorldOffset.z)
                : centre - hullOffset;
            recoveryDestination.y = waterHeight;
            course.ExcludeRecoverySkips(runController.Tasks, footprint.center.z,
                recoveryDestination.z + hullOffset.z - footprint.extents.z);
            recoveryStart = whaleBody.position;
            recoveryElapsed = 0f;
            lateralVelocity = 0f;
            phase = RoutePhase.Recovering;
            hull.enabled = false;
            wake?.Stop();
            player.DiscardBufferedInput();
            runController.SetCarrierRecovery(true);
        }

        private void TickRecovery()
        {
            player.DiscardBufferedInput();
            recoveryElapsed += Time.fixedDeltaTime;
            float progress = Mathf.Clamp01(recoveryElapsed / sinkSeconds);
            Vector3 sunk = recoveryStart + Vector3.down * (sinkDepth * Mathf.SmoothStep(0f, 1f, progress));
            Vector3 anchorWorldOffset = whaleBody.rotation * anchorOffset;
            if (progress < 1f)
            {
                whaleBody.MovePosition(sunk);
                sphereBody.MovePosition(sunk + anchorWorldOffset);
                return;
            }

            whaleBody.position = recoveryDestination;
            transform.position = recoveryDestination;
            sphereBody.position = recoveryDestination + anchorWorldOffset;
            player.transform.position = sphereBody.position;
            wake?.ResetAt(recoveryDestination, course.SurfaceHeight, course.SurfaceMaterial);
            hitFeedback?.Clear();
            nextHitAllowedAt = Time.time + recoveryProtectionSeconds;
            if (recoverToExit)
            {
                phase = RoutePhase.WaitingForExit;
                course.Hide();
            }
            else
            {
                if (integratedRoute)
                {
                    float remainingSeconds = Mathf.Max(Time.fixedDeltaTime, routeDuration - routeElapsed);
                    forwardSpeed = Mathf.Max(0f, routeStopZ - anchorWorldOffset.z - recoveryDestination.z) / remainingSeconds;
                }
                phase = RoutePhase.Surfing;
                hull.enabled = true;
            }
            runController.SetCarrierRecovery(false);
        }

        private void BeginExitJump(float floorHeight)
        {
            runController.SetCarrierRecovery(false);
            // The whale is stationary before the bridge throughout this jump.
            phase = RoutePhase.ExitJump;
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
            hitFeedback?.Clear();
            wake?.Stop();
            surfing = false;
            integratedRoute = false;
            phase = RoutePhase.None;
            RestorePlayer(true);
            hull.enabled = false;
            course.Hide();
            // Keep the stopped whale visible behind the player until out of view.
            retireWhaleAtZ = whaleBody.position.z + 40f;
            runController.Generator.SetBeachSurfRunning(false);
            runController.EndSpecialEvent(Mathf.Max(0f, Time.time - startedAt));
            runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.RoadAfterEvent);
            runController.SetWhaleCameraFraming(false, Vector3.zero, 0f);
        }

        [ContextMenu("Cancel Surfing - Return To Bridge")]
        public void CancelSurfing()
        {
            if (!surfing) return;
            if (integratedRoute)
            {
                // The old entry may already have been recycled. Return forward.
                if (runController.Generator.TryGetBeachSurfExitFloor(out float height))
                {
                    hull.enabled = false;
                    course.Hide();
                    BeginExitJump(height);
                }
                else Debug.LogWarning("The exit bridge is not ready; continue surfing before cancelling.", this);
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
            if (!surfing || phase != RoutePhase.Surfing || Time.time < nextHitAllowedAt) return;
            WhaleSurfObstacle obstacle = other.GetComponentInParent<WhaleSurfObstacle>();
            if (obstacle == null) return;
            nextHitAllowedAt = Time.time + 1.5f;
            surfHits++;
            Vector3 direction = other.bounds.center - hull.bounds.center;
            direction.y = 0f;
            if (direction.sqrMagnitude < .001f)
                direction = Vector3.forward;
            Vector3 probe = hull.bounds.center + direction.normalized * (hull.bounds.size.magnitude + 1f);
            Vector3 contact = hull.ClosestPoint(probe);
            contact.y = course.SurfaceHeight + .12f;
            hitFeedback?.React(contact, course.SurfaceMaterial, hitSoundVolume, hitFlinchDuration, hitFlinchDepth);
            runController.Tasks.RecordEventNavigation(obstacle.GetInstanceID(),false);
            BeginRecovery(obstacle);
            EndlessRollball.Infrastructure.RuntimeLog.Info($"[WhaleHit] {obstacle.name}; total hits: {surfHits}", this);
        }

        private void OnDisable()
        {
            hitFeedback?.Clear();
            wake?.Clear();
            waitingAtEntry = false;
            if (surfing && sphereBody != null)
            {
                surfing = false;
                integratedRoute = false;
                phase = RoutePhase.None;
                RestorePlayer(false);
                if (runController != null)
                {
                    runController.Generator.SetBeachSurfRunning(false);
                    runController.EndSpecialEvent(Mathf.Max(0f, Time.time - startedAt));
            runController.GetComponent<GameSequenceCoordinator>()?.Notify(GameSequenceCoordinator.SequencePhase.RoadAfterEvent);
                }
            }
            if (hull != null) hull.enabled = false;
            if (runController != null)
                runController.SetWhaleCameraFraming(false, Vector3.zero, 0f);
            if (visual != null) visual.SetActive(false);
            if (course != null) course.Hide();
        }
    }
}
