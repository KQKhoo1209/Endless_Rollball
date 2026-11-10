using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    [DisallowMultipleComponent]
    public sealed class BallLandingAudio : MonoBehaviour
    {
        [SerializeField] private BallMovement ballMovement;
        [SerializeField] private GameplayRunController runController;
        [SerializeField] private ObstacleManager obstacleManager;
        [SerializeField] private StreetImpactAudio impactAudio;
        [SerializeField, Min(.02f)] private float minimumAirTime = .08f;
        private bool touchedRoad;
        private float airborneSince = -1f;

        private void OnEnable()
        {
            if (ballMovement == null) return;
            ballMovement.AirborneObserved += ObserveAirborne;
            ballMovement.GroundContact += Land;
        }

        private void ObserveAirborne()
        {
            if (runController == null || runController.State != SessionFlowState.Running)
            {
                ResetContact();
                return;
            }
            if (touchedRoad && airborneSince < 0f) airborneSince = Time.time;
        }

        private void Land(Collider ground)
        {
            if (runController == null || runController.State != SessionFlowState.Running)
            {
                ResetContact();
                return;
            }
            // Ordinary road landings only; cloud piano and obstacle hits have their own audio.
            if (touchedRoad && airborneSince < 0f) return;
            if (ground == null || ground.GetComponentInParent<TrackSegment>() == null ||
                (obstacleManager != null && obstacleManager.OwnsActiveCollider(ground))) return;
            if (touchedRoad && airborneSince >= 0f && Time.time - airborneSince >= minimumAirTime)
                impactAudio?.PlayLanding(ground);
            touchedRoad = true;
            airborneSince = -1f;
        }

        private void LateUpdate()
        {
            if (runController == null || runController.State != SessionFlowState.Running) ResetContact();
        }

        private void ResetContact()
        {
            touchedRoad = false;
            airborneSince = -1f;
        }

        private void OnDisable()
        {
            if (ballMovement != null)
            {
                ballMovement.AirborneObserved -= ObserveAirborne;
                ballMovement.GroundContact -= Land;
            }
            ResetContact();
        }
    }
}
