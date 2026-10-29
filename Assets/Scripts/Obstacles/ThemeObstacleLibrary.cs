using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    [CreateAssetMenu(
        fileName = "ObstacleLibrary",
        menuName = "Endless Rollball/Theme Obstacle Library")]
    public sealed class ThemeObstacleLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Family
        {
            [Tooltip("Stable encounter identity, shared by its visual variants.")]
            [SerializeField]
            private string familyId;

            [Tooltip("Prefabs that provide the same basic player challenge.")]
            [SerializeField]
            private GameObject[] variants = Array.Empty<GameObject>();

            public string FamilyId => familyId;
            public GameObject[] Variants => variants;
        }

        [Serializable]
        public sealed class PacingSettings
        {
            [Min(1)] public int minimumFamilyAppearances = 3;
            public Vector2 easySeconds = new Vector2(4f, 5f);
            public Vector2 normalSeconds = new Vector2(3f, 4f);
            public Vector2 hardSeconds = new Vector2(2.5f, 3f);
            [Min(0f)] public float breathingSeconds = 1f;
        }
        [SerializeField] private bool useCityPacing;
        [SerializeField] private PacingSettings cityPacing = new PacingSettings();
        public PacingSettings CityPacing => useCityPacing ? cityPacing : null;

        [Header("Steer around stationary hazards")]
        [SerializeField]
        private Family[] fixedFamilies = Array.Empty<Family>();

        [Header("Navigate moving hazards")]
        [SerializeField]
        private Family[] movingFamilies = Array.Empty<Family>();

        [Header("Jump over hazards")]
        [SerializeField]
        private Family[] jumpFamilies = Array.Empty<Family>();

        [Header("Roadworks track replacements")]
        [SerializeField]
        private RoadworksTrackEncounter[] roadworksTracks =
            Array.Empty<RoadworksTrackEncounter>();

        public RoadworksTrackEncounter[] RoadworksTracks => roadworksTracks;

        [Header("Junction track replacements")]
        [SerializeField]
        private JunctionTrafficController[] junctionTracks =
            Array.Empty<JunctionTrafficController>();

        public JunctionTrafficController[] JunctionTracks => junctionTracks;

        [Header("Bus and taxi stop obstacle prefabs")]
        [SerializeField] private RoadsideStopEncounter[] roadsideStops = Array.Empty<RoadsideStopEncounter>();
        public RoadsideStopEncounter[] RoadsideStops => roadsideStops;

        [Header("Forward and oncoming traffic tracks")]
        [SerializeField] private LongitudinalTrafficEncounter[] longitudinalTrafficTracks = Array.Empty<LongitudinalTrafficEncounter>();
        public LongitudinalTrafficEncounter[] LongitudinalTrafficTracks => longitudinalTrafficTracks;

        [SerializeField] private DungeonTrapEncounter[] dungeonTraps = Array.Empty<DungeonTrapEncounter>();
        public DungeonTrapEncounter[] DungeonTraps => dungeonTraps;

        [Header("Beach moving jump tracks")]
        [SerializeField] private BeachBarrierEncounter[] beachBarrierTracks = Array.Empty<BeachBarrierEncounter>();
        public BeachBarrierEncounter[] BeachBarrierTracks => beachBarrierTracks;

        [Header("Multi-stage encounters")]
        [SerializeField]
        private ObstaclePattern[] combinationPatterns =
            Array.Empty<ObstaclePattern>();

        public Family[] FixedFamilies => fixedFamilies;
        public Family[] MovingFamilies => movingFamilies;
        public Family[] JumpFamilies => jumpFamilies;
        public ObstaclePattern[] CombinationPatterns => combinationPatterns;

        [Header("Combined jump and navigation encounters")]
        [SerializeField]
        private CombinedObstacleEncounter[] combinedEncounters =
            Array.Empty<CombinedObstacleEncounter>();

        public CombinedObstacleEncounter[] CombinedEncounters =>
            combinedEncounters;
    }
}
