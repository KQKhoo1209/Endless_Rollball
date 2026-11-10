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

        [Header("Swoop entrance")]
        [SerializeField, Min(.1f)] private float approachSeconds = 1.2f;
        [SerializeField] private Vector3 approachOffset = new Vector3(0f, 3f, -10f);
        [SerializeField] private Vector3 interceptionOffset = new Vector3(0f, 1.5f, -2f);
        [SerializeField, Range(0f, 60f)] private float approachPitch = 40f;
        [SerializeField, Range(.2f, .8f)] private float catchFraction = .5f;
        private Vector3 flightStartPosition, nominalAnchorOffset, interceptionStart;
        private float approachStartZ, approachTakeoffZ, approachProgress;
        private bool carryAnchorFitted;

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

        private enum RoutePhase { None, Approaching, Intercepting, CarryingToStart, WaitingForFlight, Flying, WaitingForExit, ExitJump }
        private RoutePhase phase;
        private Rigidbody phoenixBody;
        private EventSpawnAudio spawnAudio;
        private StreetImpactAudio impactAudio;
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
        public float EntranceLeadSeconds => approachSeconds;

        private void Awake()
        {
            spawnAudio = GetComponent<EventSpawnAudio>();
            impactAudio = FindFirstObjectByType<StreetImpactAudio>();
            phoenixBody = GetComponent<Rigidbody>();
            hull = GetComponent<CapsuleCollider>();
            phoenixBody.isKinematic = true;
            phoenixBody.useGravity = false;
            phoenixBody.interpolation = RigidbodyInterpolation.Interpolate;
            hull.isTrigger = true;
            hull.enabled = false;
            if (flightVisual != null) flightVisual.PrepareModel();
            if (ballAnchor != null)
                nominalAnchorOffset = transform.InverseTransformPoint(ballAnchor.position);
            if (visual != null) visual.SetActive(false);
            if (course != null) course.Hide();
        }

        private void Update()
        {
            if (runController != null &&
                (runController.State == SessionFlowState.Ending ||
                 runController.State == SessionFlowState.Results ||
                 runController.State == SessionFlowState.SetupError))
            {
                if (waitingAtEntry) HideWaitingPhoenix();
                if (flying && (phase == RoutePhase.Intercepting || phase == RoutePhase.CarryingToStart))
                {
                    flying = integratedRoute = false;
                    ResetEntrance();
                    RestorePlayer(false);
                    hull.enabled = false;
                    course.Hide();
                    visual.SetActive(false);
                    runController.Generator.SetPhoenixRunning(false);
                    runController.SetCarrierCameraFraming(false, Vector3.zero, 0f);
                }
                return;
            }
            if (waitingAtEntry && runController != null &&
                runController.State == SessionFlowState.Running && Time.timeScale > 0f)
            {
                float distance = approachTakeoffZ - approachStartZ;
                approachProgress = Mathf.Max(approachProgress, distance <= .01f ? 1f :
                    Mathf.Clamp01((player.transform.position.z - approachStartZ) / distance));
                flightVisual?.SetEntrancePitch(approachPitch * Mathf.Lerp(.75f, 1f,
                    Mathf.SmoothStep(0f, 1f, approachProgress)));
            }
            if (flying && (phase == RoutePhase.Intercepting || phase == RoutePhase.CarryingToStart) &&
                runController.State == SessionFlowState.SpecialEvent && Time.timeScale > 0f)
            {
                jumpElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(jumpElapsed / handoffJumpSeconds);
                // Keep the fast, nose-down silhouette until the final pull-up.
                float catchProgress = t / Mathf.Max(.01f, catchFraction);
                flightVisual?.SetEntrancePitch(approachPitch * (1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(.45f, 1f, catchProgress))));
            }
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
            FitCarryAnchor();
            course.SetDuration(duration);
            return course.Prepare();
        }

        private void FitCarryAnchor()
        {
            if (carryAnchorFitted || player == null || ballAnchor == null || ballAnchor.parent == null)
                return;
            // BallAnchor is authored under Legs. Measure after model normalisation,
            // before the preview starts, so import scale and animated pitch cannot
            // leave a fixed local-space gap between the toes and the sphere.
            var feet = ballAnchor.parent.GetComponentsInChildren<Renderer>(true);
            if (feet.Length == 0) return;
            Vector3 centre = ballAnchor.position;
            float radius = Mathf.Max(.01f, player.SphereRadius);
            float fittedY = 0f;
            int contacts = 0;
            foreach (var foot in feet)
            {
                Bounds bounds = foot.bounds;
                float dx = bounds.center.x - centre.x;
                float dz = bounds.center.z - centre.z;
                float horizontalSquared = dx * dx + dz * dz;
                if (horizontalSquared >= radius * radius) continue;
                // The sphere surface is lower away from its centre; account for
                // the separation of the feet instead of aligning only its very top.
                fittedY += bounds.min.y - Mathf.Sqrt(radius * radius - horizontalSquared);
                contacts++;
            }
            if (contacts == 0) return;
            centre.y = fittedY / contacts + .015f;
            ballAnchor.position = centre;
            nominalAnchorOffset = transform.InverseTransformPoint(centre);
            carryAnchorFitted = true;
        }

        public void ShowWaitingPhoenix(float entryZ)
        {
            if (flying || waitingAtEntry || player == null || ballAnchor == null || visual == null)
                return;
            channelCentreX = player.Corridor.CentreX;
            anchorOffset = nominalAnchorOffset;
            Vector3 offset = phoenixBody.rotation * anchorOffset;
            Vector3 position = new Vector3(channelCentreX - offset.x, flightHeight,
                entryZ + waitingPhoenixDistance - offset.z);
            flightStartPosition = position;
            approachStartZ = player.transform.position.z;
            approachTakeoffZ = entryZ - boardingTakeoffDistance;
            approachProgress = 0f;
            phase = RoutePhase.Approaching;
            phoenixBody.interpolation = RigidbodyInterpolation.None;
            SetEntranceRoot(player.transform.position + approachOffset -
                (ballAnchor.position - transform.position));
            hull.enabled = false;
            flightVisual?.SetSteering(0f);
            flightVisual?.SetEntrancePitch(approachPitch * .75f);
            visual.SetActive(true);
            waitingAtEntry = true;
            spawnAudio?.Play();
            retirePhoenixAtZ = float.PositiveInfinity;
        }

        public void HideWaitingPhoenix()
        {
            if (flying) return;
            waitingAtEntry = false;
            ResetEntrance();
            if (visual != null) visual.SetActive(false);
        }

        private bool CapturePlayer(bool bridgeEntry)
        {
            if (!GameSequenceCoordinator.IsEventUnlocked(EndlessRollball.Scenery.EnvironmentTheme.Dungeon))
                return false;
            if (flying || player == null || runController == null || ballAnchor == null ||
                visual == null || course == null || !course.Prepare()) return false;
            sphereBody = player.GetComponent<Rigidbody>();
            if (sphereBody == null || !runController.TryBeginSpecialEvent(
                keepPhysicsRunning: true, allowRespawnFlashing: bridgeEntry)) return false;

            entryPosition = sphereBody.position;
            player.SetInputContext(EndlessRollball.Imu.ImuControlContext.AvatarRotation);
            entryRotation = sphereBody.rotation;
            savedMovementEnabled = player.enabled;
            savedGravity = sphereBody.useGravity;
            savedKinematic = sphereBody.isKinematic;
            savedDetectCollisions = sphereBody.detectCollisions;
            savedConstraints = sphereBody.constraints;
            savedInterpolation = sphereBody.interpolation;
            channelCentreX = player.Corridor.CentreX;
            anchorOffset = nominalAnchorOffset;
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
            if (!waitingAtEntry) spawnAudio?.Play();
            waitingAtEntry = false;
            visual.SetActive(true);
            return true;
        }

        [ContextMenu("Begin Flying")]
        public void BeginFlying()
        {
            if (!Application.isPlaying) return;
            FitCarryAnchor();
            if (!CapturePlayer(false)) return;
            ResetEntrance();
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
            // The preview is behind the player; route geometry still starts at the
            // original waiting position, independently of the swoop's current pose.
            float distance = stopZ - (flightStartPosition.z + offset.z);
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
            phase = RoutePhase.Intercepting;
            jumpElapsed = 0f;
            jumpStart = sphereBody.position;
            jumpEnd = flightStartPosition + offset;
            interceptionStart = ballAnchor.position;
            sphereBody.interpolation = RigidbodyInterpolation.None;
            phoenixBody.interpolation = RigidbodyInterpolation.None;
            if (!runController.Generator.CommitPhoenixLava())
            {
                sphereBody.position = entryPosition;
                player.transform.position = entryPosition;
                FinishRoute();
                if (visual != null) visual.SetActive(false);
                return false;
            }
            return true;
        }

        private void StartCourse()
        {
            hull.enabled = true;
            course.Show(phoenixBody.position, channelCentreX, forwardSpeed,
                channelWidth, flightHeight, transform, runController.Generator.LavaSurfaceHeight,
                runController.Generator.CurrentSeed,runController.TrackDifficulty,
                runController.Generator.EncounterProgress01,steeringSpeed,steeringAcceleration,hull.bounds.extents);

        }

        private void LateUpdate()
        {
            // PhoenixFlightVisual runs first, so all alignment uses the actual feet
            // after their wing-driven bob and entrance pitch have been applied.
            if (waitingAtEntry && runController != null && player != null &&
                runController.State == SessionFlowState.Running && Time.timeScale > 0f)
            {
                Vector3 feet = player.transform.position + Vector3.Lerp(approachOffset,
                    interceptionOffset, Mathf.SmoothStep(0f, 1f, approachProgress));
                SetEntranceRoot(feet - (ballAnchor.position - transform.position));
                return;
            }
            if (!flying || sphereBody == null || ballAnchor == null ||
                runController == null ||
                runController.State != SessionFlowState.SpecialEvent || Time.timeScale <= 0f)
                return;

            if (phase == RoutePhase.Intercepting || phase == RoutePhase.CarryingToStart)
            {
                TickInterception();
                return;
            }

            bool carrying = phase == RoutePhase.WaitingForFlight ||
                            phase == RoutePhase.Flying || phase == RoutePhase.WaitingForExit;
            if (!carrying) return;

            // Run after PhoenixFlightVisual so the sphere follows the animated
            // feet, including their bob and bank, without interpolation lag.
            sphereBody.interpolation = RigidbodyInterpolation.None;
            sphereBody.position = ballAnchor.position;
        }

        private Vector3 BoardingPosition(float t)
        {
            Vector3 position = Vector3.Lerp(jumpStart, jumpEnd, t);
            position.y += PhoenixLavaMath.JumpLift(t, handoffJumpHeight);
            return position;
        }

        private void SetEntranceRoot(Vector3 position)
        {
            phoenixBody.position = position;
            transform.position = position;
        }

        private void TickInterception()
        {
            player.DiscardBufferedInput();
            float t = Mathf.Clamp01(jumpElapsed / handoffJumpSeconds);
            float contact = Mathf.Clamp(catchFraction, .2f, .8f);
            Vector3 animatedOffset = ballAnchor.position - transform.position;
            Vector3 ballPosition = BoardingPosition(t);
            if (t < contact)
            {
                // Match the ball's velocity at contact rather than easing the bird
                // to a stop immediately before it takes over carrying the ball.
                Vector3 catchPosition = BoardingPosition(contact);
                Vector3 catchTangent = jumpEnd - jumpStart + Vector3.up *
                    (4f * handoffJumpHeight * (1f - 2f * contact));
                Vector3 firstControl = interceptionStart + Vector3.forward *
                    (Mathf.Max(.1f, player.ForwardSpeed) * handoffJumpSeconds * contact / 3f);
                Vector3 lastControl = catchPosition - catchTangent * (contact / 3f);
                float u = t / contact;
                float v = 1f - u;
                Vector3 feet = v * v * v * interceptionStart +
                    3f * v * v * u * firstControl + 3f * v * u * u * lastControl +
                    u * u * u * catchPosition;
                SetEntranceRoot(feet - animatedOffset);
                sphereBody.position = ballPosition;
                player.transform.position = ballPosition;
                return;
            }

            phase = RoutePhase.CarryingToStart;
            // At contact this is exactly the existing jump arc. Gradually include
            // the animated offset so the root ends exactly at its authored start.
            float carry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(contact, 1f, t));
            Vector3 nominalOffset = phoenixBody.rotation * nominalAnchorOffset;
            Vector3 feetTarget = ballPosition + (animatedOffset - nominalOffset) * carry;
            SetEntranceRoot(feetTarget - animatedOffset);
            sphereBody.position = ballAnchor.position;
            player.transform.position = sphereBody.position;
            if (t < 1f) return;
            SetEntranceRoot(flightStartPosition);
            sphereBody.position = ballAnchor.position;
            player.transform.position = sphereBody.position;
            flightVisual?.SetEntrancePitch(0f);
            phoenixBody.interpolation = RigidbodyInterpolation.Interpolate;
            phase = RoutePhase.WaitingForFlight;
        }

        private void ResetEntrance()
        {
            approachProgress = 0f;
            jumpElapsed = 0f;
            phase = RoutePhase.None;
            flightVisual?.SetEntrancePitch(0f);
            if (phoenixBody != null) phoenixBody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void FixedUpdate()
        {
            if (!flying || runController.State != SessionFlowState.SpecialEvent) return;
            if (phase == RoutePhase.Intercepting || phase == RoutePhase.CarryingToStart)
            {
                player.DiscardBufferedInput();
                return;
            }
            if (phase == RoutePhase.ExitJump)
            {
                player.DiscardBufferedInput();
                jumpElapsed += Time.fixedDeltaTime;
                float t = Mathf.Clamp01(jumpElapsed / handoffJumpSeconds);
                Vector3 position = Vector3.Lerp(jumpStart, jumpEnd, t);
                position.y += PhoenixLavaMath.JumpLift(t, handoffJumpHeight);
                sphereBody.MovePosition(position);
                if (t >= 1f)
                {
                    sphereBody.position = jumpEnd;
                    player.transform.position = jumpEnd;
                    FinishRoute();
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
            flightVisual?.SetEntrancePitch(0f);
            phoenixBody.interpolation = RigidbodyInterpolation.Interpolate;
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
            player.SetInputContext(EndlessRollball.Imu.ImuControlContext.Road);
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
            ResetEntrance();
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
            impactAudio?.PlayDungeonBurn(other);
            flightHits++;
            runController.Tasks.RecordEventNavigation(obstacle.GetInstanceID(),false);
            if (flightVisual != null) flightVisual.React();
            EndlessRollball.Infrastructure.RuntimeLog.Info($"[PhoenixHit] {obstacle.name}; total hits: {flightHits}", this);
        }

        private void OnDisable()
        {
            ResetEntrance();
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
