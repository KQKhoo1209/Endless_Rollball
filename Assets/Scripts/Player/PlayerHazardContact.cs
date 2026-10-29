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
            Collider shape = collision.collider;

            if (runController == null ||
                obstacleManager == null ||
                !obstacleManager.OwnsActiveCollider(shape))
            {
                return;
            }

            CombinedObstacleEncounter combined =
                shape.GetComponentInParent<CombinedObstacleEncounter>();

            Rigidbody body = shape.attachedRigidbody;

            bool isCargo =
                combined != null &&
                combined.NavigationHazards != null &&
                body != null &&
                !body.isKinematic &&
                body.transform.IsChildOf(
                    combined.NavigationHazards.transform);

            if (isCargo)
            {
                runController.ReportCargoContact(shape);
                return;
            }

            runController.ReportObstacleCollision(shape);
        }

        private void OnTriggerEnter(Collider other)
        {
            var dungeon=other.GetComponentInParent<DungeonTrapEncounter>();
            if(dungeon!=null && dungeon.Hazard==other && runController!=null && obstacleManager!=null && obstacleManager.OwnsActiveCollider(other))
            { runController.ReportObstacleCollision(other); return; }
            var beach=other.GetComponentInParent<BeachBarrierEncounter>();
            if(beach!=null && beach.IsFallSensor(other) && obstacleManager!=null &&
                obstacleManager.OwnsBeachBarrier(beach) && runController!=null)
            { runController.ReportEncounterFall(); return; }
            PalmObstacle palm = other.GetComponentInParent<PalmObstacle>();
            if (runController != null && runController.State == SessionFlowState.Running &&
                obstacleManager != null && palm != null && palm.IsLeafSensor(other) &&
                obstacleManager.OwnsActiveCollider(other))
            {
                runController.ReportNavigationContact(other);
                palm.TouchLeaves(other.ClosestPoint(transform.position), transform.forward);
                return;
            }
            if (runController != null &&
                obstacleManager != null &&
                obstacleManager.OwnsRoadworksCollider(other))
            {
                runController.ReportObstacleCollision(other);
            }
        }
    }
}
