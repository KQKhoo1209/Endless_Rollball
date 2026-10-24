using UnityEngine;

namespace EndlessRollball.Scenery
{
    [CreateAssetMenu(
        fileName = "Theme",
        menuName = "Endless Rollball Assets/Environment/Theme Config")]
    public sealed class EnvironmentThemeConfig : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private EnvironmentTheme theme;

        [Header("24 m scenery prefabs")]
        [SerializeField]
        private GameObject[] sceneryPrefabs =
            new GameObject[0];

        [Header("Presentation")]
        [SerializeField] private Material skyboxMaterial;

        [SerializeField]
        private Color ambientLight =
            new Color(0.5f, 0.5f, 0.5f);

        [SerializeField] private bool fogEnabled;

        [SerializeField]
        private Color fogColor =
            new Color(0.6f, 0.7f, 0.8f);

        [SerializeField, Min(0f)] private float fogStart = 80f;

        [SerializeField, Min(0f)] private float fogEnd = 180f;

        public EnvironmentTheme Theme => theme;
        public GameObject[] SceneryPrefabs => sceneryPrefabs;

        public Material SkyboxMaterial => skyboxMaterial;
        public Color AmbientLight => ambientLight;

        public bool FogEnabled => fogEnabled;
        public Color FogColor => fogColor;
        public float FogStart => fogStart;
        public float FogEnd => fogEnd;
    }
}