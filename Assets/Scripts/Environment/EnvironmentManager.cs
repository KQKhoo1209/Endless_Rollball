using System;
using System.Collections.Generic;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball.Scenery
{
    [DisallowMultipleComponent]
    public sealed partial class EnvironmentManager : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField]
        private Transform activeSceneryRoot;
        [SerializeField]
        private Transform sceneryPoolRoot;
        [SerializeField]
        private Camera gameplayCamera;
        [SerializeField]
        private Light directionalLight;
        [Header("Theme prefab lists and presentation")]
        [SerializeField]
        private EnvironmentThemeConfig street;
        [SerializeField]
        private EnvironmentThemeConfig beach;
        [SerializeField]
        private EnvironmentThemeConfig dungeon;
        [Header("Dungeon background")]
        [SerializeField]
        private Color dungeonBackgroundColor = new Color(0.025f, 0.025f, 0.035f);
        private EnvironmentThemeConfig GetSettings(EnvironmentTheme theme)
        {
            return theme switch
            {
                EnvironmentTheme.Street => street,
                EnvironmentTheme.Water => beach,
                EnvironmentTheme.Dungeon => dungeon,
                _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown environment theme.")};
        }

        [Header("Transition tunnel")]
        [SerializeField]
        private GameObject transitionTunnelSceneryPrefab;
        [Header("Standard Easy journey")]
        [SerializeField, Min(1)]
        private int standardEasyStreetSegments = 30;
        [Header("Standard Normal / Hard journey")]
        [SerializeField, Min(4)]
        private int standardThreeThemeSegmentsPerZone = 30;
        [Header("Endless journey")]
        [SerializeField, Min(4)]
        private int endlessSegmentsPerZone = 30;
        [Tooltip("Provisional environment-only Normal-stage threshold. " + "Measured from player world Z=0, not spawn-ahead distance.")]
        [SerializeField, Min(0f)]
        private float endlessDungeonUnlockDistance = 500f;
        private readonly EnvironmentJourneyPlanner journey = new EnvironmentJourneyPlanner();
        private int visibleZone;
        public bool HasScheduledJourney => isActiveAndEnabled && sceneryReady && journey.HasScheduledJourney;
        public bool SuppressRandomTunnels => isActiveAndEnabled && sceneryReady;
        public string JourneyIdentifier => sceneryReady ? journey.JourneyIdentifier : string.Empty;
        public int FinalFiniteZoneStartIndex => HasScheduledJourney ? journey.FinalFiniteZoneStartIndex : 0;

        public bool IsTransitionSegment(int index) => HasScheduledJourney && journey.IsTransitionSegment(index);
        public bool RequiresClearTrack(int index) => HasScheduledJourney && journey.RequiresClearTrack(index);
        private EnvironmentJourneyPlanner.ScenerySlot GetScenerySlot(int index) => journey.GetScenerySlot(index);
        private EnvironmentTheme GetThemeForZone(int zone) => journey.GetThemeForZone(zone);
        private void PruneEndlessThemeHistory() => journey.PruneEndlessThemeHistory(visibleZone);
        private void ResetJourneyPlan()
        {
            journey.ResetJourneyPlan();
            visibleZone = 0;
        }

        private bool ConfigureJourney(GameMode mode, DifficultyMode difficulty, int seed, float origin)
        {
            visibleZone = 0;
#if UNITY_EDITOR
            if(useReviewThemeOverride) mode=GameMode.FastPaced;
#endif
            if (!journey.ConfigureJourney(mode, difficulty, seed, origin, CurrentTheme, standardEasyStreetSegments, standardThreeThemeSegmentsPerZone, endlessSegmentsPerZone, endlessDungeonUnlockDistance))
            {
                Debug.LogWarning(journey.Error, this);
                return false;
            }

            if (journey.HasScheduledJourney && (transitionTunnelSceneryPrefab == null || !HasJourneyFadeReferences()))
            {
                Debug.LogWarning("Assign the journey tunnel and fade references.", this);
                return false;
            }

            return HasValidTheme(EnvironmentTheme.Street) && HasValidTheme(EnvironmentTheme.Water) && (mode == GameMode.Standard && difficulty == DifficultyMode.Easy || HasValidTheme(EnvironmentTheme.Dungeon));
        }

        private bool HasValidTheme(EnvironmentTheme theme)
        {
            EnvironmentThemeConfig settings = GetSettings(theme);
            if (settings == null || settings.sceneryPrefabs == null || settings.sceneryPrefabs.Count == 0)
                return false;
            var unique = new HashSet<GameObject>();
            foreach (GameObject prefab in settings.sceneryPrefabs)
                if (prefab == null || !unique.Add(prefab))
                    return false;
            return theme == EnvironmentTheme.Dungeon || settings.skyboxMaterial != null;
        }
    }
}
