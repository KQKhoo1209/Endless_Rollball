using UnityEngine;

public sealed class CloudFogFollow : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float forwardOffset = 20f;

    private float fixedHeight;

    private void OnEnable()
    {
        fixedHeight = transform.position.y;
    }

    private void LateUpdate()
    {
        if (player == null) return;

        transform.position = new Vector3(
            player.position.x,
            fixedHeight,
            player.position.z + forwardOffset);
    }
}