using UnityEngine;

namespace EndlessRollball
{
    public sealed class PhoenixFlightVisual : MonoBehaviour
    {
        [SerializeField] private Transform model;
        [SerializeField] private Transform leftWing;
        [SerializeField] private Transform rightWing;
        [SerializeField] private Transform leftShoulder;
        [SerializeField] private Transform rightShoulder;
        [SerializeField] private Renderer body;
        [SerializeField, Min(.1f)] private float wingspan = 1.4f;
        [SerializeField] private float flapDegrees = 22f;
        [SerializeField] private float flapSeconds = .75f;
        [SerializeField, Min(0f)] private float bodyBob = .1f;
        [SerializeField, Range(0f, 30f)] private float maximumYaw = 18f;
        [SerializeField, Range(0f, 15f)] private float maximumBank = 6f;
        [SerializeField, Min(.1f)] private float turnResponse = 6f;
        private bool prepared;
        private Quaternion leftRest, rightRest, restRotation;
        private Vector3 restPosition;
        private float reactionUntil;
        private float animationTime;
        private float targetSteering, visualSteering;
        private float leftSide, rightSide;

        public void PrepareModel()
        {
            if (prepared || model == null || leftWing == null || rightWing == null ||
                leftShoulder == null || rightShoulder == null || body == null) return;
            // Normalise only this event's model instance, preserving imported assets.
            model.localPosition = Vector3.zero;
            model.localScale = Vector3.one;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float span = Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.z));
            model.localScale *= wingspan / span;
            bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            Vector3 desired = transform.TransformPoint(new Vector3(0f, .8f, 0f));
            model.position += desired - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            // Shoulder joints are authored at the mesh roots. Do not refit them
            // from renderer bounds, which would move the hinge away from the body.
            leftRest = leftShoulder.localRotation;
            rightRest = rightShoulder.localRotation;
            leftSide = ShoulderSide(leftShoulder);
            rightSide = ShoulderSide(rightShoulder);
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            prepared = true;
        }

        private float ShoulderSide(Transform shoulder)
        {
            return Mathf.Sign(Vector3.Dot(shoulder.position - body.bounds.center, shoulder.right));
        }

        public void SetSteering(float steering) => targetSteering = Mathf.Clamp(steering, -1f, 1f);

        public void React() => reactionUntil = Time.time + .6f;

        private void LateUpdate()
        {
            if (!prepared) PrepareModel();
            if (!prepared) return;
            animationTime += Time.deltaTime;
            float phase = animationTime * (Mathf.PI * 2f / Mathf.Max(.1f, flapSeconds));
            float wave = Mathf.Sin(phase);
            leftShoulder.localRotation = leftRest * Quaternion.AngleAxis(-leftSide * wave * flapDegrees, Vector3.forward);
            rightShoulder.localRotation = rightRest * Quaternion.AngleAxis(-rightSide * wave * flapDegrees, Vector3.forward);
            visualSteering = Mathf.Lerp(visualSteering, targetSteering,
                1f - Mathf.Exp(-turnResponse * Time.deltaTime));
            bool hit = Time.time < reactionUntil;
            // Lift follows the downstroke. The controller follows the animated
            // leg anchor afterward; the phoenix collision body stays level.
            transform.localPosition = restPosition + Vector3.up * (Mathf.Sin(phase - .5f) * bodyBob);
            float reactionRoll = hit ? Mathf.Sin(Time.time * 28f) * 9f : 0f;
            transform.localRotation = restRotation * Quaternion.Euler(0f,
                visualSteering * maximumYaw, -visualSteering * maximumBank + reactionRoll);
        }

        private void OnDisable()
        {
            reactionUntil = 0f;
            animationTime = 0f;
            targetSteering = visualSteering = 0f;
            if (!prepared) return;
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
            leftShoulder.localRotation = leftRest;
            rightShoulder.localRotation = rightRest;
        }
    }
}
