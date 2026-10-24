using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EndlessRollball.Track
{
    public sealed class GameplayRunController : MonoBehaviour
    {
        [SerializeField] private BallMovement ballMovement;
        [SerializeField] private TrackGenerator trackGenerator;
        [SerializeField]
        private Vector3 spawnPosition =
            new Vector3(0f, 0.75f, 0f);
        [SerializeField] private float fallThreshold = -5f;

        [Header("Finite finish gate - Stage 6C")]
        [SerializeField] private GameObject finishGatePrefab;

        public bool IsCompleted { get; private set; }
        public event Action Completed;

        private TrackGenerator subscribedGenerator;
        private GameObject finishGateInstance;
        private FinishGateTrigger finishGateTrigger;
        private bool runReady;
        private bool planHandled;
        private bool hasStarted;

        private void OnEnable()
        {
            SubscribeToGenerator();

            // Re-enabling an unfinished run may resume its current plan.
            // A completed run stays completed until an explicit restart.
            if (hasStarted && !IsCompleted &&
                trackGenerator != null && trackGenerator.IsRunInitialized)
                HandleRunPrepared();
        }

        private void Start()
        {
            hasStarted = true;
            if (ballMovement == null || trackGenerator == null)
            {
                FailSetup("Assign BallMovement and TrackGenerator.");
                return;
            }

            // All OnEnable subscriptions exist before scene Start calls,
            // but this also handles a generator that was already ready.
            if (!planHandled && trackGenerator.IsRunInitialized)
                HandleRunPrepared();
        }

        private void Update()
        {
            if (ballMovement == null)
                return;

            bool restartPressed =
                Keyboard.current?.rKey.wasPressedThisFrame ?? false;
            if (restartPressed)
            {
                RestartRun();
                return;
            }

            if (runReady && !IsCompleted &&
                ballMovement.transform.position.y < fallThreshold)
                RestartRun();
        }

        public void RestartRun()
        {
            if (ballMovement == null || trackGenerator == null)
            {
                FailSetup("Cannot restart without ball and generator.");
                return;
            }

            // Must precede route planning: Stage 6B captures start Z.
            runReady = false;
            ballMovement.ResetMotion(spawnPosition, Quaternion.identity);
            trackGenerator.ResetRun();
        }

        private void HandleRunResetting()
        {
            runReady = false;
            planHandled = false;
            IsCompleted = false;
            HideFinishGate();
            if (ballMovement != null)
                ballMovement.StopMotionForCompletion();
        }

        private void HandleRunPrepared()
        {
            if (!isActiveAndEnabled || trackGenerator == null ||
                !trackGenerator.IsRunInitialized)
                return;

            planHandled = true;
            runReady = false;
            IsCompleted = false;
            HideFinishGate();

            if (ballMovement == null)
            {
                FailSetup("Assign BallMovement.");
                return;
            }

            if (trackGenerator.HasFiniteRoute && !PrepareFinishGate())
            {
                FailSetup("Finite run has no correctly configured finish gate.");
                return;
            }

            // Release the temporary stop used while rebuilding the route.
            // RestartRun already placed the player at spawn beforehand.
            // Do not teleport again using a potentially stale physics pose.
            // This also preserves current-position resumption on re-enable.
            ballMovement.ResumeMotion();
            trackGenerator.SetGenerationPaused(false);
            runReady = true;
        }

        private bool PrepareFinishGate()
        {
            if (finishGatePrefab == null)
                return false;

            if (finishGateInstance == null)
            {
                finishGateInstance = Instantiate(finishGatePrefab);
                finishGateInstance.name = "FinishGate_Runtime";
                finishGateInstance.SetActive(false);

                FinishGateTrigger[] triggers =
                    finishGateInstance.GetComponentsInChildren
                        <FinishGateTrigger>(true);
                if (triggers.Length != 1)
                    return false;

                finishGateTrigger = triggers[0];
            }

            if (finishGateTrigger == null)
                return false;

            finishGateInstance.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, trackGenerator.FinishLineZ),
                Quaternion.identity);
            finishGateInstance.transform.localScale = Vector3.one;
            finishGateInstance.SetActive(true);

            return finishGateTrigger.Arm(ballMovement, CompleteRun);
        }

        private void CompleteRun()
        {
            if (!runReady || IsCompleted ||
                trackGenerator == null || !trackGenerator.HasFiniteRoute)
                return;

            // Commit the completed state before invoking any listeners.
            IsCompleted = true;
            runReady = false;
            if (finishGateTrigger != null)
                finishGateTrigger.Disarm();
            trackGenerator.SetGenerationPaused(true);
            ballMovement.StopMotionForCompletion();

            Debug.Log(
                $"Run completed: {trackGenerator.TrackIdentifier}, " +
                $"seed={trackGenerator.CurrentSeed}.", this);
            Completed?.Invoke();
        }

        private void HideFinishGate()
        {
            if (finishGateTrigger != null)
                finishGateTrigger.Disarm();
            if (finishGateInstance != null)
                finishGateInstance.SetActive(false);
        }

        private void FailSetup(string reason)
        {
            runReady = false;
            HideFinishGate();
            if (trackGenerator != null)
                trackGenerator.SetGenerationPaused(true);
            if (ballMovement != null)
                ballMovement.StopMotionForCompletion();
            Debug.LogError($"Gameplay run setup: {reason}", this);
        }

        private void SubscribeToGenerator()
        {
            UnsubscribeFromGenerator();
            subscribedGenerator = trackGenerator;
            if (subscribedGenerator == null)
                return;

            subscribedGenerator.RunResetting += HandleRunResetting;
            subscribedGenerator.RunPrepared += HandleRunPrepared;
        }

        private void UnsubscribeFromGenerator()
        {
            if (subscribedGenerator != null)
            {
                subscribedGenerator.RunResetting -= HandleRunResetting;
                subscribedGenerator.RunPrepared -= HandleRunPrepared;
            }
            subscribedGenerator = null;
        }

        private void OnDisable()
        {
            UnsubscribeFromGenerator();
            runReady = false;
            planHandled = false;
            HideFinishGate();
            if (trackGenerator != null)
                trackGenerator.SetGenerationPaused(true);
            if (ballMovement != null)
                ballMovement.StopMotionForCompletion();
        }

        private void OnDestroy()
        {
            if (finishGateInstance != null)
                Destroy(finishGateInstance);
        }

        public void ConfigureForScene(
            BallMovement configuredBallMovement,
            TrackGenerator configuredTrackGenerator,
            Vector3 configuredSpawnPosition,
            float configuredFallThreshold)
        {
            ballMovement = configuredBallMovement;
            trackGenerator = configuredTrackGenerator;
            spawnPosition = configuredSpawnPosition;
            fallThreshold = configuredFallThreshold;

            if (Application.isPlaying && isActiveAndEnabled)
                SubscribeToGenerator();
        }
    }
}
