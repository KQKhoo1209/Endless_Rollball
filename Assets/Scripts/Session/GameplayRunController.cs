using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EndlessRollball.Track
{
    public enum SessionOutcome
    {
        Completed,
        Collision,
        Fall,
        Stopped
    }

    public enum SessionFlowState
    {
        Preparing,
        Running,
        Paused,
        Ending,
        Results,
        SetupError,
        Recovering,
        Streaming
    }

    // Temporary in-memory end data, NOT the persisted Chapter 4 SessionSummary.
    public sealed class SessionEndInfo
    {
        public string SessionId { get; }
        public SessionOutcome Outcome { get; }
        public double ActiveDurationSeconds { get; }
        public int TrackSeed { get; }
        public string TrackIdentifier { get; }
        public SessionScore Score { get; }
        public double? PlannedRouteDurationSeconds { get; }

        public SessionEndInfo(string id, SessionOutcome outcome, double seconds, int seed, string trackIdentifier, SessionScore score, double? plannedDuration)
        {
            SessionId = id;
            Outcome = outcome;
            ActiveDurationSeconds = seconds;
            TrackSeed = seed;
            TrackIdentifier = trackIdentifier;
            Score = score;
            PlannedRouteDurationSeconds = plannedDuration;
        }
    }

    [DisallowMultipleComponent]
    public sealed class GameplayRunController : MonoBehaviour
    {
        [SerializeField]
        private BallMovement ballMovement;
        [SerializeField]
        private TrackGenerator trackGenerator;
        [SerializeField, HideInInspector]
        private Vector3 spawnPosition = new Vector3(0f, .75f, 0f);
        [SerializeField]
        private float fallThreshold = -5f;
        [SerializeField]
        private GameObject finishGatePrefab;
        [Header("Session presentation")]
        [SerializeField]
        private PlayerHazardContact hazardContact;
        [SerializeField]
        private SessionFlowUI sessionUI;
        [SerializeField]
        private SessionTaskTracker taskTracker;
        [Tooltip("Assign the CameraFollow component, NOT the Camera or BallMovement.")]
        [SerializeField]
        private Behaviour gameplayCameraFollow;
        [Header("Finite-mode respawn")]
        [SerializeField, Min(0f)]
        private float respawnDelaySeconds = 1f;
        [SerializeField, Min(0f)]
        private float respawnFlashSeconds = 3f;
        [SerializeField, Min(.1f)]
        private float respawnBlinkInterval = .25f;
        [Header("Finite finish presentation")]
        [SerializeField, Min(.1f)]
        private float finishFireworksSeconds = 5f;
        [SerializeField, Min(.05f)]
        private float finishFadeOutSeconds = .6f;
        [SerializeField, Min(.05f)]
        private float finishFadeInSeconds = .6f;
        [SerializeField, Min(.1f)]
        private float finishSlowdownSeconds = 2f;
        public SessionFlowState State { get; private set; } = SessionFlowState.Preparing;
        public SessionEndInfo EndInfo { get; private set; }
        public bool IsCompleted => EndInfo != null && EndInfo.Outcome == SessionOutcome.Completed;

        public event Action Completed;
        public DifficultyMode TrackDifficulty => trackGenerator.CurrentDifficulty;
        public double ActiveDurationSeconds => activeSeconds + (State == SessionFlowState.Running ? Math.Max(0d, Time.timeAsDouble - activeIntervalStart) : 0d);

        private TrackGenerator subscribedGenerator;
        private GameObject finishGateInstance;
        private FinishGateTrigger finishGateTrigger;
        private bool started;
        private bool initialRunStarted;
        private bool ownsClock;
        private bool cameraWasEnabled;
        private float savedTimeScale = 1f;
        private double activeSeconds;
        private double activeIntervalStart;
        private string sessionId;
        private int discardInputThroughFrame = -1;
        private float recoveryMinimumZ;
        private double recoveryReadyAt;
        private double recoveryStartedAt;
        private BallShatterEffect ballShatter;
        private BallBurnEffect ballBurn;
        private const float BurnRollSeconds = 2.1f;
        private const float BurnPresentationSeconds = 3f;
        private bool burnRecovering;
        private bool burnEnding;
        private double burnEndingStartedAt;
        private bool respawnFlashing;
        private float respawnFlashElapsed;
        private Renderer[] respawnRenderers = Array.Empty<Renderer>();
        private bool[] rendererEnabledBeforeFlash = Array.Empty<bool>();
        private enum FinishPhase
        {
            None,
            Fireworks,
            FadeOut,
            Covered,
            FadeIn
        }

        private FinishPhase finishPhase;
        private double finishPhaseStartedAt;
        private int finishCoveredFrame;
        private FinishCelebrationEffect finishCelebration;
        private void OnEnable()
        {
            SubscribeToGenerator();
        }

        private void Start()
        {
            started = true;
            if (ballMovement == null || trackGenerator == null || sessionUI == null || !sessionUI.IsConfigured || hazardContact == null || !hazardContact.IsConfiguredFor(this, ballMovement) || taskTracker == null || !taskTracker.IsConfiguredFor(this, ballMovement) || gameplayCameraFollow == null)
            {
                FailSetup("Assign the ball, generator, configured UI/contact reporter and CameraFollow.");
                return;
            }

            sessionUI.HidePanels();
            if (!initialRunStarted) ballMovement.StopMotionForCompletion();
            if (!initialRunStarted && trackGenerator.IsRunPrepared)
                HandleRunPrepared();
        }

        private void Update()
        {
            UpdateRespawnFlash();
            if (State == SessionFlowState.Running && ballMovement.transform.position.y < fallThreshold)
            {
                HandleFailure(SessionOutcome.Fall);
                return;
            }

            if (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
            {
                if (State == SessionFlowState.Running || State == SessionFlowState.Streaming)
                    PauseSession();
                else if (State == SessionFlowState.Paused)
                    ResumeSession();
            }
        }

        private void LateUpdate()
        {
            if(State==SessionFlowState.Ending && burnEnding)
            {
                float elapsed=(float)(Time.unscaledTimeAsDouble-burnEndingStartedAt);
                ballBurn?.Sample(elapsed,BurnPresentationSeconds);
                if(elapsed>=BurnRollSeconds)
                {
                    ballMovement.StopMotionForCompletion();
                    HoldClockAndCamera();
                }
                if(elapsed>=BurnPresentationSeconds) {burnEnding=false;State=SessionFlowState.Results;sessionUI.ShowResult(EndInfo);}
            }
            if (State == SessionFlowState.Ending && finishPhase != FinishPhase.None)
                UpdateFinishPresentation();
            // Collision callbacks latch once; teleport only after the physics step has finished.
            if (State == SessionFlowState.Recovering)
            {
                ballShatter?.Sample((float)(Time.unscaledTimeAsDouble - recoveryStartedAt), (float)(recoveryReadyAt - recoveryStartedAt));
                if(burnRecovering)
                {
                    float elapsed=(float)(Time.unscaledTimeAsDouble-recoveryStartedAt);
                    ballBurn?.Sample(elapsed,BurnPresentationSeconds);
                    if(elapsed>=BurnRollSeconds)
                    {
                        ballMovement.StopMotionForCompletion();
                        HoldClockAndCamera();
                    }
                }
                if (Time.unscaledTimeAsDouble >= recoveryReadyAt)
                    CompleteRecovery();
            }

            // All input-provider Updates have finished before this drain.
            if (ballMovement != null && (State != SessionFlowState.Running || Time.frameCount <= discardInputThroughFrame))
                ballMovement.DiscardBufferedInput();
        }

        private bool loadingHold;
        private void HandleStreamingHold(bool hold)
        {
            loadingHold = hold;
            if (hold && State == SessionFlowState.Running)
            {
                CaptureActiveInterval();
                State = SessionFlowState.Streaming;
                HoldClockAndCamera();
            }
            else if (!hold && State == SessionFlowState.Streaming)
            {
                ReleaseClockAndCamera();
                ballMovement.DiscardBufferedInput();
                discardInputThroughFrame = Time.frameCount;
                activeIntervalStart = Time.timeAsDouble;
                State = SessionFlowState.Running;
            }
        }

        public void PauseSession()
        {
            if (State != SessionFlowState.Running && State != SessionFlowState.Streaming)
                return;
            if (State == SessionFlowState.Running)
            {
                taskTracker.ObservePlayer();
                CaptureActiveInterval();
            }
            State = SessionFlowState.Paused;
            trackGenerator.SetGenerationPaused(true);
            HoldClockAndCamera();
            if (respawnFlashing)
                SetRespawnVisibility(true);
            sessionUI.ShowPause();
        // Do not stop/reset the Rigidbody: its exact pose/velocity is retained.
        }

        public void ResumeSession()
        {
            if (State != SessionFlowState.Paused)
                return;
            ballMovement.DiscardBufferedInput();
            discardInputThroughFrame = Time.frameCount;
            sessionUI.HidePanels();
            if (!loadingHold) ReleaseClockAndCamera();
            activeIntervalStart = Time.timeAsDouble;
            State = loadingHold ? SessionFlowState.Streaming : SessionFlowState.Running;
            trackGenerator.SetGenerationPaused(false);
        // No ResumeMotion, ResetMotion, ResetRun or scene reload here.
        }

        public void EndSessionFromPause()
        {
            if (State == SessionFlowState.Paused)
                EndSessionOnce(SessionOutcome.Stopped);
        }

        public void ReportEncounterFall()
        {
            if(State == SessionFlowState.Running) HandleFailure(SessionOutcome.Fall);
        }

        public void ReportObstacleCollision(Collider relatedHazard = null)
        {
            if (State == SessionFlowState.Running)
                HandleFailure(SessionOutcome.Collision, relatedHazard);
        }

        public void ReportCargoContact(Collider cargo)
        {
            ReportNavigationContact(cargo);
        }

        public void ReportNavigationContact(Collider hazard)
        {
            if (State == SessionFlowState.Running)
                taskTracker.RecordNavigationContact(hazard);
        }

        private void HandleFailure(SessionOutcome outcome, Collider relatedHazard = null)
        {
            if (State != SessionFlowState.Running)
                return;

            if (!trackGenerator.HasFiniteRoute)
            {
                EndSessionOnce(outcome, relatedHazard);
                return;
            }

            CaptureActiveInterval();
            State = SessionFlowState.Recovering;
            StopRespawnFlash();

            trackGenerator.SetGenerationPaused(true);

            burnRecovering=BallBurnEffect.IsFireContact(relatedHazard);
            if(burnRecovering) ballMovement.BeginBurnRoll(BurnRollSeconds);
            else
            {
                HoldClockAndCamera();
                ballMovement.StopMotionForCompletion();
            }

            recoveryMinimumZ =
                taskTracker.RecordFailure(outcome, relatedHazard);

            recoveryStartedAt = Time.unscaledTimeAsDouble;
            recoveryReadyAt =
                recoveryStartedAt + Mathf.Max(0f, respawnDelaySeconds);

            ballShatter?.Stop();
            ballBurn?.Stop();
            if(burnRecovering)
            {
                if(ballBurn==null) ballBurn=new BallBurnEffect();
                recoveryReadyAt=Math.Max(recoveryReadyAt,recoveryStartedAt+BurnPresentationSeconds);
                ballBurn.Play(ballMovement);
            }
            else
            {
                if(ballShatter==null) ballShatter=new BallShatterEffect();
                ballShatter.Play(ballMovement);
            }
        }

        private void CompleteRecovery()
        {
            if (!taskTracker.TryGetRecoveryPoint(recoveryMinimumZ, trackGenerator.FinishLineZ, out Vector3 position))
            {
                FailSetup("No safe generated road support after the failed challenge; recovery stopped.");
                return;
            }

            ballShatter?.Stop();
            ballBurn?.Stop();
            burnRecovering=false;
            burnEnding=false;
            taskTracker.PrepareRecoveryArrival(position.z);
            ReleaseClockAndCamera();
            ballMovement.ResetMotion(position, Quaternion.identity);
            ballMovement.DiscardBufferedInput();
            discardInputThroughFrame = Time.frameCount;
            if (gameplayCameraFollow is CameraFollow follow)
                follow.SnapToTarget();
            activeIntervalStart = Time.timeAsDouble;
            State = SessionFlowState.Running;
            trackGenerator.SetGenerationPaused(false);
            BeginRespawnFlash();
            Debug.Log($"Finite session recovered after failed challenge at Z={position.z:F2}; scores and seed retained.", this);
        }

        private void BeginRespawnFlash()
        {
            StopRespawnFlash();
            if (respawnFlashSeconds <= 0f)
                return;
            respawnRenderers = ballMovement.GetComponentsInChildren<Renderer>(true);
            rendererEnabledBeforeFlash = new bool[respawnRenderers.Length];
            for (int i = 0; i < respawnRenderers.Length; i++)
                rendererEnabledBeforeFlash[i] = respawnRenderers[i].enabled;
            respawnFlashElapsed = 0f;
            respawnFlashing = true;
        }

        private void UpdateRespawnFlash()
        {
            if (!respawnFlashing || State != SessionFlowState.Running)
                return;
            respawnFlashElapsed += Time.deltaTime;
            if (respawnFlashElapsed >= respawnFlashSeconds)
            {
                StopRespawnFlash();
                return;
            }

            bool visible = Mathf.FloorToInt(respawnFlashElapsed / Mathf.Max(.1f, respawnBlinkInterval)) % 2 == 0;
            SetRespawnVisibility(visible);
        }

        private void SetRespawnVisibility(bool visible)
        {
            // Only renderers blink. Keep the ball, physics, colliders and task evidence active.
            for (int i = 0; i < respawnRenderers.Length; i++)
                if (respawnRenderers[i] != null)
                    respawnRenderers[i].enabled = visible && rendererEnabledBeforeFlash[i];
        }

        private void StopRespawnFlash()
        {
            SetRespawnVisibility(true);
            respawnFlashing = false;
            respawnRenderers = Array.Empty<Renderer>();
            rendererEnabledBeforeFlash = Array.Empty<bool>();
        }

        private void CompleteRun()
        {
            if (State == SessionFlowState.Running && trackGenerator.HasFiniteRoute)
                EndSessionOnce(SessionOutcome.Completed);
        }

        private void EndSessionOnce(SessionOutcome outcome, Collider relatedHazard = null)
        {
            if (State != SessionFlowState.Running && State != SessionFlowState.Paused)
                return;
            if (outcome == SessionOutcome.Completed && !trackGenerator.HasFiniteRoute)
                return;
            if (State == SessionFlowState.Running)
                CaptureActiveInterval();
            State = SessionFlowState.Ending; // Latch before stopping physics/callbacks.
            ballShatter?.Stop();
            ballBurn?.Stop();
            burnEnding=false;
            StopRespawnFlash();
            trackGenerator.SetGenerationPaused(true);
            if (outcome == SessionOutcome.Completed)
            {
                HoldCamera(); // Retain the finish composition while the ball rolls through it.
                ballMovement.BeginFinishRoll(trackGenerator.FinishLineZ, finishSlowdownSeconds);
            }
            else if(outcome==SessionOutcome.Collision && BallBurnEffect.IsFireContact(relatedHazard))
            {
                ballMovement.BeginBurnRoll(BurnRollSeconds);
            }
            else
            {
                HoldClockAndCamera();
                ballMovement.StopMotionForCompletion();
            }

            if (outcome == SessionOutcome.Completed)
                finishGateTrigger?.Disarm();
            else
                HideFinishGate();
            SessionScore score = taskTracker.FinalizeSession(outcome, relatedHazard);
            EndInfo = new SessionEndInfo(sessionId, outcome, activeSeconds, trackGenerator.CurrentSeed, trackGenerator.TrackIdentifier, score, trackGenerator.HasFiniteRoute ? (double? )trackGenerator.PlannedRouteDurationSeconds : null);
            // 7C-1B will insert short, unscaled failure feedback HERE.
            // The immutable ending/duration already exist; do not reset the route.
            Debug.Log($"Session ended: {EndInfo.SessionId} | {outcome} | active={activeSeconds:F2}s", this);
            if (outcome == SessionOutcome.Completed)
            {
                sessionUI.ShowEndingPresentation();
                if (finishCelebration == null)
                    finishCelebration = new FinishCelebrationEffect();
                finishCelebration.Play(gameObject.scene, new Vector3(0f, 0f, trackGenerator.FinishLineZ), Mathf.Max(.1f, finishFireworksSeconds));
                finishPhase = FinishPhase.Fireworks;
                finishPhaseStartedAt = Time.unscaledTimeAsDouble;
                return;
            }

            if(outcome==SessionOutcome.Collision && BallBurnEffect.IsFireContact(relatedHazard))
            {
                if(ballBurn==null) ballBurn=new BallBurnEffect();
                ballBurn.Play(ballMovement);burnEnding=true;burnEndingStartedAt=Time.unscaledTimeAsDouble;
                sessionUI.ShowEndingPresentation();return;
            }
            State = SessionFlowState.Results;
            sessionUI.ShowResult(EndInfo);
        }

        private void UpdateFinishPresentation()
        {
            // Physics keeps advancing during the rollout, but scored gameplay already ended at the gate.
            if (ballMovement.IsFinishRollComplete)
                HoldClockAndCamera();
            float elapsed = (float)(Time.unscaledTimeAsDouble - finishPhaseStartedAt);
            switch (finishPhase)
            {
                case FinishPhase.Fireworks:
                    finishCelebration.TickFireworks(elapsed, Mathf.Max(.1f, finishFireworksSeconds));
                    if (elapsed < Mathf.Max(.1f, finishFireworksSeconds) || !ballMovement.IsFinishRollComplete)
                        return;
                    finishPhase = FinishPhase.FadeOut;
                    finishPhaseStartedAt = Time.unscaledTimeAsDouble;
                    break;
                case FinishPhase.FadeOut:
                    float cover = Mathf.Clamp01(elapsed / Mathf.Max(.05f, finishFadeOutSeconds));
                    finishCelebration.SetFade(Mathf.SmoothStep(0f, 1f, cover));
                    if (cover < 1f)
                        return;
                    finishPhase = FinishPhase.Covered;
                    finishCoveredFrame = Time.frameCount;
                    break;
                case FinishPhase.Covered:
                    // Render a completely covered frame before swapping to the existing results panel.
                    if (Time.frameCount <= finishCoveredFrame)
                        return;
                    finishCelebration.ClearFireworks();
                    sessionUI.ShowResult(EndInfo);
                    finishPhase = FinishPhase.FadeIn;
                    finishPhaseStartedAt = Time.unscaledTimeAsDouble;
                    break;
                case FinishPhase.FadeIn:
                    float reveal = Mathf.Clamp01(elapsed / Mathf.Max(.05f, finishFadeInSeconds));
                    finishCelebration.SetFade(1f - Mathf.SmoothStep(0f, 1f, reveal));
                    if (reveal < 1f)
                        return;
                    StopFinishPresentation();
                    State = SessionFlowState.Results;
                    Completed?.Invoke();
                    break;
            }
        }

        private void StopFinishPresentation()
        {
            finishPhase = FinishPhase.None;
            finishCelebration?.Stop();
        }

        private void CaptureActiveInterval()
        {
            activeSeconds += Math.Max(0d, Time.timeAsDouble - activeIntervalStart);
            activeIntervalStart = Time.timeAsDouble;
        }

        private void HoldClockAndCamera()
        {
            HoldCamera();
            Time.timeScale = 0f;
        }

        private void HoldCamera()
        {
            if (!ownsClock)
            {
                savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                cameraWasEnabled = gameplayCameraFollow != null && gameplayCameraFollow.enabled;
                ownsClock = true;
            }

            if (gameplayCameraFollow != null)
                gameplayCameraFollow.enabled = false;
        }

        private void ReleaseClockAndCamera()
        {
            if (!ownsClock)
                return;
            Time.timeScale = savedTimeScale;
            if (gameplayCameraFollow != null)
                gameplayCameraFollow.enabled = cameraWasEnabled;
            ownsClock = false;
        }

        private void HandleRunResetting()
        {
            if (initialRunStarted)
            {
                FailSetup("Do not reset an active/ended session in place. Return through results.");
                return;
            }

            if (ballMovement != null)
                ballMovement.StopMotionForCompletion();
            if (gameplayCameraFollow is CameraFollow follow)
                follow.SnapToTarget();
            HideFinishGate();
        }

        private void HandleRunPrepared()
        {
            if (!started || !isActiveAndEnabled || trackGenerator == null || !trackGenerator.IsRunPrepared || State == SessionFlowState.SetupError)
                return;
            if (initialRunStarted)
            {
                FailSetup("A new session must not reuse this controller's ended run.");
                return;
            }

            if (taskTracker != null && taskTracker.HasRegistrationErrors)
            {
                FailSetup("A scored challenge could not be registered; review its geometry and Console.");
                return;
            }

            if (sessionUI == null || !sessionUI.IsConfigured || ballMovement == null || hazardContact == null || !hazardContact.IsConfiguredFor(this, ballMovement) || taskTracker == null || !taskTracker.IsConfiguredFor(this, ballMovement) || gameplayCameraFollow == null)
            {
                FailSetup("Complete Stage 7C Inspector assignments before playing.");
                return;
            }

            if (trackGenerator.HasFiniteRoute && !PrepareFinishGate())
            {
                FailSetup("Finite run needs the existing correctly configured finish gate.");
                return;
            }

            initialRunStarted = true;
            sessionId = Guid.NewGuid().ToString("N");
            activeSeconds = 0d;
            EndInfo = null;
            taskTracker.BeginSession(sessionId);
            sessionUI.BindRun(this, taskTracker);
            ballMovement.ResumeMotion(); // Initial setup ONLY, not Pause/Resume.
            ballMovement.DiscardBufferedInput();
            discardInputThroughFrame = Time.frameCount;
            sessionUI.HidePanels();
            activeIntervalStart = Time.timeAsDouble;
            State = SessionFlowState.Running;
            trackGenerator.SetGenerationPaused(false);
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
                FinishGateTrigger[] triggers = finishGateInstance.GetComponentsInChildren<FinishGateTrigger>(true);
                if (triggers.Length != 1)
                    return false;
                finishGateTrigger = triggers[0];
            }

            if (finishGateTrigger == null)
                return false;
            finishGateInstance.transform.SetPositionAndRotation(new Vector3(0f, 0f, trackGenerator.FinishLineZ), Quaternion.identity);
            finishGateInstance.transform.localScale = Vector3.one;
            finishGateInstance.SetActive(true);
            return finishGateTrigger.Arm(ballMovement, CompleteRun);
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
            StopFinishPresentation();
            ballShatter?.Stop();
            ballBurn?.Stop();
            burnRecovering=false;
            burnEnding=false;
            StopRespawnFlash();
            State = SessionFlowState.SetupError;
            HideFinishGate();
            if (trackGenerator != null)
                trackGenerator.SetGenerationPaused(true);
            if (ballMovement != null)
                ballMovement.StopMotionForCompletion();
            Debug.LogError($"Gameplay run setup: {reason}", this);
        }

        public void ReportScoringSetupError(string reason) => FailSetup("Scoring: " + reason);
        // Preserve the old caller signature, but never expose in-game restarting.
        public void RestartRun()
        {
            Debug.LogWarning("In-game restart is disabled. End the session and use the result/menu flow.", this);
        }

        private void SubscribeToGenerator()
        {
            UnsubscribeFromGenerator();
            subscribedGenerator = trackGenerator;
            if (subscribedGenerator == null)
                return;
            subscribedGenerator.RunResetting += HandleRunResetting;
            subscribedGenerator.RunPrepared += HandleRunPrepared;
            subscribedGenerator.RunSetupFailed += FailSetup;
            subscribedGenerator.StreamingHoldChanged += HandleStreamingHold;
        }

        private void UnsubscribeFromGenerator()
        {
            if (subscribedGenerator != null)
            {
                subscribedGenerator.RunResetting -= HandleRunResetting;
                subscribedGenerator.RunPrepared -= HandleRunPrepared;
                subscribedGenerator.RunSetupFailed -= FailSetup;
                subscribedGenerator.StreamingHoldChanged -= HandleStreamingHold;
            }

            subscribedGenerator = null;
        }

        private void OnDisable()
        {
            burnRecovering=false;
            if (finishPhase != FinishPhase.None)
                State = SessionFlowState.SetupError;
            StopFinishPresentation();
            ballShatter?.Stop();
            ballBurn?.Stop();
            burnEnding=false;
            StopRespawnFlash();
            UnsubscribeFromGenerator();
            if (State == SessionFlowState.Running)
                CaptureActiveInterval();
            if (State == SessionFlowState.Running || State == SessionFlowState.Paused || State == SessionFlowState.Recovering || State == SessionFlowState.Streaming)
                State = SessionFlowState.SetupError; // Re-enable never revives a run.
            HideFinishGate();
            if (trackGenerator != null)
                trackGenerator.SetGenerationPaused(true);
            if (ballMovement != null)
                ballMovement.StopMotionForCompletion();
            ReleaseClockAndCamera(); // Do not leak Time.timeScale=0 into another scene.
        }

        private void OnDestroy()
        {
            finishCelebration?.Dispose();
            ballShatter?.Dispose();
            ballBurn?.Dispose();
            if (finishGateInstance != null)
                Destroy(finishGateInstance);
        }

        public void ConfigureForScene(BallMovement configuredBallMovement, TrackGenerator configuredTrackGenerator, Vector3 configuredSpawnPosition, float configuredFallThreshold)
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
