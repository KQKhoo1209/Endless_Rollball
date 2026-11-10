using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BallMovement))]
    public sealed class PlayerHazardContact : MonoBehaviour
    {
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private ObstacleManager obstacleManager;
        [SerializeField] private StreetImpactAudio impactAudio;
        [SerializeField] private BeachGameplayAudio beachAudio;

        public bool IsConfiguredFor(GameplayRunController controller, BallMovement ball)
        {
            return isActiveAndEnabled && runController == controller &&
                ball != null && ball.gameObject == gameObject &&
                obstacleManager != null && obstacleManager.isActiveAndEnabled;
        }

        private void OnCollisionEnter(Collision collision)
        {
            Collider shape = collision.collider;

            // Cones are harmless props and intentionally excluded from active hazards.
            if (runController != null && runController.State == SessionFlowState.Running &&
                obstacleManager != null && obstacleManager.OwnsStreetConeCollider(shape))
            {
                impactAudio?.PlayCone(shape);
                return;
            }

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
                if (runController.State == SessionFlowState.Running) impactAudio?.PlayCargo(shape);
                runController.ReportCargoContact(shape);
                return;
            }

            // Play before failure handling changes state or starts recovery.
            PlayDungeonContact(shape);
            if (runController.State == SessionFlowState.Running && obstacleManager.IsStreetVehicleCollider(shape))
                impactAudio?.PlayCar(shape);
            else if (runController.State == SessionFlowState.Running && obstacleManager.IsStreetBarrierCollider(shape))
                impactAudio?.PlayFence(shape);
            else if (runController.State == SessionFlowState.Running && obstacleManager.IsBeachWoodCollider(shape))
                beachAudio?.PlayWood();
            runController.ReportObstacleCollision(shape);
        }

        private void PlayDungeonContact(Collider shape)
        {
            if (runController == null || runController.State != SessionFlowState.Running) return;
            var trap = shape.GetComponentInParent<DungeonTrapEncounter>();
            if (trap != null && trap.Hazard == shape)
            {
                if (trap.Kind == DungeonTrapEncounter.TrapKind.Fire)
                    impactAudio?.PlayDungeonBurn(shape);
                else if (trap.Kind != DungeonTrapEncounter.TrapKind.SlidingBlock)
                    impactAudio?.PlayDungeonRock(shape);
            }
            else if (shape.GetComponentInParent<DungeonRollingStoneBall>() != null)
                impactAudio?.PlayDungeonRock(shape);
        }

        private void OnTriggerEnter(Collider other)
        {
            var dungeon=other.GetComponentInParent<DungeonTrapEncounter>();
            if(dungeon!=null && dungeon.Hazard==other && runController!=null && obstacleManager!=null && obstacleManager.OwnsActiveCollider(other))
            {
                PlayDungeonContact(other);
                runController.ReportObstacleCollision(other);
                return;
            }
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
                if (runController.State == SessionFlowState.Running) impactAudio?.PlayFence(other);
                runController.ReportObstacleCollision(other);
            }
        }
    }
}
