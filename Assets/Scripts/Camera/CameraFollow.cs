using UnityEngine;

public sealed class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 4f, -7f);
    [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.2f;

    [Header("Rotation")]
    [SerializeField] private float lookHeight = 1f;
    [SerializeField, Min(0f)] private float rotationSpeed = 8f;

    private Vector3 followVelocity;
    [SerializeField, Min(0f)] private float lookAheadDistance = 5f;

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = target.position + offset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            positionSmoothTime);

        Vector3 lookTarget =
            target.position
            + Vector3.up * lookHeight
            + Vector3.forward * lookAheadDistance;
        Vector3 lookDirection = lookTarget - transform.position;

        if (lookDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion desiredRotation = Quaternion.LookRotation(
            lookDirection,
            Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            rotationSpeed * Time.deltaTime);
    }
}