using UnityEngine;
using EndlessRollball.Track;
using EndlessRollball.Scenery;

namespace EndlessRollball
{
    [RequireComponent(typeof(GameplayRunController))]
    public sealed class DungeonLavaEventScheduler : MonoBehaviour
    {
        [SerializeField] private EnvironmentManager environmentManager;
        [SerializeField] private PhoenixFlightController phoenix;
        [Min(0f)] public float earliestEntrySeconds = 75f;
        [Min(0f)] public float targetEntrySeconds = 82.5f;
        [Min(0f)] public float latestEntrySeconds = 90f;
        [Min(10f)] public float flightDurationSeconds = 45f;
        [Header("Runtime information")]
        [SerializeField] private float dungeonElapsedSeconds;
        [SerializeField] private bool routeReserved, eventStarted, skipped;
        [SerializeField] private float entryZ, exitZ;
        private GameplayRunController run;
        private GameSequenceCoordinator sequence;
        private float nextAttempt;
        private bool enteredDungeon;

        private void Awake() {run=GetComponent<GameplayRunController>();sequence=GetComponent<GameSequenceCoordinator>();}

        private void Update()
        {
            if(sequence!=null && sequence.Eligible) {
                sequence.Window(out earliestEntrySeconds,out targetEntrySeconds,out latestEntrySeconds);
                flightDurationSeconds=sequence.Configuration.eventSeconds;
            }
            if (run.State == SessionFlowState.Preparing)
            {
                dungeonElapsedSeconds = 0f;
                routeReserved = eventStarted = skipped = enteredDungeon = false;
                nextAttempt = 0f;
                phoenix?.HideWaitingPhoenix();
                return;
            }
            if (run.State != SessionFlowState.Running || eventStarted || skipped) return;
            if (sequence==null || !sequence.Eligible) return;
            if (environmentManager == null || phoenix == null)
            {
                Skip("Assign Environment Manager and Phoenix on the Dungeon Lava Event Scheduler.");
                return;
            }
            if (environmentManager.CurrentTheme != EnvironmentTheme.Dungeon) return;
            if (!enteredDungeon) { dungeonElapsedSeconds = 0f; enteredDungeon = true; }
            dungeonElapsedSeconds = sequence!=null ? sequence.Elapsed : dungeonElapsedSeconds + Time.deltaTime;
            if (dungeonElapsedSeconds > latestEntrySeconds)
            {
                Skip("No compatible entry reached within the window. Check the lava prefab, route length and entry timing.");
                return;
            }
            if (!routeReserved && Time.time >= nextAttempt)
            {
                nextAttempt = Time.time + 1f;
                if (!phoenix.PrepareLavaRoute(flightDurationSeconds))
                {
                    Skip("Phoenix rig or lava course is incomplete. Check its references and hazard prefabs.");
                    return;
                }
                routeReserved = run.Generator.TryReservePhoenixLava(dungeonElapsedSeconds,
                    run.PlayerBall.ForwardSpeed, Mathf.Max(0f, earliestEntrySeconds-phoenix.BoardingSeconds), targetEntrySeconds-phoenix.BoardingSeconds,
                    latestEntrySeconds-phoenix.BoardingSeconds, phoenix.BoardingTakeoffDistance, flightDurationSeconds,
                    out entryZ, out exitZ);
            }
            if (!routeReserved) return;
            float z = run.PlayerBall.transform.position.z;
            if (z >= entryZ - 45f) phoenix.ShowWaitingPhoenix(entryZ);
            if (z < entryZ - phoenix.BoardingTakeoffDistance) return;
            // An early arrival boards safely, then waits at the carry anchor.
            // Waiting and boarding use scaled time; neither consumes the 45 s course.
            float wait = PhoenixLavaMath.SecondsUntilFlight(dungeonElapsedSeconds, earliestEntrySeconds);
            if (phoenix.BeginLavaRoute(exitZ, flightDurationSeconds, wait))
            {
                eventStarted = true;
                        sequence?.Notify(GameSequenceCoordinator.SequencePhase.Boarding);
                EndlessRollball.Infrastructure.RuntimeLog.Info($"[PhoenixLava] Boarding at Dungeon time {dungeonElapsedSeconds:F1}s.", this);
            }
        }

        private void Skip(string reason)
        {
            if (skipped) return;
            skipped = true;
            run.Generator.CancelPhoenixReservation();
            phoenix?.HideWaitingPhoenix();
            Debug.LogWarning("[PhoenixLava] Event skipped. " + reason, this);
        }

        private void OnValidate()
        {
            earliestEntrySeconds = Mathf.Max(0f, earliestEntrySeconds);
            latestEntrySeconds = Mathf.Max(earliestEntrySeconds, latestEntrySeconds);
            targetEntrySeconds = Mathf.Clamp(targetEntrySeconds, earliestEntrySeconds, latestEntrySeconds);
            flightDurationSeconds = Mathf.Max(10f, flightDurationSeconds);
        }
    }
}
