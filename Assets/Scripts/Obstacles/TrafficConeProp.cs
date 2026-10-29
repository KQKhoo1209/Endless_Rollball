using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TrafficConeProp : MonoBehaviour
    {
        private Rigidbody body;
        private Vector3 initialLocalPosition;
        private Quaternion initialLocalRotation;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            initialLocalPosition = transform.localPosition;
            initialLocalRotation = transform.localRotation;
        }

        private void OnEnable()
        {
            Restore();
        }

        private void OnDisable()
        {
            Restore();
        }

        private void Restore()
        {
            if (body == null)
                return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            transform.SetLocalPositionAndRotation(
                initialLocalPosition,
                initialLocalRotation);

            body.position = transform.position;
            body.rotation = transform.rotation;
            body.Sleep();
        }
    }
}