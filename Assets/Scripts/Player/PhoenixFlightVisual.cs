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
        private bool prepared;
        private Quaternion leftRest, rightRest;
        private Vector3 restPosition;
        private float reactionUntil;

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
            FitShoulder(leftWing, leftShoulder);
            FitShoulder(rightWing, rightShoulder);
            leftRest = leftShoulder.localRotation;
            rightRest = rightShoulder.localRotation;
            restPosition = transform.localPosition;
            prepared = true;
        }

        private void FitShoulder(Transform wing, Transform pivot)
        {
            Renderer renderer = wing.GetComponent<Renderer>();
            if (renderer == null) return;
            Vector3 wingPosition = wing.position;
            Quaternion rotation = wing.rotation;
            Bounds b = renderer.bounds;
            float x = Mathf.Abs(b.min.x - body.bounds.center.x) < Mathf.Abs(b.max.x - body.bounds.center.x)
                ? b.min.x : b.max.x;
            pivot.position = new Vector3(x, b.center.y, b.center.z);
            wing.SetPositionAndRotation(wingPosition, rotation);
        }

        public void React() => reactionUntil = Time.time + .6f;

        private void LateUpdate()
        {
            if (!prepared) PrepareModel();
            if (!prepared) return;
            float wave = Mathf.Sin(Time.time * (Mathf.PI * 2f / Mathf.Max(.1f, flapSeconds)));
            leftShoulder.localRotation = leftRest * Quaternion.AngleAxis(wave * flapDegrees, Vector3.forward);
            rightShoulder.localRotation = rightRest * Quaternion.AngleAxis(-wave * flapDegrees, Vector3.forward);
            bool hit = Time.time < reactionUntil;
            transform.localPosition = restPosition + Vector3.up * (wave * .06f);
            transform.localRotation = Quaternion.Euler(0f, 0f, hit ? Mathf.Sin(Time.time * 28f) * 9f : 0f);
        }

        private void OnDisable()
        {
            reactionUntil = 0f;
            if (!prepared) return;
            transform.localPosition = restPosition;
            transform.localRotation = Quaternion.identity;
            leftShoulder.localRotation = leftRest;
            rightShoulder.localRotation = rightRest;
        }
    }
}
