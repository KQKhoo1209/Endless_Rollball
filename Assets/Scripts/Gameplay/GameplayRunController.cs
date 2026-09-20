using UnityEngine;
using UnityEngine.InputSystem;

namespace EndlessRollball.Track
{
    public sealed class GameplayRunController : MonoBehaviour
    {
        [SerializeField] private BallMovement ballMovement;
        [SerializeField] private TrackGenerator trackGenerator;
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 0.75f, 0f);
        [SerializeField] private float fallThreshold = -5f;

        private void Update()
        {
            if (ballMovement == null)
            {
                return;
            }

            bool restartPressed = Keyboard.current?.rKey.wasPressedThisFrame ?? false;
            if (restartPressed || ballMovement.transform.position.y < fallThreshold)
            {
                RestartRun();
            }
        }

        public void RestartRun()
        {
            trackGenerator?.ResetRun();
            ballMovement.ResetMotion(spawnPosition, Quaternion.identity);
        }

        public void ConfigureForScene(
            BallMovement configuredBallMovement,
            TrackGenerator configuredTrackGenerator,
            Vector3 configuredSpawnPosition,
            float configuredFallThreshold)
        {
            ballMovement = configuredBallMovement;
            trackGenerator = configuredTrackGenerator;
            spawnPosition = configuredSpawnPosition;
            fallThreshold = configuredFallThreshold;
        }
    }
}
