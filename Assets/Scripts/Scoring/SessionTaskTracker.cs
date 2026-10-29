using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class SessionTaskTracker : MonoBehaviour
    {
        private enum TaskKind
        {
            Navigation,
            JumpLanding
        }

        private enum TaskState
        {
            Upcoming,
            Encountered,
            Succeeded,
            Failed,
            Cancelled
        }

        private sealed class Opportunity
        {
            public TrackSegment Track;
            public int Index;
            public int LocalIndex;
            public long EncounterId;
            public float RecoveryEndZ;
            public string Identity;
            public TaskKind Kind;
            public TaskState State;
            public float EntryZ, HazardStartZ, LandingStartZ, DeadlineZ;
            public bool AcceptedJump;
            public bool ObservedAirborne;
            public Collider[] Hazards = Array.Empty<Collider>();
        }

        [SerializeField]
        private BallMovement ballMovement;
        [SerializeField]
        private GameplayRunController runController;
        [Header("Scoring Diagnostics")]
        [SerializeField, Tooltip("Log failed task identities and jump evidence to the Console. Does not change scoring.")]
        private bool logFailedTasks;
        private readonly List<Opportunity> opportunities = new List<Opportunity>();
        private readonly Dictionary<Collider, TrackSegment> floors = new Dictionary<Collider, TrackSegment>();
        private readonly Dictionary<TrackSegment, List<Collider>> trackFloors = new Dictionary<TrackSegment, List<Collider>>();
        private int navigationRequired, navigationSucceeded, jumpsRequired, jumpsSucceeded;
        private bool sessionActive, scoringSuspended;
        private string sessionId;
        private BallMovement subscribedBall;
        private int highestRegisteredIndex = -1;
        internal bool HasRegistrationErrors { get; private set; }

        public event Action ScoreChanged;
        public SessionScore CurrentScore { get; private set; } = new SessionScore(0, 0, 0, 0);

        public bool IsConfiguredFor(GameplayRunController controller, BallMovement ball) => isActiveAndEnabled && runController == controller && ballMovement == ball && ball != null && ball.GetComponent<SphereCollider>() != null;
        private float Radius
        {
            get
            {
                SphereCollider sphere = ballMovement.GetComponent<SphereCollider>();
                Vector3 s = sphere.transform.lossyScale;
                return sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }
        }

        private float PlayerZ => ballMovement.transform.position.z;
        private bool CanScore => sessionActive && !scoringSuspended && runController != null && runController.State == SessionFlowState.Running;

        // Called by the existing generator before registering the initial spawn-ahead tracks.
        internal void ResetRoute()
        {
            Unsubscribe();
            sessionActive = false;
            opportunities.Clear();
            floors.Clear();
            trackFloors.Clear();
            navigationRequired = navigationSucceeded = jumpsRequired = jumpsSucceeded = 0;
            highestRegisteredIndex = -1;
            HasRegistrationErrors = false;
            Publish();
        }

        public void BeginSession(string id)
        {
            Unsubscribe();
            sessionId = id;
            navigationRequired = navigationSucceeded = jumpsRequired = jumpsSucceeded = 0;
            scoringSuspended = false;
            sessionActive = true;
            foreach (Opportunity task in opportunities)
            {
                task.State = TaskState.Upcoming;
                task.AcceptedJump = false;
                task.ObservedAirborne = false;
                task.Identity = $"{sessionId}:{task.EncounterId}:{task.LocalIndex}:{task.Kind}";
            }

            subscribedBall = ballMovement;
            subscribedBall.JumpAccepted += HandleAcceptedJump;
            subscribedBall.GroundContact += HandleGroundContact;
            subscribedBall.AirborneObserved += HandleAirborne;
            Publish();
        }

        internal void RegisterTrack(TrackSegment track)
        {
            if (trackFloors.ContainsKey(track))
                return;
            var owned = new List<Collider>();
            foreach (BoxCollider box in track.GetComponentsInChildren<BoxCollider>(true))
            {
                // These are the authored support objects, never containment walls or scenery.
                if (!box.isTrigger && box.enabled && (box.name == "FloorCollider" || box.name == "Floor_Takeoff" || box.name == "Floor_Landing"))
                {
                    owned.Add(box);
                    floors[box] = track;
                }
            }

            trackFloors.Add(track, owned);
        }

        internal void RegisterPayload(TrackSegment track, int index, GameObject payload, bool requiredJump, SideToSideObstacleMotion motion, int localIndex = 0, Collider[] cachedColliders = null, long encounterId = 0, ObstacleManager.TaskWindow window = null, bool usePlannedMotionWindow = false)
        {
            Collider[] hazards = cachedColliders ?? payload.GetComponentsInChildren<Collider>(true);
            Bounds envelope = default;
            bool measured = false;
            foreach (Collider collider in hazards)
            {
                PalmObstacle palm = collider.GetComponentInParent<PalmObstacle>();
                if (!(collider is BoxCollider box) || !box.enabled ||
                    (box.isTrigger && (palm == null || !palm.IsLeafSensor(box)) && box.GetComponentInParent<DungeonTrapEncounter>() == null))
                    continue;
                EncapsulateBox(box, payload.transform, ref envelope, ref measured);
            }

            if (!measured)
            {
                RegistrationError(track, "No box hazard geometry.");
                return;
            }

            if (motion != null)
            {
                if (motion.transform.parent != payload.transform)
                {
                    RegistrationError(track, "Mover must be a direct child of the obstacle root.");
                    return;
                }

                Bounds travel = motion.AuthoredTravelBounds;
                Vector3 current = motion.transform.localPosition;
                Vector3 min = envelope.min + travel.min - current;
                Vector3 max = envelope.max + travel.max - current;
                envelope.SetMinMax(min, max);
            }

            float hazardStart = payload.transform.TransformPoint(envelope.min).z;
            if (usePlannedMotionWindow && window != null)
                hazardStart = window.HazardStartZ;
            else if (window != null)
                hazardStart = Mathf.Min(hazardStart, window.HazardStartZ);

            float hazardEnd = payload.transform.TransformPoint(envelope.max).z;
            if (usePlannedMotionWindow && window != null)
                hazardEnd = window.LandingStartZ - Radius - .10f;
            AddOpportunity(ApplyWindow(new Opportunity { Track = track, Index = index, LocalIndex = localIndex, Hazards = hazards, Kind = requiredJump ? TaskKind.JumpLanding : TaskKind.Navigation, EntryZ = hazardStart - 12f, HazardStartZ = hazardStart, LandingStartZ = hazardEnd + Radius + .10f, DeadlineZ = track.ExitAnchor.position.z - Radius - .10f }, encounterId, window));
        }

        internal void UpdateMovingJumpWindow(TrackSegment track, float start, float end)
        {
            foreach(var task in opportunities)
                if(task.Track==track && task.Kind==TaskKind.JumpLanding &&
                    (task.State==TaskState.Upcoming || task.State==TaskState.Encountered))
                { task.HazardStartZ=start; task.LandingStartZ=end+Radius+.15f; task.EntryZ=start-12f; }
        }

        internal void RegisterGap(TrackSegment track, int index, int localIndex = 0, long encounterId = 0, ObstacleManager.TaskWindow window = null)
        {
            BoxCollider takeoff = null, landing = null;
            foreach (BoxCollider box in track.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.name == "Floor_Takeoff")
                    takeoff = box;
                else if (box.name == "Floor_Landing")
                    landing = box;
            }

            if (takeoff == null || landing == null)
            {
                RegistrationError(track, "Gap needs its authored takeoff/landing floor boxes.");
                return;
            }

            Vector3 takeoffLip = takeoff.transform.TransformPoint(takeoff.center + new Vector3(0f, takeoff.size.y * .5f, takeoff.size.z * .5f));
            Vector3 landingLip = landing.transform.TransformPoint(landing.center + new Vector3(0f, landing.size.y * .5f, -landing.size.z * .5f));
            AddOpportunity(ApplyWindow(new Opportunity { Track = track, Index = index, LocalIndex = localIndex, Kind = TaskKind.JumpLanding, EntryZ = takeoffLip.z - 12f, HazardStartZ = takeoffLip.z, LandingStartZ = landingLip.z + Radius + .10f, DeadlineZ = track.ExitAnchor.position.z - Radius - .10f }, encounterId, window));
        }

        private Opportunity ApplyWindow(Opportunity task, long encounterId, ObstacleManager.TaskWindow window)
        {
            task.EncounterId = window == null ? task.Index : encounterId;
            task.RecoveryEndZ = window == null ? task.LandingStartZ + 1f : window.RecoveryEndZ;
            if (window != null)
            {
                task.EntryZ = window.EntryZ;
                // Keep the measured hazard start: a combination can have an earlier blocker.
                // The planner window reserves space; it is not the jump takeoff boundary.
                task.LandingStartZ = Mathf.Max(task.LandingStartZ, window.LandingStartZ);
                task.DeadlineZ = window.DeadlineZ;
            }

            return task;
        }

        private void AddOpportunity(Opportunity task)
        {
            Opportunity existing = opportunities.Find(item => item.EncounterId == task.EncounterId && item.LocalIndex == task.LocalIndex);
            if (existing != null && existing.Kind == task.Kind)
            {
                existing.EntryZ = Mathf.Min(existing.EntryZ, task.EntryZ);
                existing.HazardStartZ = Mathf.Min(existing.HazardStartZ, task.HazardStartZ);
                existing.LandingStartZ = Mathf.Max(existing.LandingStartZ, task.LandingStartZ);
                var combined = new List<Collider>(existing.Hazards);
                foreach (Collider hazard in task.Hazards)
                    if (!combined.Contains(hazard))
                        combined.Add(hazard);
                existing.Hazards = combined.ToArray();
                return;
            }

            if (existing != null || task.Index < highestRegisteredIndex || task.LandingStartZ >= task.DeadlineZ)
            {
                RegistrationError(task.Track, "Duplicate task or no safe completion/landing space.");
                return;
            }

            highestRegisteredIndex = task.Index;
            task.Identity = $"{sessionId}:{task.EncounterId}:{task.LocalIndex}:{task.Kind}";
            opportunities.Add(task);
        }

        private void LateUpdate()
        {
            if (sessionActive && scoringSuspended && runController.State == SessionFlowState.Running)
            {
                bool changed = false;
                foreach (Opportunity task in opportunities)
                    if (task.State == TaskState.Upcoming && PlayerZ >= task.EntryZ)
                    {
                        Cancel(task);
                        changed = true;
                    }

                if (changed)
                    Publish();
                return;
            }

            ObservePlayer();
        }

        public void ObservePlayer()
        {
            if (!CanScore)
                return;
            foreach (Opportunity task in opportunities)
            {
                Enter(task);
                if (task.State != TaskState.Encountered)
                    continue;
                // A planning estimate must not expire a valid flight. A supported landing,
                // collision, fall or eventual track release supplies the actual outcome.
                if (ballMovement.IsGrounded)
                    ObserveSupportedPass(task, ballMovement.GroundCollider);
            }
        }

        private void HandleAirborne()
        {
            if (!CanScore)
                return;
            foreach (Opportunity task in opportunities)
                if (task.State == TaskState.Encountered && task.AcceptedJump)
                    task.ObservedAirborne = true;
        }

        private void HandleAcceptedJump(Collider support)
        {
            if (!CanScore || support == null || !floors.ContainsKey(support))
                return;
            foreach (Opportunity task in opportunities)
            {
                Enter(task);
                // Arm every upcoming hazard this flight could cross, including close stages.
                // Landing before a hazard clears its evidence again. The sphere can still
                // jump from a gap lip while its centre is slightly beyond the takeoff edge.
                if (task.State == TaskState.Encountered && task.Kind == TaskKind.JumpLanding &&
                    PlayerZ <= task.HazardStartZ + Radius && InsideRoute(task))
                {
                    task.AcceptedJump = true;
                    task.ObservedAirborne = false;
                }
            }
        }

        private void HandleGroundContact(Collider support)
        {
            if (!CanScore || support == null || !floors.ContainsKey(support))
                return;
            foreach (Opportunity task in opportunities)
            {
                Enter(task);
                if (task.State != TaskState.Encountered)
                    continue;
                ObserveSupportedPass(task, support);
            }
        }

        private void ObserveSupportedPass(Opportunity task, Collider support)
        {
            if (!IsRouteFloor(support) || !InsideRoute(task))
                return;
            // An earlier jump that landed before clearing this hazard is not its evidence.
            if (task.Kind == TaskKind.JumpLanding && task.AcceptedJump && task.ObservedAirborne &&
                PlayerZ + 2f * Radius + .20f < task.LandingStartZ)
                task.AcceptedJump = task.ObservedAirborne = false;

            if (PlayerZ < task.LandingStartZ)
                return;
            // Accept any registered route support beyond the hazard, including the next
            // segment after a long gap jump. Planning deadlines do not invalidate landings.
            Resolve(task, task.Kind == TaskKind.Navigation ||
                (task.AcceptedJump && task.ObservedAirborne), "Supported pass without accepted jump/airborne evidence");
        }

        private bool InsideRoute(Opportunity task)
        {
            return ballMovement.Corridor.Contains(ballMovement.transform.position.x, Radius);
        }

        private bool IsRouteFloor(Collider support) => support != null && support.enabled &&
            support.gameObject.activeInHierarchy && floors.TryGetValue(support, out TrackSegment owner) && owner != null;

        private void Enter(Opportunity task)
        {
            if (task.State != TaskState.Upcoming || PlayerZ < task.EntryZ)
                return;
            task.State = TaskState.Encountered;
            if (task.Kind == TaskKind.Navigation)
                navigationRequired++;
            else
                jumpsRequired++;
            Publish();
        }

        private void Resolve(Opportunity task, bool succeeded, string reason = "Unresolved task")
        {
            if (task.State != TaskState.Encountered)
                return;
            task.State = succeeded ? TaskState.Succeeded : TaskState.Failed;
            if (!succeeded && logFailedTasks)
                Debug.Log($"Scoring failure {task.Identity} ({task.Kind}): {reason}; " +
                    $"player Z={PlayerZ:F2}, hazard Z={task.HazardStartZ:F2}, clearance Z={task.LandingStartZ:F2}, " +
                    $"jump accepted={task.AcceptedJump}, airborne observed={task.ObservedAirborne}.", this);
            if (succeeded)
            {
                if (task.Kind == TaskKind.Navigation)
                    navigationSucceeded++;
                else
                    jumpsSucceeded++;
            }

            Publish();
        }

        private void Cancel(Opportunity task)
        {
            if (task.State == TaskState.Encountered)
            {
                if (task.Kind == TaskKind.Navigation)
                    navigationRequired--;
                else
                    jumpsRequired--;
            }

            if (task.State == TaskState.Upcoming || task.State == TaskState.Encountered)
                task.State = TaskState.Cancelled;
        }

        // Explicit hook for future special-avatar/BLE scoring windows. Pausing alone does not cancel.
        public void SetScoringSuspended(bool value)
        {
            if (value == scoringSuspended)
                return;
            scoringSuspended = value;
            if (value)
                foreach (Opportunity task in opportunities)
                    if (task.State == TaskState.Encountered)
                        Cancel(task);
            Publish();
        }

        public SessionScore FinalizeSession(SessionOutcome outcome, Collider relatedHazard)
        {
            if (!sessionActive)
                return CurrentScore;
            RecordFailure(outcome, relatedHazard);
            foreach (Opportunity task in opportunities)
                Cancel(task);
            sessionActive = false;
            Unsubscribe();
            Publish();
            Debug.Log($"Scored session {sessionId}: navigation {navigationSucceeded}/{navigationRequired}, " + $"jump/landing {jumpsSucceeded}/{jumpsRequired}; {SessionScore.RulesId}", this);
            return CurrentScore;
        }

        // Finite-mode recovery retains the same session, failed denominator and future task records.
        internal float RecordFailure(SessionOutcome outcome, Collider relatedHazard)
        {
            Opportunity failed = null;
            if (outcome == SessionOutcome.Collision && relatedHazard != null)
                foreach (Opportunity task in opportunities)
                    foreach (Collider hazard in task.Hazards)
                        if (hazard == relatedHazard)
                            failed = task;
            if (outcome == SessionOutcome.Fall)
                foreach (Opportunity task in opportunities)
                    // A fast fall may pass the exit/deadline before reaching the fall threshold.
                    if (task.State != TaskState.Cancelled && task.State != TaskState.Succeeded && PlayerZ + Radius >= task.HazardStartZ && PlayerZ <= task.RecoveryEndZ + TrackSegment.ContractLength && (failed == null || task.HazardStartZ > failed.HazardStartZ))
                        failed = task;
            bool separateCombinedCollision =
                outcome == SessionOutcome.Collision &&
                relatedHazard != null &&
                relatedHazard.GetComponentInParent<CombinedObstacleEncounter>() != null;

            if (sessionActive && !scoringSuspended && failed != null)
            {
                foreach (Opportunity task in opportunities)
                {
                    if (task.EncounterId != failed.EncounterId)
                        continue;

                    if (separateCombinedCollision &&
                        !ReferenceEquals(task, failed))
                    {
                        // Recovery skips the unfinished companion challenge.
                        // Preserve any task that already succeeded or failed.
                        Cancel(task);
                        continue;
                    }

                    Enter(task);

                    if (task.State == TaskState.Encountered)
                        Resolve(task, false, outcome.ToString());
                }

                Publish();
            }

            float recoveryZ = PlayerZ + .5f;
            if (failed != null)
                foreach (Opportunity task in opportunities)
                    if (task.EncounterId == failed.EncounterId)
                        recoveryZ = Mathf.Max(recoveryZ, task.RecoveryEndZ + 1f);
            return recoveryZ;
        }

        internal bool TryGetRecoveryPoint(float minimumZ, float finishZ, out Vector3 position)
        {
            position = default;
            float bestZ = float.PositiveInfinity;
            float radius = Radius;
            foreach (var entry in trackFloors)
            {
                TrackSegment track = entry.Key;
                if (track == null || !track.gameObject.activeInHierarchy)
                    continue;
                foreach (Collider collider in entry.Value)
                {
                    if (!(collider is BoxCollider floor) || !floor.enabled || !floor.gameObject.activeInHierarchy || floor.name == "Floor_Takeoff")
                        continue;
                    float z = Mathf.Max(minimumZ, floor.bounds.min.z + radius + .25f);
                    // Never place inside a moving hazard's sweep or another required jump.
                    foreach (Opportunity task in opportunities)
                        if (z + radius >= task.HazardStartZ - radius && z <= task.RecoveryEndZ + radius)
                            z = Mathf.Max(z, task.RecoveryEndZ + 1f);
                    if (z >= bestZ || z + radius + .25f > floor.bounds.max.z || z + radius + .5f >= finishZ)
                        continue;
                    float top = float.NegativeInfinity;
                    bool supported = true;
                    // Check the entire footprint on this one authored support, including sloped landings.
                    for (int sample = 0; sample < 5; sample++)
                    {
                        float x = sample == 1 ? -radius : sample == 2 ? radius : 0f;
                        float sampleZ = z + (sample == 3 ? -radius : sample == 4 ? radius : 0f);
                        var ray = new Ray(new Vector3(x, floor.bounds.max.y + 10f, sampleZ), Vector3.down);
                        if (!floor.Raycast(ray, out RaycastHit hit, floor.bounds.size.y + 20f) || hit.normal.y < .55f)
                        {
                            supported = false;
                            break;
                        }

                        top = Mathf.Max(top, hit.point.y);
                    }

                    if (!supported)
                        continue;
                    bestZ = z;
                    position = new Vector3(0f, top + radius + .08f, z);
                }
            }

            return !float.IsPositiveInfinity(bestZ);
        }

        internal void PrepareRecoveryArrival(float destinationZ)
        {
            foreach (Opportunity task in opportunities)
            {
                // Teleporting past a different task is an exclusion, never free success/failure evidence.
                if (task.HazardStartZ <= destinationZ)
                    Cancel(task);
                task.AcceptedJump = task.ObservedAirborne = false;
            }

            Publish();
        }

        internal void ReleaseTrack(TrackSegment track)
        {
            for (int i = opportunities.Count - 1; i >= 0; i--)
            {
                Opportunity task = opportunities[i];
                if (task.Track != track)
                    continue;
                if (CanScore)
                {
                    Enter(task);
                    if (task.State == TaskState.Encountered)
                        Resolve(task, false, "Track recycled before a supported completion");
                }
                else
                    Cancel(task);
                opportunities.RemoveAt(i);
            }

            if (trackFloors.TryGetValue(track, out List<Collider> owned))
            {
                foreach (Collider floor in owned)
                    floors.Remove(floor);
                trackFloors.Remove(track);
            }
        }

        private void Publish()
        {
            CurrentScore = new SessionScore(navigationRequired, navigationSucceeded, jumpsRequired, jumpsSucceeded);
            ScoreChanged?.Invoke();
        }

        private void Unsubscribe()
        {
            if (subscribedBall == null)
                return;
            subscribedBall.JumpAccepted -= HandleAcceptedJump;
            subscribedBall.GroundContact -= HandleGroundContact;
            subscribedBall.AirborneObserved -= HandleAirborne;
            subscribedBall = null;
        }

        private void OnDisable()
        {
            sessionActive = false;
            Unsubscribe();
        }

        private void RegistrationError(TrackSegment track, string message)
        {
            HasRegistrationErrors = true;
            Debug.LogError($"Scoring task not registered for {track.name}: {message}", this);
            if (runController != null && runController.State == SessionFlowState.Running)
                runController.ReportScoringSetupError(message);
        }

        private static void EncapsulateBox(BoxCollider box, Transform root, ref Bounds combined, ref bool initialized)
        {
            var local = new Bounds(box.center, box.size);
            Matrix4x4 matrix = root.worldToLocalMatrix * box.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = matrix.MultiplyPoint3x4(corner);
                if (initialized)
                    combined.Encapsulate(point);
                else
                {
                    combined = new Bounds(point, Vector3.zero);
                    initialized = true;
                }
            }
        }

        internal void RecordNavigationContact(Collider hazard)
        {
            if (!CanScore || hazard == null)
                return;

            foreach (Opportunity task in opportunities)
            {
                if (task.Kind != TaskKind.Navigation ||
                    (task.State != TaskState.Upcoming &&
                     task.State != TaskState.Encountered) ||
                    Array.IndexOf(task.Hazards, hazard) < 0)
                {
                    continue;
                }

                // Actual contact can occur before the planned approach window
                // if a dynamic box has moved.
                task.EntryZ = Mathf.Min(task.EntryZ, PlayerZ);

                Enter(task);
                Resolve(task, false, hazard.GetComponentInParent<PalmObstacle>() != null ? "Palm leaf contact" : "Cargo contact");

                return;
            }
        }
    }
}
