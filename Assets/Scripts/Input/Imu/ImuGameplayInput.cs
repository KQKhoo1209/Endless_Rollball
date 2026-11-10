using UnityEngine;
using UnityEngine.Serialization;

namespace EndlessRollball.Imu
{
    public enum ImuControlContext { Road, Cloud, AvatarRotation }

    [DisallowMultipleComponent, RequireComponent(typeof(KeyboardGameplayInput))]
    public sealed class ImuGameplayInput : MonoBehaviour, IGameplayInput
    {
        [Header("Development fallback")]
        [SerializeField] private bool keyboardOnly;
        [Header("Shared wrist roll (road, cloud, surfing and lava)")]
        [FormerlySerializedAs("invertSteering")]
        [SerializeField] private bool invertRoll;
        [Header("Ball positioning response (neutral = centre)")]
        [SerializeField, Min(0f)] private float steeringDeadZone = 5f;
        [SerializeField, Min(1f)] private float fullSteeringAngle = 20f;
        [Header("Avatar turning response (surf / lava only)")]
        [SerializeField, Min(0f)] private float rotationDeadZone = 7f;
        [SerializeField, Min(1f)] private float fullRotationAngle = 25f;
        [Header("Wrist-tilt jump (road only; clouds bounce automatically)")]
        [SerializeField] private bool invertJump;
        [SerializeField, Min(1f)] private float jumpAngle = 20f;
        [SerializeField, Min(0f)] private float jumpReleaseAngle = 8f;
        [SerializeField, Min(.02f)] private float jumpHoldSeconds = .06f;
        [SerializeField, Min(.02f)] private float jumpReleaseSeconds = .1f;
        [SerializeField, Min(.1f)] private float jumpCooldown = .6f;
        [SerializeField, Min(.01f)] private float smoothingSeconds = .08f;
        private ImuRuntimeSession session;
        private KeyboardGameplayInput keyboard;
        private BallMovement ball;
        private ImuControlContext context;
        private float rollAngle;
        private double previousSample, raisedSince = -1, neutralSince = -1, lastJump = -100, queuedUntil;
        private bool jumpArmed, jumpQueued;
        private int revision = -1;
        public bool UsesImu => GameSessionSettings.SelectedInputMode == GameInputMode.Imu &&
            !keyboardOnly && session != null && session.InputRequested;
        public bool IsInputReady => !UsesImu || session.HasFreshInput;
        public ImuControlContext Context => context;

        private void Awake()
        {
            keyboard = GetComponent<KeyboardGameplayInput>();
            ball = GetComponent<BallMovement>();
        }
        private void OnEnable()
        {
            session = ImuRuntimeSession.Ensure();
            session.SampleReceived += OnSample;
            ClearTransientInput();
        }
        private void OnDisable()
        {
            if (session != null) session.SampleReceived -= OnSample;
            ClearTransientInput();
        }

        public void SetContext(ImuControlContext next)
        {
            if (next == context) return;
            context = next;
            // Context changes the response curve, not the gesture or calibrated neutral.
            ClearTransientInput();
        }

        public void ClearTransientInput()
        {
            jumpQueued = jumpArmed = false;
            raisedSince = neutralSince = -1;
            if (keyboard != null) keyboard.ReadCommand();
        }

        private void OnSample(ImuSample sample)
        {
            if (!UsesImu || !session.HasFreshInput || Time.timeScale <= 0f)
            { ClearTransientInput(); previousSample = sample.ReceivedAt; return; }
            if (revision != session.CalibrationRevision)
            {
                revision = session.CalibrationRevision;
                rollAngle = 0;
                previousSample = 0;
                ClearTransientInput();
            }
            float dt = previousSample > 0 ? (float)(sample.ReceivedAt - previousSample) : .02f;
            previousSample = sample.ReceivedAt;
            if (dt > .1f) ClearTransientInput();
            float blend = 1f - Mathf.Exp(-Mathf.Clamp(dt, .001f, .1f) / smoothingSeconds);
            rollAngle = Mathf.Lerp(rollAngle, Mathf.DeltaAngle(session.NeutralTiltDegrees.x, sample.Roll), blend);
            if (context != ImuControlContext.Road) { ClearTransientInput(); return; }
            float pitch = Mathf.DeltaAngle(session.NeutralTiltDegrees.y, sample.Pitch) * (invertJump ? -1 : 1);
            if (Mathf.Abs(pitch) <= jumpReleaseAngle)
            {
                if (neutralSince < 0) neutralSince = sample.ReceivedAt;
                if (sample.ReceivedAt - neutralSince >= jumpReleaseSeconds) jumpArmed = true;
            }
            else neutralSince = -1;
            if (!jumpArmed || pitch < jumpAngle) { raisedSince = -1; return; }
            if (raisedSince < 0) raisedSince = sample.ReceivedAt;
            if (sample.ReceivedAt - raisedSince < jumpHoldSeconds) return;
            jumpArmed = false;
            raisedSince = -1;
            // Consume airborne gestures rather than saving them for a later landing.
            if (ball != null && ball.IsGrounded && sample.ReceivedAt - lastJump >= jumpCooldown)
            {
                jumpQueued = true;
                queuedUntil = sample.ReceivedAt + .15;
                lastJump = sample.ReceivedAt;
            }
        }

        private static float Map(float angle, float deadZone, float fullAngle) =>
            Mathf.Sign(angle) * Mathf.Clamp01((Mathf.Abs(angle) - deadZone) / Mathf.Max(1f, fullAngle - deadZone));

        public GameplayCommand ReadCommand()
        {
            if (!IsInputReady || Time.timeScale <= 0f) { ClearTransientInput(); return GameplayCommand.Neutral; }
            if (!UsesImu)
            {
                GameplayCommand raw = keyboard != null ? keyboard.ReadCommand() : GameplayCommand.Neutral;
                return new GameplayCommand(raw.Steering, 0,
                    context == ImuControlContext.Road && raw.JumpPressed);
            }
            float signedRoll = rollAngle * (invertRoll ? -1 : 1);
            if (context == ImuControlContext.AvatarRotation)
                return new GameplayCommand(Map(signedRoll, rotationDeadZone, fullRotationAngle), 0, false);
            bool jump = context == ImuControlContext.Road && jumpQueued &&
                WindowsBleImuTransport.Now <= queuedUntil && ball != null && ball.IsGrounded;
            jumpQueued = false;
            return new GameplayCommand(Map(signedRoll, steeringDeadZone, fullSteeringAngle),
                0, jump, steeringIsPosition: true);
        }
    }
}
