using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed class TrackSegment : MonoBehaviour
    {
        public const float ContractLength = 24f;

        [SerializeField] private string segmentId = "segment";
        [SerializeField] private Transform entryAnchor;
        [SerializeField] private Transform exitAnchor;
        [SerializeField] private TrackSegmentKind kind;
        [SerializeField] private DifficultyModeMask allowedModes = DifficultyModeMask.All;
        [SerializeField] private TrackLaneMask entryLanes = TrackLaneMask.All;
        [SerializeField] private TrackLaneMask safeLanes = TrackLaneMask.All;
        [SerializeField] private bool requiredJump;

        [Header("Selection weights")]
        [SerializeField, Min(0f)] private float easyWeight = 1f;
        [SerializeField, Min(0f)] private float normalWeight = 1f;
        [SerializeField, Min(0f)] private float hardWeight = 1f;
        [SerializeField, Min(0f)] private float endlessWeight = 1f;

        public string SegmentId => segmentId;
        public Transform EntryAnchor => entryAnchor;
        public Transform ExitAnchor => exitAnchor;
        public TrackSegmentKind Kind => kind;
        public TrackLaneMask EntryLanes => entryLanes;
        public TrackLaneMask SafeLanes => safeLanes;
        public bool RequiredJump => requiredJump;
        public bool IsObstacle => kind is TrackSegmentKind.Gap
            or TrackSegmentKind.StaticObstacle
            or TrackSegmentKind.MovingObstacle;
        public float Length => exitAnchor == null || entryAnchor == null
            ? 0f
            : exitAnchor.localPosition.z - entryAnchor.localPosition.z;

        public bool Supports(DifficultyMode mode)
        {
            return DifficultyModeMaskUtility.Allows(allowedModes, mode) && GetWeight(mode) > 0f;
        }

        public float GetWeight(DifficultyMode mode)
        {
            return mode switch
            {
                DifficultyMode.Easy => easyWeight,
                DifficultyMode.Normal => normalWeight,
                DifficultyMode.Hard => hardWeight,
                DifficultyMode.Endless => endlessWeight,
                _ => 0f
            };
        }

        public void Activate(DifficultySnapshot difficulty)
        {
            gameObject.SetActive(true);

            foreach (MovingObstacle obstacle in GetComponentsInChildren<MovingObstacle>(true))
            {
                obstacle.Configure(difficulty.MovingObstacleSpeed);
            }
        }

        public void Deactivate()
        {
            gameObject.SetActive(false);
        }

        public bool IsContractValid(out string reason)
        {
            if (entryAnchor == null || exitAnchor == null)
            {
                reason = "Entry and exit anchors are required.";
                return false;
            }

            if (entryAnchor.localPosition.sqrMagnitude > 0.000001f)
            {
                reason = "The entry anchor must be at local (0, 0, 0).";
                return false;
            }

            Vector3 expectedExit = new Vector3(0f, 0f, ContractLength);
            if ((exitAnchor.localPosition - expectedExit).sqrMagnitude > 0.000001f)
            {
                reason = $"The exit anchor must be at local {expectedExit}.";
                return false;
            }

            if (entryLanes == TrackLaneMask.None || safeLanes == TrackLaneMask.None)
            {
                reason = "At least one entry and safe lane is required.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public void Configure(
            string configuredId,
            Transform configuredEntry,
            Transform configuredExit,
            TrackSegmentKind configuredKind,
            DifficultyModeMask configuredModes,
            TrackLaneMask configuredEntryLanes,
            TrackLaneMask configuredSafeLanes,
            bool configuredRequiredJump,
            float configuredEasyWeight,
            float configuredNormalWeight,
            float configuredHardWeight,
            float configuredEndlessWeight)
        {
            segmentId = configuredId;
            entryAnchor = configuredEntry;
            exitAnchor = configuredExit;
            kind = configuredKind;
            allowedModes = configuredModes;
            entryLanes = configuredEntryLanes;
            safeLanes = configuredSafeLanes;
            requiredJump = configuredRequiredJump;
            easyWeight = configuredEasyWeight;
            normalWeight = configuredNormalWeight;
            hardWeight = configuredHardWeight;
            endlessWeight = configuredEndlessWeight;
        }
    }
}
