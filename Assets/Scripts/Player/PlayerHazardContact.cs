using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallMovement))]
    public sealed class PlayerHazardContact : MonoBehaviour
    {
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private ObstacleManager obstacleManager;

        public bool IsConfiguredFor(GameplayRunController controller, BallMovement ball)
        {
            return isActiveAndEnabled && runController == controller &&
                ball != null && ball.gameObject == gameObject &&
                obstacleManager != null && obstacleManager.isActiveAndEnabled;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (runController != null && obstacleManager != null &&
                obstacleManager.OwnsActiveCollider(collision.collider))
                runController.ReportObstacleCollision(collision.collider);
        }
    }
}
