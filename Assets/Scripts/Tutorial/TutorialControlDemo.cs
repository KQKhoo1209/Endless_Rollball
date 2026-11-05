using UnityEngine;

namespace EndlessRollball.Tutorial
{
    public enum TutorialDemonstration { None, Left, Right, Centre, Jump, Obstacle, Combined }

    // Animator owns the actual scene ball only while the player watches a demonstration.
    public sealed class TutorialControlDemo : MonoBehaviour
    {
        private Animator animator;
        private BallMovement ball;
        private Rigidbody body;
        private CameraFollow cameraFollow;
        private Vector3 savedPosition;
        private Quaternion savedRotation;
        private bool savedKinematic, savedCollisions, savedMovement, savedCamera;
        public bool IsPlaying { get; private set; }

        public void Configure(RuntimeAnimatorController controller, BallMovement player, CameraFollow camera, Transform root)
        {
            ball = player;
            body = ball.GetComponent<Rigidbody>();
            cameraFollow = camera;
            ball.transform.SetParent(root, true);
            ball.name = "Tutorial Ball";
            animator = root.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = false;
        }

        public void Play(TutorialDemonstration value)
        {
            Stop();
            if (value == TutorialDemonstration.None || animator == null) return;
            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogError("Assign the tutorial demonstration Animator Controller on Tutorial Controller.", this);
                return;
            }
            savedPosition = ball.transform.position;
            savedRotation = ball.transform.rotation;
            savedKinematic = body.isKinematic;
            savedCollisions = body.detectCollisions;
            savedMovement = ball.enabled;
            savedCamera = cameraFollow.enabled;
            ball.StopMotionForCompletion();
            ball.DiscardBufferedInput();
            ball.enabled = false;
            body.isKinematic = true;
            body.detectCollisions = false;
            // Keep the camera still so lateral movement is visible in the Street scene.
            if (value == TutorialDemonstration.Obstacle)
            {
                ball.transform.position = new Vector3(0, .55f, 9);
                cameraFollow.SnapToTarget();
            }
            cameraFollow.enabled = false;
            IsPlaying = true;
            animator.enabled = true;
            animator.speed = 1;
            animator.Play("Base Layer." + value, 0, 0);
        }

        public void SetPaused(bool paused) { if (animator != null) animator.speed = paused ? 0 : 1; }

        private void Stop()
        {
            if (animator != null) animator.enabled = false;
            if (!IsPlaying || ball == null) return;
            IsPlaying = false;
            body.isKinematic = savedKinematic;
            body.detectCollisions = savedCollisions;
            ball.enabled = savedMovement;
            ball.ResetMotion(savedPosition, savedRotation);
            ball.StopMotionForCompletion();
            ball.DiscardBufferedInput();
            if (cameraFollow != null)
            {
                cameraFollow.enabled = savedCamera;
                cameraFollow.SnapToTarget();
            }
        }

        private void OnDestroy() => Stop();
    }
}
