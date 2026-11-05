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
        [Serializable]
        public sealed class SharedPacingSettings
        {
            [Min(1)] public int minimumFamilyAppearances = 3;
            public Vector2 easySeconds = new Vector2(3f, 3.5f);
            public Vector2 normalSeconds = new Vector2(1.6f, 2f);
            public Vector2 hardSeconds = new Vector2(.2f, .3f);
            [Tooltip("Endless starting interval, independent of Easy tuning.")]
            public Vector2 endlessMinimumSeconds = new Vector2(2f, 2f);
            [Tooltip("Endless progression endpoint, independent of the Hard-only rush setting.")]
            public Vector2 endlessMaximumSeconds = new Vector2(.4f, .6f);
            [Tooltip("Preferred navigation share of decision rows, including gaps as jumps. Zero retains family-only selection. Falls back when the preferred action cannot fit.")]
            [Range(0f, 1f)] public float navigationShare;
            [Tooltip("Extra passage width beyond the ball diameter.")]
            public Vector3 passageExtraWidth = new Vector3(.6f, .4f, .2f);
        }
        [Header("Shared decision pacing (all themes)")]
        [SerializeField] private bool useSharedPacing;
        [SerializeField] private SharedPacingSettings sharedPacing = new SharedPacingSettings();
        public SharedPacingSettings SharedPacing => useSharedPacing ? sharedPacing : null;

        [Header("Legacy City pacing (shared pacing takes priority)")]
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

        [Header("Dungeon oncoming rolling stones")]
        [SerializeField] private DungeonRollingStoneBall[] rollingStoneBalls = Array.Empty<DungeonRollingStoneBall>();
        public DungeonRollingStoneBall[] RollingStoneBalls => rollingStoneBalls;

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
