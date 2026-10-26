using System;
using UnityEngine;
using EndlessRollball.Track;

[RequireComponent(typeof(Rigidbody), typeof(KeyboardGameplayInput))]
public sealed class BallMovement : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Must implement IGameplayInput. Leave empty to use the first provider on this object.")]
    [SerializeField]
    private MonoBehaviour inputProvider;
    [Header("Steering")]
    [SerializeField, Min(0f)]
    private float steeringAcceleration = 22f;
    [SerializeField, Min(0f)]
    private float maxLateralSpeed = 7f;
    [SerializeField, Min(0f)]
    private float steeringResponse = 8f;
    [Header("Forward Speed")]
    [SerializeField, Min(0f)]
    private float baseForwardSpeed = 7f;
    [SerializeField, Min(0f)]
    private float speedAdjustmentRange = 4f;
    [SerializeField, Min(0f)]
    private float forwardAcceleration = 18f;
    [SerializeField, Min(0f)]
    private float forwardResponse = 4f;
    [Header("Jump")]
    [SerializeField, Min(0f)]
    private float jumpSpeed = 6f;
    [SerializeField, Range(0f, 1f)]
    private float minimumGroundNormal = 0.55f;
    [SerializeField, Min(0f)]
    private float jumpCooldown = 0.25f;
    private RoadCorridor corridor = new RoadCorridor(9f);
    private SphereCollider sphereCollider;
    public RoadCorridor Corridor => corridor;
    public float SphereRadius => sphereCollider == null ? .5f : sphereCollider.radius * Mathf.Max(Mathf.Abs(sphereCollider.transform.lossyScale.x), Mathf.Abs(sphereCollider.transform.lossyScale.y), Mathf.Abs(sphereCollider.transform.lossyScale.z));
    public float JumpSpeed => jumpSpeed;
    public float MaxLateralSpeed => maxLateralSpeed;
    public float SteeringAcceleration => steeringAcceleration;

    public void SetRoadCorridor(RoadCorridor value) => corridor = value;
    private void EnforceRoadCorridor()
    {
        Vector3 position = ballRigidbody.position;
        Vector3 velocity = ballRigidbody.linearVelocity;
        float centreOffset = sphereCollider == null ? 0f : transform.TransformVector(sphereCollider.center).x;
        float centre = position.x + centreOffset;
        float clamped = corridor.Clamp(centre, SphereRadius);
        if (!Mathf.Approximately(centre, clamped))
        {
            position.x = clamped - centreOffset;
            ballRigidbody.position = position;
        }

        float next = centre + velocity.x * Time.fixedDeltaTime;
        float nextClamped = corridor.Clamp(next, SphereRadius);
        if ((next > nextClamped && velocity.x > 0f) || (next < nextClamped && velocity.x < 0f))
            velocity.x = (nextClamped - clamped) / Time.fixedDeltaTime;
        ballRigidbody.linearVelocity = velocity;
    }

    private Rigidbody ballRigidbody;
    private IGameplayInput gameplayInput;
    private bool isGrounded;
    private float lastJumpTime = float.NegativeInfinity;
    private bool movementStopped;
    private RigidbodyConstraints constraintsBeforeStop;
    private Collider groundCollider;
    public bool IsGrounded => isGrounded;
    public Collider GroundCollider => groundCollider;

    public event Action<Collider> JumpAccepted;
    public event Action<Collider> GroundContact;
    public event Action AirborneObserved;
    private bool finishRolling;
    private float finishCoastThroughZ, finishRollSpeed, finishBrakeDuration, finishBrakeElapsed;
    private Vector3 finishAngularVelocity;
    internal bool IsFinishRollComplete => !finishRolling;

    private void Awake()
    {
        ballRigidbody = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();
        gameplayInput = ResolveInputProvider();
        if (gameplayInput == null)
        {
            Debug.LogError($"{nameof(BallMovement)} needs a component that implements {nameof(IGameplayInput)}.", this);
            enabled = false;
        }
    }

    private void FixedUpdate()
    {
        if (movementStopped)
            return;
        if (finishRolling)
        {
            UpdateFinishRoll();
            EnforceRoadCorridor();
            isGrounded = false;
            groundCollider = null;
            return; // No player steering/jump input or normal forward acceleration after completion.
        }

        // Previous physics step had no upward support; this is evidence, not a jump-physics change.
        if (!isGrounded)
            AirborneObserved?.Invoke();
        GameplayCommand command = gameplayInput.ReadCommand();
        EnforceRoadCorridor();
        ApplySteering(command.Steering);
        ApplyForwardSpeed();
        TryJump(command.JumpPressed);
        EnforceRoadCorridor();
        isGrounded = false;
        groundCollider = null;
    }

    private void ApplySteering(float steering)
    {
        float targetLateralSpeed = steering * maxLateralSpeed;
        float currentLateralSpeed = ballRigidbody.linearVelocity.x;
        float speedError = targetLateralSpeed - currentLateralSpeed;
        float acceleration = Mathf.Clamp(speedError * steeringResponse, -steeringAcceleration, steeringAcceleration);
        float centre = ballRigidbody.position.x + (sphereCollider == null ? 0f : transform.TransformVector(sphereCollider.center).x);
        float edge = corridor.CentreLimit(SphereRadius);
        if ((centre >= corridor.CentreX + edge - .005f && acceleration > 0f) || (centre <= corridor.CentreX - edge + .005f && acceleration < 0f))
            acceleration = 0f;
        ballRigidbody.AddForce(Vector3.right * acceleration, ForceMode.Acceleration);
    }

    private void ApplyForwardSpeed()
    {
        float targetSpeed = baseForwardSpeed;
        float speedError = targetSpeed - ballRigidbody.linearVelocity.z;
        float acceleration = Mathf.Clamp(speedError * forwardResponse, -forwardAcceleration, forwardAcceleration);
        ballRigidbody.AddForce(Vector3.forward * acceleration, ForceMode.Acceleration);
    }

    private void TryJump(bool jumpPressed)
    {
        if (!jumpPressed || !isGrounded || Time.time < lastJumpTime + jumpCooldown)
        {
            return;
        }

        Collider support = groundCollider;
        Vector3 velocity = ballRigidbody.linearVelocity;
        velocity.y = Mathf.Max(0f, velocity.y);
        ballRigidbody.linearVelocity = velocity;
        ballRigidbody.AddForce(Vector3.up * jumpSpeed, ForceMode.VelocityChange);
        lastJumpTime = Time.time;
        isGrounded = false;
        JumpAccepted?.Invoke(support);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Landing evidence only: do not alter the existing jump-grounding timing.
        for (int index = 0; index < collision.contactCount; index++)
            if (collision.GetContact(index).normal.y >= minimumGroundNormal)
            {
                GroundContact?.Invoke(collision.collider);
                return;
            }
    }

    private void OnCollisionStay(Collision collision)
    {
        for (int index = 0; index < collision.contactCount; index++)
        {
            if (collision.GetContact(index).normal.y >= minimumGroundNormal)
            {
                isGrounded = true;
                groundCollider = collision.collider;
                GroundContact?.Invoke(collision.collider);
                return;
            }
        }
    }

    public void SetForwardSpeed(float configuredBaseSpeed, float configuredAdjustmentRange)
    {
        baseForwardSpeed = Mathf.Max(0f, configuredBaseSpeed);
        speedAdjustmentRange = Mathf.Max(0f, configuredAdjustmentRange);
    }

    public void ResetMotion(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();
        // Route planning reads the Transform immediately, before the next
        // physics step. Keep it in sync with the Rigidbody teleport.
        worldPosition.x = corridor.Clamp(worldPosition.x, SphereRadius);
        transform.SetPositionAndRotation(worldPosition, worldRotation);
        ballRigidbody.position = worldPosition;
        ballRigidbody.rotation = worldRotation;
        ResumeMotion();
    }

    public void ResumeMotion()
    {
        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();
        if (movementStopped)
            ballRigidbody.constraints = constraintsBeforeStop;
        finishRolling = false;
        movementStopped = false;
        ballRigidbody.linearVelocity = Vector3.zero;
        ballRigidbody.angularVelocity = Vector3.zero;
        ballRigidbody.WakeUp();
        isGrounded = false;
        lastJumpTime = float.NegativeInfinity;
        groundCollider = null;
        // KeyboardGameplayInput queues jump presses. Discard a pending
        // command so input pressed on the completed screen is not replayed.
        if (gameplayInput != null)
            gameplayInput.ReadCommand();
    }

    public void StopMotionForCompletion()
    {
        finishRolling = false;
        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();
        if (movementStopped)
            return;
        constraintsBeforeStop = ballRigidbody.constraints;
        movementStopped = true;
        ballRigidbody.linearVelocity = Vector3.zero;
        ballRigidbody.angularVelocity = Vector3.zero;
        ballRigidbody.constraints = RigidbodyConstraints.FreezeAll;
        ballRigidbody.Sleep();
        isGrounded = false;
        groundCollider = null;
    }

    internal void BeginFinishRoll(float gateZ, float brakeSeconds)
    {
        if (ballRigidbody == null)
            ballRigidbody = GetComponent<Rigidbody>();
        if (movementStopped)
            return;
        finishRolling = true;
        finishCoastThroughZ = gateZ + 2f;
        finishRollSpeed = Mathf.Max(1f, ballRigidbody.linearVelocity.z);
        // Limit braking travel to 16 m: even the minimum authored clear runoff has room.
        finishBrakeDuration = Mathf.Max(.05f, Mathf.Min(Mathf.Max(.1f, brakeSeconds), 32f / finishRollSpeed));
        finishBrakeElapsed = 0f;
        finishAngularVelocity = ballRigidbody.angularVelocity;
        DiscardBufferedInput();
    }

    private void UpdateFinishRoll()
    {
        // Keep the incoming forward motion until the entire sphere has crossed the gate.
        if (ballRigidbody.position.z >= finishCoastThroughZ)
            finishBrakeElapsed += Time.fixedDeltaTime;
        float t = Mathf.Clamp01(finishBrakeElapsed / finishBrakeDuration);
        float remaining = 1f - Mathf.SmoothStep(0f, 1f, t);
        Vector3 velocity = ballRigidbody.linearVelocity;
        velocity.z = finishRollSpeed * remaining;
        velocity.x = Mathf.MoveTowards(velocity.x, 0f, 8f * Time.fixedDeltaTime);
        ballRigidbody.linearVelocity = velocity; // Retain vertical physics/contact with the real support floor.
        ballRigidbody.angularVelocity = finishAngularVelocity * remaining;
        if (t >= 1f)
            StopMotionForCompletion(); // Freeze only once translation and rotation have eased to zero.
    }

    public void DiscardBufferedInput()
    {
        if (gameplayInput != null)
            gameplayInput.ReadCommand();
    }

    private IGameplayInput ResolveInputProvider()
    {
        if (inputProvider is IGameplayInput selectedProvider)
        {
            return selectedProvider;
        }

        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is IGameplayInput provider)
            {
                inputProvider = behaviour;
                return provider;
            }
        }

        return null;
    }
}
