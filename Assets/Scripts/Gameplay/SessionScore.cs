using System;

namespace EndlessRollball.Track
{
    // Immutable in-memory result. No persistence or coin balance mutation lives here.
    public sealed class SessionScore
    {
        public const string RulesId = "task-score-v2-finite-recovery-12m-approach-floor-landing-60-40";
        public int SteeringTaskCount { get; }
        public int SuccessfulSteeringTaskCount { get; }
        public int RequiredJumpOpportunityCount { get; }
        public int SuccessfulJumpLandingCount { get; }
        public decimal? SafeNavigationPercent { get; }
        public decimal? JumpAndLandingPercent { get; }
        public decimal? OverallPercent { get; }
        public int PotentialAvatarCoins { get; }

        public SessionScore(int navigationRequired, int navigationSucceeded,
            int jumpsRequired, int jumpsSucceeded)
        {
            if (navigationRequired < 0 || jumpsRequired < 0 || navigationSucceeded < 0 ||
                jumpsSucceeded < 0 || navigationSucceeded > navigationRequired || jumpsSucceeded > jumpsRequired)
                throw new ArgumentOutOfRangeException(nameof(navigationRequired), "Invalid task counts.");
            SteeringTaskCount = navigationRequired;
            SuccessfulSteeringTaskCount = navigationSucceeded;
            RequiredJumpOpportunityCount = jumpsRequired;
            SuccessfulJumpLandingCount = jumpsSucceeded;
            SafeNavigationPercent = navigationRequired > 0
                ? 100m * navigationSucceeded / navigationRequired : (decimal?)null;
            JumpAndLandingPercent = jumpsRequired > 0
                ? 100m * jumpsSucceeded / jumpsRequired : (decimal?)null;
            OverallPercent = !SafeNavigationPercent.HasValue ? (decimal?)null
                : JumpAndLandingPercent.HasValue
                    ? .60m * SafeNavigationPercent.Value + .40m * JumpAndLandingPercent.Value
                    : SafeNavigationPercent;
            PotentialAvatarCoins = OverallPercent.HasValue
                ? (int)decimal.Ceiling(OverallPercent.Value / 2m) : 0;
        }
    }
}
