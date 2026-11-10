using EndlessRollball.Track;

namespace EndlessRollball.Avatars
{
    public static class SessionFeedbackRules
    {
        public static string Message(SessionEndInfo info, bool reduced)
        {
            bool pain = GameSessionSettings.PainRating.GetValueOrDefault() >= 4;
            bool stress = GameSessionSettings.StressRating.GetValueOrDefault() >= 4;
            if (info.Outcome == SessionOutcome.Stopped) return "Session ended. Thank you for taking part. You may rest now.";
            bool complete = info.Outcome == SessionOutcome.Completed;
            if (pain && stress) return complete ? "Session complete. Take time to rest before another activity." : "The session has stopped. Rest before deciding what to do next.";
            if (pain) return complete ? "Session complete. Take a moment to rest." : "The session has stopped. Take a rest; there is no need to retry.";
            if (stress) return complete ? "Session complete. Thank you for taking part." : "It is okay to stop here. You can return when you feel ready.";
            if (reduced) return complete ? "Session complete. Thank you for taking part. You may rest now." : "The session has stopped. Thank you for taking part. You may rest now.";
            var s = info.Score;
            if (!s.OverallPercent.HasValue) return "Thank you for taking part. There is not enough session data for performance feedback. You may rest now.";
            string achievement = s.SuccessfulSteeringTaskCount > 0 ? "You completed steering tasks successfully." : s.SuccessfulJumpLandingCount > 0 ? "You completed a jump and landing successfully." : "Thank you for practising today.";
            bool jump = s.JumpAndLandingPercent.HasValue && s.JumpAndLandingPercent.Value < s.SafeNavigationPercent.Value;
            if (s.SafeNavigationPercent == 100m && (!s.JumpAndLandingPercent.HasValue || s.JumpAndLandingPercent == 100m))
                return achievement + "\nYou were consistent across the available tasks.\nNext time, keep movements comfortable and steady.";
            return achievement + (jump
                ? "\nPractice point: landing after jumps.\nNext time, when comfortable, focus on one jump and landing at a time."
                : "\nPractice point: steering through safe spaces.\nNext time, when comfortable, focus on one gentle steering movement at a time.");
        }
    }
}
