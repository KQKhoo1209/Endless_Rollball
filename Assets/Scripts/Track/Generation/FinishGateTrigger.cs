using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class FinishGateTrigger : MonoBehaviour
    {
        private BoxCollider trigger;
        private Rigidbody expectedPlayerBody;
        private Action onFinish;
        private bool armed;

        private void Awake()
        {
            Disarm();
        }

        public bool Arm(BallMovement player, Action finishCallback)
        {
            Disarm();
            trigger = GetComponent<BoxCollider>();
            Rigidbody body = player != null
                ? player.GetComponent<Rigidbody>() : null;

            if (body == null || finishCallback == null ||
                trigger == null || !trigger.isTrigger)
            {
                Debug.LogError(
                    "FinishGateTrigger needs the assigned player, " +
                    "a callback and an Is Trigger BoxCollider.", this);
                return false;
            }

            expectedPlayerBody = body;
            onFinish = finishCallback;
            armed = true;
            trigger.enabled = true;
            return true;
        }

        public void Disarm()
        {
            armed = false;
            onFinish = null;
            expectedPlayerBody = null;

            if (trigger == null)
                trigger = GetComponent<BoxCollider>();
            if (trigger != null)
                trigger.enabled = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!armed || other.attachedRigidbody != expectedPlayerBody)
                return;

            // Latch and disable BEFORE notifying the run controller.
            // Multiple player colliders cannot complete the run twice.
            Action callback = onFinish;
            Disarm();
            callback?.Invoke();
        }

        private void OnDisable()
        {
            Disarm();
        }
    }
}