using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(KeyboardGameplayInput))]
public sealed class BallMovement : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Must implement IGameplayInput. Leave empty to use the first provider on this object.")]
    [SerializeField] private MonoBehaviour inputProvider;

    [Header("Steering")]
    [SerializeField, Min(0f)] private float steeringAcceleration = 22f;
    [SerializeField, Min(0f)] private float maxLateralSpeed = 7f;
    [SerializeField, Min(0f)] private float steeringResponse = 8f;

    [Header("Forward Speed")]
    [SerializeField, Min(0f)] private float baseForwardSpeed = 7f;
    [SerializeField, Min(0f)] private float speedAdjustmentRange = 4f;
    [SerializeField, Min(0f)] private float forwardAcceleration = 18f;
    [SerializeField, Min(0f)] private float forwardResponse = 4f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpSpeed = 6f;
    [SerializeField, Range(0f, 1f)] private float minimumGroundNormal = 0.55f;
    [SerializeField, Min(0f)] private float jumpCooldown = 0.25f;

    private Rigidbody ballRigidbody;
    private IGameplayInput gameplayInput;
    private bool isGrounded;
    private float lastJumpTime = float.NegativeInfinity;

    private void Awake()
    {
        ballRigidbody = GetComponent<Rigidbody>();
        gameplayInput = ResolveInputProvider();

        if (gameplayInput == null)
        {
            Debug.LogError(
                $"{nameof(BallMovement)} needs a component that implements {nameof(IGameplayInput)}.",
                this);
            enabled = false;
        }
    }

    private void FixedUpdate()
    {
        GameplayCommand command = gameplayInput.ReadCommand();

        ApplySteering(command.Steering);
        ApplyForwardSpeed();
        TryJump(command.JumpPressed);

        isGrounded = false;
    }

    private void ApplySteering(float steering)
    {
        float targetLateralSpeed = steering * maxLateralSpeed;
        float currentLateralSpeed = ballRigidbody.linearVelocity.x;
        float speedError = targetLateralSpeed - currentLateralSpeed;

        float acceleration = Mathf.Clamp(
            speedError * steeringResponse,
            -steeringAcceleration,
            steeringAcceleration);

        ballRigidbody.AddForce(
            Vector3.right * acceleration,
            ForceMode.Acceleration);
    }

    private void ApplyForwardSpeed()
    {
        float targetSpeed = baseForwardSpeed;

        float speedError =
            targetSpeed - ballRigidbody.linearVelocity.z;

        float acceleration = Mathf.Clamp(
            speedError * forwardResponse,
            -forwardAcceleration,
            forwardAcceleration);

        ballRigidbody.AddForce(
            Vector3.forward * acceleration,
            ForceMode.Acceleration);
    }

    private void TryJump(bool jumpPressed)
    {
        if (!jumpPressed || !isGrounded || Time.time < lastJumpTime + jumpCooldown)
        {
            return;
        }

        Vector3 velocity = ballRigidbody.linearVelocity;
        velocity.y = Mathf.Max(0f, velocity.y);
        ballRigidbody.linearVelocity = velocity;
        ballRigidbody.AddForce(Vector3.up * jumpSpeed, ForceMode.VelocityChange);

        lastJumpTime = Time.time;
        isGrounded = false;
    }

    private void OnCollisionStay(Collision collision)
    {
        for (int index = 0; index < collision.contactCount; index++)
        {
            if (collision.GetContact(index).normal.y >= minimumGroundNormal)
            {
                isGrounded = true;
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
        {
            ballRigidbody = GetComponent<Rigidbody>();
        }

        ballRigidbody.position = worldPosition;
        ballRigidbody.rotation = worldRotation;
        ballRigidbody.linearVelocity = Vector3.zero;
        ballRigidbody.angularVelocity = Vector3.zero;
        ballRigidbody.WakeUp();
        isGrounded = false;
        lastJumpTime = float.NegativeInfinity;
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
