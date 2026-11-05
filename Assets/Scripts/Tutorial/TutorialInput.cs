using System;
using UnityEngine;

namespace EndlessRollball.Tutorial
{
    [RequireComponent(typeof(KeyboardGameplayInput))]
    public sealed class TutorialInput : MonoBehaviour, IGameplayInput
    {
        private KeyboardGameplayInput source;
        private bool steeringAllowed, jumpAllowed;
        public float Steering { get; private set; }
        public event Action<Collider> Contact;

        private void Awake() => source = GetComponent<KeyboardGameplayInput>();

        public void SetAllowed(bool steering, bool jump)
        {
            steeringAllowed = steering;
            jumpAllowed = jump;
            Clear();
        }

        public void Clear()
        {
            if (source == null) source = GetComponent<KeyboardGameplayInput>();
            if (source != null) source.ReadCommand();
            Steering = 0f;
        }

        public GameplayCommand ReadCommand()
        {
            if (source == null) source = GetComponent<KeyboardGameplayInput>();
            if (source == null) return GameplayCommand.Neutral;
            GameplayCommand raw = source.ReadCommand();
            Steering = steeringAllowed ? raw.Steering : 0f;
            return new GameplayCommand(Steering, 0f, jumpAllowed && raw.JumpPressed);
        }

        private void OnCollisionEnter(Collision collision) => Contact?.Invoke(collision.collider);
        private void OnDisable() => Clear();
    }
}
