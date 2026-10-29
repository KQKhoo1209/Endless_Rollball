using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class CombinedObstacleEncounter : MonoBehaviour
    {
        [SerializeField]
        private string familyId = "Street.FenceCargo";

        [Tooltip("Only the fence colliders that require jumping.")]
        [SerializeField]
        private GameObject jumpHazards;

        [Tooltip("The lorry and dangerous cargo colliders.")]
        [SerializeField]
        private GameObject navigationHazards;

        [Tooltip("Local X of the intended route over the fence.")]
        [SerializeField]
        private float jumpPassageX;

        [Tooltip("Allowed offsets for the complete encounter, including both hazard groups.")]
        [SerializeField] private float[] allowedLateralOffsets = { 0f };
        public float[] AllowedLateralOffsets => allowedLateralOffsets;
        public string FamilyId => familyId;
        public GameObject JumpHazards => jumpHazards;
        public GameObject NavigationHazards => navigationHazards;
        public float JumpPassageX => jumpPassageX;

        private Rigidbody[] bodies;
        private Vector3[] initialPositions;
        private Quaternion[] initialRotations;

        private void Awake()
        {
            bodies = navigationHazards != null
                ? navigationHazards.GetComponentsInChildren<Rigidbody>(true)
                : System.Array.Empty<Rigidbody>();

            initialPositions = new Vector3[bodies.Length];
            initialRotations = new Quaternion[bodies.Length];

            for (int i = 0; i < bodies.Length; i++)
            {
                initialPositions[i] = bodies[i].transform.localPosition;
                initialRotations[i] = bodies[i].transform.localRotation;
            }
        }

        private void OnEnable()
        {
            RestoreCargo();

            if (bodies == null)
                return;

            foreach (Rigidbody body in bodies)
                if (body != null)
                    body.WakeUp();
        }

        private void OnDisable()
        {
            RestoreCargo();
        }

        private void RestoreCargo()
        {
            if (bodies == null)
                return;

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];

                if (body == null)
                    continue;

                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;

                body.transform.SetLocalPositionAndRotation(
                    initialPositions[i],
                    initialRotations[i]);

                body.position = body.transform.position;
                body.rotation = body.transform.rotation;
            }
        }
    }
}