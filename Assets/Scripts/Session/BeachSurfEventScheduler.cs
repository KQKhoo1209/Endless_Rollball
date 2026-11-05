using UnityEngine;
using EndlessRollball.Track;
using EndlessRollball.Scenery;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameplayRunController))]
    public sealed class BeachSurfEventScheduler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EnvironmentManager environmentManager;
        [SerializeField] private WhaleSurfController whale;

        [Header("Beach event timing")]
        [Min(0f)] public float earliestEntrySeconds = 75f;
        [Min(0f)] public float targetEntrySeconds = 82.5f;
        [Min(0f)] public float latestEntrySeconds = 90f;
        [Min(1f)] public float surfDurationSeconds = 45f;

        [Header("Runtime information")]
        [SerializeField] private float beachElapsedSeconds;
        [SerializeField] private bool eligible;
        [SerializeField] private bool entryWindowReached;

        private GameplayRunController run;
        private GameSequenceCoordinator sequence;
        private bool wasInBeach;
        private bool missedWindowReported;

        public float BeachElapsedSeconds => beachElapsedSeconds;
        public bool Eligible => eligible;

        [SerializeField] private bool routeReserved;
        [SerializeField] private float reservedEntryZ;
        [SerializeField] private float reservedExitZ;

        [SerializeField] private bool eventStarted;

        private float nextReservationAttemptAt;

        private void Awake()
        {
            run = GetComponent<GameplayRunController>(); sequence=GetComponent<GameSequenceCoordinator>();
        }

        private void Update()
        {
            if(sequence!=null && sequence.Eligible) {
                sequence.Window(out earliestEntrySeconds,out targetEntrySeconds,out latestEntrySeconds);
                surfDurationSeconds=sequence.Configuration.eventSeconds;
            }
            eligible = IsEligibleMode();

            if (run.State == SessionFlowState.Preparing)
            {
                beachElapsedSeconds = 0f;
                missedWindowReported = false;
                entryWindowReached = false;
                routeReserved = false;
                reservedEntryZ = 0f;
                reservedExitZ = 0f;
                nextReservationAttemptAt = 0f;
                wasInBeach = false;
                eventStarted = false;
                whale?.HideWaitingWhale();
                return;
            }

            if (environmentManager == null || whale == null)
                return;

            // Only advance during ordinary active gameplay.
            if (run.State != SessionFlowState.Running)
                return;

            bool inBeach =
                environmentManager.CurrentTheme ==
                EnvironmentTheme.Water;

            if (!inBeach)
            {
                if (wasInBeach)
                {
                    beachElapsedSeconds = 0f;
                    entryWindowReached = false;
                }

                wasInBeach = false;
                whale.HideWaitingWhale();
                return;
            }

            if (!wasInBeach)
            {
                beachElapsedSeconds = 0f;
                entryWindowReached = false;
                wasInBeach = true;
            }

            if (!eligible)
                return;

            beachElapsedSeconds = sequence!=null ? sequence.Elapsed : beachElapsedSeconds + Time.deltaTime;
            if(!eventStarted && !routeReserved && beachElapsedSeconds>latestEntrySeconds) {
                if(!missedWindowReported)Debug.LogWarning("[BeachSurf] Entry window missed. " + run.Generator.SurfReservationIssue,this);
                missedWindowReported=true; return;
            }
            TryReserveRoute();

            if (routeReserved && !eventStarted)
            {
                float playerZ = run.PlayerBall.transform.position.z;

                // The whale is already stationary in the water when it comes into view.
                if (playerZ >= reservedEntryZ - 45f)
                    whale.ShowWaitingWhale(reservedEntryZ);

                if (playerZ >= reservedEntryZ - whale.BoardingTakeoffDistance)
                {
                    if (whale.BeginBeachRoute(
                            reservedExitZ,
                            surfDurationSeconds))
                    {
                        eventStarted = true;
                        sequence?.Notify(GameSequenceCoordinator.SequencePhase.Boarding);

                        EndlessRollball.Infrastructure.RuntimeLog.Info(
                            $"[BeachSurf] Boarded at Beach time " +
                            $"{beachElapsedSeconds:F1}s.",
                            this);
                    }
                }
            }

            if (!entryWindowReached &&
                beachElapsedSeconds >= earliestEntrySeconds)
            {
                entryWindowReached = true;

                EndlessRollball.Infrastructure.RuntimeLog.Info(
                    "[BeachSurf] Beach entry window reached.",
                    this);
            }
        }

        private void TryReserveRoute()
        {
            if (routeReserved ||
                Time.time < nextReservationAttemptAt)
                return;

            nextReservationAttemptAt = Time.time + 1f;

            if (!whale.PrepareBeachRoute(surfDurationSeconds))
                return;

            BallMovement player = run.PlayerBall;

            if (player == null)
                return;

            float speed = Mathf.Max(.1f, player.ForwardSpeed);

            float secondsUntilEntry = Mathf.Max(
                0f, targetEntrySeconds - whale.BoardingSeconds - beachElapsedSeconds);

            float desiredEntryZ =
                player.transform.position.z +
                speed * secondsUntilEntry + whale.BoardingTakeoffDistance;

            if (!run.Generator.TryReserveBeachSurf(
                    desiredEntryZ,
                    speed,
                    surfDurationSeconds,
                    out reservedEntryZ,
                    out reservedExitZ))
                return;

            routeReserved = true;
            sequence?.Reservation(beachElapsedSeconds + (reservedEntryZ-whale.BoardingTakeoffDistance-player.transform.position.z)/speed+whale.BoardingSeconds);

            EndlessRollball.Infrastructure.RuntimeLog.Info(
                $"[BeachSurf] Reserved entry Z={reservedEntryZ:F1}, " +
                $"exit Z={reservedExitZ:F1}.",
                this);
        }

        private bool IsEligibleMode()
        {
            GameMode? mode = GameSessionSettings.SelectedMode;
            DifficultyMode difficulty = run.TrackDifficulty;

            return
                mode == GameMode.FastPaced ||
                mode == GameMode.Standard;
        }

        private void OnValidate()
        {
            earliestEntrySeconds =
                Mathf.Max(0f, earliestEntrySeconds);

            latestEntrySeconds = Mathf.Max(
                earliestEntrySeconds, latestEntrySeconds);

            targetEntrySeconds = Mathf.Clamp(
                targetEntrySeconds,
                earliestEntrySeconds,
                latestEntrySeconds);

            surfDurationSeconds =
                Mathf.Max(1f, surfDurationSeconds);
        }
    }
}
