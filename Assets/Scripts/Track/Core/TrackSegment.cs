using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed class TrackSegment : MonoBehaviour
    {
        public const float ContractLength = 24f;
        [SerializeField] private bool compactJunction;
        public bool IsCompactJunction => compactJunction;
        public Vector3 EntryLocal => entryAnchor == null ? Vector3.zero : transform.InverseTransformPoint(entryAnchor.position);
        public Vector3 ExitLocal => exitAnchor == null ? Vector3.zero : transform.InverseTransformPoint(exitAnchor.position);
        [SerializeField]
        private string segmentId = "segment";
        [SerializeField]
        private Transform entryAnchor;
        [SerializeField]
        private Transform exitAnchor;
        [SerializeField]
        private TrackSegmentKind kind;
        [SerializeField]
        private DifficultyModeMask allowedModes = DifficultyModeMask.All;
        [SerializeField]
        private TrackLaneMask entryLanes = TrackLaneMask.All;
        [SerializeField]
        private TrackLaneMask safeLanes = TrackLaneMask.All;
        [SerializeField]
        private bool requiredJump;
        [Tooltip("Optional visual-only child. Zone fades hide this child without disabling floor collision.")]
        [SerializeField]
        private GameObject presentationRoot;
        [Header("Selection weights")]
        [SerializeField, Min(0f)]
        private float easyWeight = 1f;
        [SerializeField, Min(0f)]
        private float normalWeight = 1f;
        [SerializeField, Min(0f)]
        private float hardWeight = 1f;
        [SerializeField, Min(0f)]
        private float endlessWeight = 1f;
        public Transform VisualRoot => presentationRoot != null ? presentationRoot.transform : transform;
        public string SegmentId => segmentId;
        public Transform EntryAnchor => entryAnchor;
        public Transform ExitAnchor => exitAnchor;
        public TrackSegmentKind Kind => kind;
        public TrackLaneMask EntryLanes => entryLanes & TrackLaneMask.All;
        public TrackLaneMask SafeLanes => safeLanes & TrackLaneMask.All;
        public bool RequiredJump => requiredJump;
        public bool IsPresentationVisible => presentationRoot == null || presentationRoot.activeInHierarchy;
        public bool IsObstacle => kind is TrackSegmentKind.Gap or TrackSegmentKind.StaticObstacle or TrackSegmentKind.MovingObstacle;
        public float Length => exitAnchor == null || entryAnchor == null ? 0f : ExitLocal.z - EntryLocal.z;

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
            SetPresentationVisible(true);
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

        public void SetPresentationVisible(bool visible)
        {
            if (presentationRoot != null && presentationRoot != gameObject && presentationRoot.transform.IsChildOf(transform))
                presentationRoot.SetActive(visible);
        }

        public bool IsContractValid(out string reason)
        {
            if (entryAnchor == null || exitAnchor == null)
            {
                reason = "Entry and exit anchors are required.";
                return false;
            }

            if (compactJunction)
            {
                if (GetComponent<JunctionTrafficController>() == null || requiredJump ||
                    transform.localScale != Vector3.one ||
                    !entryAnchor.IsChildOf(transform) || !exitAnchor.IsChildOf(transform) ||
                    Mathf.Abs(EntryLocal.x) > .001f || Mathf.Abs(ExitLocal.x) > .001f ||
                    Mathf.Abs(EntryLocal.y) > .001f || Mathf.Abs(ExitLocal.y) > .001f ||
                    float.IsNaN(Length) || float.IsInfinity(Length) || Length < 1f || Length > ContractLength)
                { reason = "Compact junction requires a straight positive span up to 24 m and unit root scale."; return false; }
                reason = string.Empty;
                return true;
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

        public void Configure(string configuredId, Transform configuredEntry, Transform configuredExit, TrackSegmentKind configuredKind, DifficultyModeMask configuredModes, TrackLaneMask configuredEntryLanes, TrackLaneMask configuredSafeLanes, bool configuredRequiredJump, float configuredEasyWeight, float configuredNormalWeight, float configuredHardWeight, float configuredEndlessWeight)
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
