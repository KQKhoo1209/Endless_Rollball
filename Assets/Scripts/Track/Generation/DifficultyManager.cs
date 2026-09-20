using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed class DifficultyManager : MonoBehaviour
    {
        [SerializeField] private DifficultyMode initialMode = DifficultyMode.Easy;
        [SerializeField] private DifficultyProfile[] profiles = Array.Empty<DifficultyProfile>();
        [SerializeField] private Transform player;
        [SerializeField] private BallMovement ballMovement;

        private DifficultyProfile currentProfile;
        private DifficultySnapshot currentSnapshot;
        private float lastAppliedSpeed = float.NaN;

        public TrackRunConfig CurrentRun { get; private set; }
        public DifficultySnapshot CurrentSnapshot => currentSnapshot;
        public bool IsConfigured => currentProfile != null && CurrentRun.IsValid;

        private void Awake()
        {
            if (profiles.Length == 0)
            {
                Debug.LogError(
                    "Assign difficulty profiles to DifficultyManager.", this);
                return;
            }

            DifficultyMode selectedDifficulty =
                GameSessionSettings.TrackDifficulty;

            Configure(selectedDifficulty);

            Debug.Log(
                $"Gameplay setup: {GameSessionSettings.SelectedMode} / " +
                $"{selectedDifficulty}", this);
        }

        private void Update()
        {
            if (!IsConfigured || player == null)
            {
                return;
            }

            UpdateDistance(Mathf.Max(0f, player.position.z));
        }

        public TrackRunConfig Configure(DifficultyMode mode, int? seedOverride = null)
        {
            currentProfile = FindProfile(mode);

            if (currentProfile == null)
            {
                Debug.LogError($"No difficulty profile is configured for {mode}.", this);
                CurrentRun = default;
                return CurrentRun;
            }

            initialMode = mode;
            CurrentRun = currentProfile.CreateRunConfig(seedOverride);
            UpdateDistance(0f);
            return CurrentRun;
        }

        public DifficultySnapshot UpdateDistance(float distance)
        {
            if (currentProfile == null)
            {
                return default;
            }

            currentSnapshot = currentProfile.Evaluate(distance);

            if (ballMovement != null && !Mathf.Approximately(lastAppliedSpeed, currentSnapshot.ForwardSpeed))
            {
                ballMovement.SetForwardSpeed(
                    currentSnapshot.ForwardSpeed,
                    currentSnapshot.SpeedAdjustmentRange);
                lastAppliedSpeed = currentSnapshot.ForwardSpeed;
            }

            return currentSnapshot;
        }

        public void ConfigureForScene(
            Transform configuredPlayer,
            BallMovement configuredBallMovement,
            DifficultyProfile[] configuredProfiles,
            DifficultyMode configuredInitialMode)
        {
            player = configuredPlayer;
            ballMovement = configuredBallMovement;
            profiles = configuredProfiles ?? Array.Empty<DifficultyProfile>();
            initialMode = configuredInitialMode;
        }

        private DifficultyProfile FindProfile(DifficultyMode mode)
        {
            foreach (DifficultyProfile profile in profiles)
            {
                if (profile != null && profile.Mode == mode)
                {
                    return profile;
                }
            }

            return null;
        }
    }
}
