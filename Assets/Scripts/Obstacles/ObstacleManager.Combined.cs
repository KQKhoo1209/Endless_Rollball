using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        private bool TryPlanCombined(
            ThemeObstacleLibrary library,
            TrackSegment road,
            int index,
            long identity,
            DifficultySnapshot difficulty,
            float originZ,
            float reservationLimit,
            System.Random random,
            out EncounterPlan result, bool forceSelection = false)
        {
            result = default;

            if (library == null ||
                library.CombinedEncounters == null ||
                library.CombinedEncounters.Length == 0)
            {
                return false;
            }

            // An initial frequency to tune after Play Mode testing.
            if (!forceSelection && random.NextDouble() >= 0.35)
                return false;

            int offset = random.Next(library.CombinedEncounters.Length);

            for (int attempt = 0;
                attempt < library.CombinedEncounters.Length;
                attempt++)
            {
                CombinedObstacleEncounter encounter =
                    library.CombinedEncounters[
                        (offset + attempt) %
                        library.CombinedEncounters.Length];

                if (encounter == null ||
                    string.IsNullOrWhiteSpace(encounter.FamilyId) ||
                    encounter.FamilyId == planner.LastFamily ||
                    encounter.JumpHazards == null ||
                    encounter.NavigationHazards == null)
                {
                    continue;
                }

                Transform root = encounter.transform;
                Transform jumpRoot = encounter.JumpHazards.transform;
                Transform navRoot = encounter.NavigationHazards.transform;

                if (root.localPosition != Vector3.zero ||
                    root.localRotation != Quaternion.identity ||
                    root.localScale != Vector3.one ||
                    jumpRoot == root ||
                    navRoot == root ||
                    !jumpRoot.IsChildOf(root) ||
                    !navRoot.IsChildOf(root) ||
                    jumpRoot.IsChildOf(navRoot) ||
                    navRoot.IsChildOf(jumpRoot))
                {
                    continue;
                }

                if (!TryMeasureCombinedGroup(
                        root, encounter.JumpHazards,
                        out Bounds jumpBounds) ||
                    !TryMeasureCombinedGroup(
                        root, encounter.NavigationHazards,
                        out Bounds navBounds) ||
                    !CanClearJump(jumpBounds, difficulty))
                {
                    continue;
                }

                // The fence stays static; cargo may use dynamic bodies.
                if (encounter.JumpHazards
                    .GetComponentsInChildren<Rigidbody>(true).Length > 0)
                {
                    continue;
                }

                float lateralOffset = 0f;
                if (cityPacingActive && encounter.AllowedLateralOffsets != null && encounter.AllowedLateralOffsets.Length > 0)
                    lateralOffset = encounter.AllowedLateralOffsets[random.Next(encounter.AllowedLateralOffsets.Length)];
                // Translate the complete fence/cargo layout so its intended jump
                // passage remains inside the narrowed corridor. Geometry outside the
                // corridor is harmless; the in-corridor route is validated below.
                float safeLimit = difficulty.Corridor.CentreLimit(Radius + PassagePadding);
                lateralOffset += Mathf.Clamp(encounter.JumpPassageX + lateralOffset,
                    -safeLimit, safeLimit) - (encounter.JumpPassageX + lateralOffset);
                jumpBounds.center += Vector3.right * lateralOffset;
                navBounds.center += Vector3.right * lateralOffset;
                float passage = encounter.JumpPassageX + lateralOffset;
                float clearance = Radius + PassagePadding;

                if (!difficulty.Corridor.Contains(passage, clearance))
                    continue;

                // The intended jump route must cross the fence,
                // while remaining clear of navigation hazards.
                bool crossesFence =
                    passage + Radius > jumpBounds.min.x &&
                    passage - Radius < jumpBounds.max.x;

                bool blockedByNavigation =
                    passage + clearance > navBounds.min.x &&
                    passage - clearance < navBounds.max.x;

                if (!crossesFence || blockedByNavigation)
                    continue;

                Bounds allBounds = jumpBounds;
                allBounds.Encapsulate(navBounds);

                // Preserve the layout's authored X positions.
                const float centreZ = 12f;

                float first =
                    originZ + centreZ + allBounds.min.z;

                float jumpStart =
                    originZ + centreZ + jumpBounds.min.z;

                float jumpLanding =
                    originZ + centreZ + jumpBounds.max.z
                    + Radius + 0.15f;

                float navigationLanding =
                    originZ + centreZ + allBounds.max.z
                    + Radius + 0.15f;

                // Keep other encounters out of this entire segment.
                float end = originZ + TrackSegment.ContractLength;

                float estimatedJumpEnd =
                    jumpLanding + JumpHalfTravel(difficulty) + 0.5f;

                if (first < originZ + 0.5f ||
                    navigationLanding + 0.5f >= end ||
                    estimatedJumpEnd > end ||
                    end > reservationLimit ||
                    !planner.CanReach(
                        index, first, end, passage,
                        difficulty, LateralSpeed, Acceleration))
                {
                    continue;
                }

                var navigationWindow = new TaskWindow
                {
                    LocalIndex = 0,
                    EntryZ = first - 12f,
                    HazardStartZ = first,
                    LandingStartZ = navigationLanding,
                    DeadlineZ = end,
                    RecoveryEndZ = end
                };

                var jumpWindow = new TaskWindow
                {
                    LocalIndex = 1,
                    EntryZ = jumpStart - 12f,
                    HazardStartZ = jumpStart,
                    LandingStartZ = jumpLanding,
                    DeadlineZ = end,
                    RecoveryEndZ = end
                };

                var placement = new Placement(
                    encounter.gameObject,
                    new Vector3(lateralOffset, 0f, centreZ),
                    false);

                result = new EncounterPlan(
                    this,
                    runVersion,
                    index,
                    road,
                    new[] { placement },
                    false,
                    end,
                    passage,
                    difficulty.Corridor.Width,
                    identity,
                    encounter.FamilyId,
                    new[] { navigationWindow, jumpWindow });

                return true;
            }

            return false;
        }

        private static bool TryMeasureCombinedGroup(
            Transform root,
            GameObject group,
            out Bounds bounds)
        {
            bounds = default;
            bool measured = false;

            foreach (Collider shape in
                group.GetComponentsInChildren<Collider>(true))
            {
                if (!(shape is BoxCollider box) ||
                    !box.enabled ||
                    box.isTrigger ||
                    !box.gameObject.activeSelf)
                {
                    return false;
                }

                EncapsulateLocalBounds(
                    root,
                    box.transform,
                    new Bounds(box.center, box.size),
                    ref bounds,
                    ref measured);
            }

            return measured;
        }
    }
}
