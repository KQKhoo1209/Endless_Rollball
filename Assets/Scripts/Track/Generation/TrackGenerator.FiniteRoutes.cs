using System;
using System.Globalization;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class TrackGenerator
    {
        [Header("Finite routes")]
        [SerializeField, Min(1f)]
        private float standardTargetDurationSeconds = 360f;
        [SerializeField, Min(1f)]
        private float fastPacedTargetDurationSeconds = 180f;
        [Tooltip("Hazards and required landings must end this far before the finish gate.")]
        [SerializeField, Min(1f)]
        private float finishClearance = 6f;
        [Tooltip("Clear 24 m tracks AFTER the gate-containing track.")]
        [SerializeField, Min(1)]
        private int finishRunoffSegments = 2;
        [Tooltip("Minimum ordinary tracks in the final theme before the gate segment.")]
        [SerializeField, Min(1)]
        private int minimumFinalThemeSegments = 6;
        private bool hasFiniteRoute;
        private int finishSegmentIndex;
        private int finiteSegmentLimit;
        private float finishLineZ;
        private float plannedRouteDurationSeconds;
        private string finiteRouteIdentifier;
        public bool HasFiniteRoute => hasFiniteRoute;
        // Only read FinishLineZ after checking HasFiniteRoute.
        // Used to position the single finite finish gate.
        public float FinishLineZ => hasFiniteRoute ? finishLineZ : float.NaN;
        public float PlannedRouteDurationSeconds => hasFiniteRoute ? plannedRouteDurationSeconds : 0f;

        private bool TryConfigureFiniteRoute(GameMode mode)
        {
            // Reset on every initialization/retry, including Endless.
            hasFiniteRoute = false;
            finishSegmentIndex = -1;
            finiteSegmentLimit = 0;
            finishLineZ = float.NaN;
            plannedRouteDurationSeconds = 0f;
            finiteRouteIdentifier = string.Empty;
            bool endlessMode = mode == GameMode.Endless;
            bool endlessProfile = currentRun.Mode == DifficultyMode.Endless;
            if (endlessMode != endlessProfile)
                return FiniteRouteError("Endless mode and the Endless profile must be " + "selected together.");
            if (endlessMode)
                return true; // No finite cap or finish in Endless.
            if (mode != GameMode.Standard && mode != GameMode.FastPaced)
                return FiniteRouteError("Unsupported finite game mode.");
            if (player == null)
                return FiniteRouteError("Assign the player Transform.");
            if (difficultyManager == null || !difficultyManager.IsConfigured || difficultyManager.CurrentRun.Mode != currentRun.Mode)
                return FiniteRouteError("Assign a configured DifficultyManager matching " + "the generator's current difficulty.");
            float targetSeconds = mode == GameMode.FastPaced ? fastPacedTargetDurationSeconds : standardTargetDurationSeconds;
            float playerStartZ = player.position.z;
            float speed = difficultyManager.UpdateDistance(0f).ForwardSpeed;
            if (!IsFiniteRouteNumber(startZ) || !IsFiniteRouteNumber(playerStartZ) || !IsFiniteRouteNumber(targetSeconds) || targetSeconds <= 0f || !IsFiniteRouteNumber(speed) || speed <= 0f || finishClearance < 1f || finishRunoffSegments < 1 || minimumFinalThemeSegments < 1)
                return FiniteRouteError("Use finite positive duration/speed, finite positions, " + "and at least one approach, runoff and final-theme slot.");
            int finalZoneStart = environmentManager != null ? environmentManager.FinalFiniteZoneStartIndex : 0;
            if (finalZoneStart < 0)
                return FiniteRouteError("The environment schedule has no finite final zone.");
            // The gate is at the CENTRE of one complete 24 m track.
            // Round upward so the nominal duration is not shortened.
            if (!FiniteRoutePlanner.TryPlan(startZ, playerStartZ, speed, targetSeconds, finalZoneStart, minimumFinalThemeSegments, 0, finishRunoffSegments, out FiniteRoutePlanner.Plan plan, out string reason))
                return FiniteRouteError(reason);
            finishSegmentIndex = plan.GateIndex;
            finiteSegmentLimit = plan.SegmentLimit;
            finishLineZ = plan.FinishZ;
            plannedRouteDurationSeconds = targetSeconds;
            hasFiniteRoute = true;
            // Preserve the profile identifier, but distinguish the changed
            // finite task layout from previous continuous layouts.
            finiteRouteIdentifier = $"finite-v4-anchors-m{(int)mode}" + $"-t{FiniteRouteNumber(targetSeconds)}" + $"-s{FiniteRouteNumber(speed)}" + $"-z{FiniteRouteNumber(startZ)}" + $"-p{FiniteRouteNumber(playerStartZ)}" + $"-g{finishSegmentIndex}" + $"-clear{FiniteRouteNumber(finishClearance)}" + $"-r{finishRunoffSegments}" + $"-f{minimumFinalThemeSegments}";
            double nominalSeconds = ((double)finishLineZ - playerStartZ) / speed;
            Debug.Log($"Finite route: {mode}/{currentRun.Mode}, " + $"target {targetSeconds:F0}s, nominal " + $"{nominalSeconds:F1}s, gate Z={finishLineZ:F1}, " + $"gate index={finishSegmentIndex}, clearance={finishClearance:F1}m, " + $"total tracks={finiteSegmentLimit}. " + "Finish corridor ready.", this);
            return true;
        }

        private bool IsFinishCorridor(int segmentIndex)
        {
            return hasFiniteRoute && segmentIndex > finishSegmentIndex && segmentIndex < finiteSegmentLimit;
        }

        private float EncounterReservationLimit(int segmentIndex, float originZ)
        {
            float limit = hasFiniteRoute ? finishLineZ - finishClearance : float.PositiveInfinity;
            // Long patterns can reserve up to three consecutive ordinary road segments.
            for (int offset = 0; offset < 3; offset++)
            {
                int index = segmentIndex + offset;
                if (environmentManager != null && (environmentManager.RequiresClearTrack(index) || environmentManager.IsTransitionSegment(index + 1)))
                    return Mathf.Min(limit, originZ + offset * TrackSegment.ContractLength);
            }

            return Mathf.Min(limit, originZ + 3f * TrackSegment.ContractLength - 1f);
        }

        private bool FiniteRouteError(string message)
        {
            hasFiniteRoute = false;
            finiteRouteIdentifier = string.Empty;
            Debug.LogError($"Finite route: {message}", this);
            return false;
        }

        private static bool IsFiniteRouteNumber(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string FiniteRouteNumber(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !hasFiniteRoute)
                return;
            // Scene-view marker only: no geometry or collider is created.
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(new Vector3(0f, 4.75f, finishLineZ), new Vector3(12f, 9.5f, 1f));
        }
    }
}
