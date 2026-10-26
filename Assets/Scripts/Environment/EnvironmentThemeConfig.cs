using UnityEngine;
using EndlessRollball.Track;
using System.Collections.Generic;

namespace EndlessRollball.Scenery
{
    [CreateAssetMenu(fileName = "Theme", menuName = "Endless Rollball Assets/Environment/Theme Config")]
    public sealed class EnvironmentThemeConfig : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private EnvironmentTheme theme;
        [Header("24 m scenery prefabs")]
        [SerializeField]
        public List<GameObject> sceneryPrefabs = new List<GameObject>();
        [Header("Presentation")]
        [SerializeField]
        public Material skyboxMaterial;
        [SerializeField]
        private Color ambientLight = new Color(0.5f, 0.5f, 0.5f);
        [SerializeField]
        private bool fogEnabled;
        [SerializeField]
        private Color fogColor = new Color(0.6f, 0.7f, 0.8f);
        [SerializeField, Min(0f)]
        private float fogStart = 80f;
        [SerializeField, Min(0f)]
        private float fogEnd = 180f;
        [Header("Playable tracks")]
        public TrackSegment roadTrackPrefab;
        public TrackSegment gapTrackPrefab;
        public TrackSegment normalGapTrackPrefab;
        public TrackSegment hardGapTrackPrefab;
        [Min(0f)]
        public float directionalLightIntensity = 1f;
        public Color ambientColor => ambientLight;

        public TrackSegment GapFor(DifficultySnapshot difficulty)
        {
            int tier = difficulty.Mode == DifficultyMode.Endless ? (difficulty.Progress01 >= .75f ? 2 : difficulty.Progress01 >= .35f ? 1 : 0) : (int)difficulty.Mode;
            return tier >= 2 && hardGapTrackPrefab != null ? hardGapTrackPrefab : tier >= 1 && normalGapTrackPrefab != null ? normalGapTrackPrefab : gapTrackPrefab;
        }

        public EnvironmentTheme Theme => theme;
        public GameObject[] SceneryPrefabs => sceneryPrefabs.ToArray();
        public Material SkyboxMaterial => skyboxMaterial;
        public Color AmbientLight => ambientLight;
        public bool FogEnabled => fogEnabled;
        public Color FogColor => fogColor;
        public float FogStart => fogStart;
        public float FogEnd => fogEnd;
    }
}
