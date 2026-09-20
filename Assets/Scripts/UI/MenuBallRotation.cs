using UnityEngine;

public sealed class MenuBallRotation : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = -90f;

    private void Update()
    {
        transform.Rotate(
            0f,
            0f,
            rotationSpeed * Time.unscaledDeltaTime);
    }
}