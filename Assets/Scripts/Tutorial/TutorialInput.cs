using System;
using UnityEngine;

namespace EndlessRollball.Tutorial
{
    [RequireComponent(typeof(EndlessRollball.Imu.ImuGameplayInput))]
    public sealed class TutorialInput : MonoBehaviour, IGameplayInput
    {
        private EndlessRollball.Imu.ImuGameplayInput source;
        private bool steeringAllowed, jumpAllowed;
        public float Steering { get; private set; }
        public event Action<Collider> Contact;

        private void Awake() => source = GetComponent<EndlessRollball.Imu.ImuGameplayInput>();

        public void SetAllowed(bool steering, bool jump)
        {
            steeringAllowed = steering;
            jumpAllowed = jump;
            Clear();
        }

        public void Clear()
        {
            if (source == null) source = GetComponent<EndlessRollball.Imu.ImuGameplayInput>();
            source?.ClearTransientInput();
            if (source != null) source.ReadCommand();
            Steering = 0f;
        }

        public GameplayCommand ReadCommand()
        {
            if (source == null) source = GetComponent<EndlessRollball.Imu.ImuGameplayInput>();
            if (source == null) return GameplayCommand.Neutral;
            GameplayCommand raw = source.ReadCommand();
            Steering = steeringAllowed ? raw.Steering : 0f;
            return new GameplayCommand(Steering, 0f, jumpAllowed && raw.JumpPressed,
                steeringIsPosition: steeringAllowed && raw.SteeringIsPosition);
        }

        private void OnCollisionEnter(Collision collision) => Contact?.Invoke(collision.collider);
        private void OnDisable() => Clear();
    }
}
