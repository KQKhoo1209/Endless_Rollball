using System;
using System.Collections.Generic;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball.Scenery
{
    [DisallowMultipleComponent]
    public sealed partial class EnvironmentManager : MonoBehaviour
    {
        [Serializable]
        private sealed class ThemeSettings
        {
            public List<GameObject> sceneryPrefabs =
                new List<GameObject>();

            public Material skyboxMaterial;

            public Color ambientColor =
                new Color(0.5f, 0.5f, 0.5f);

            [Min(0f)]
            public float directionalLightIntensity = 1f;

            // Preserve the previously assigned art source for authoring/rollback only.
            [HideInInspector]
            public GameObject roadSurfacePrefab;

            [Header("Playable road track")]
            [Tooltip("Complete 24m TrackSegment, including its visuals and floor collider.")]
            public TrackSegment roadTrackPrefab;
        }

        [Header("Scene references")]
        [SerializeField] private Transform activeSceneryRoot;
        [SerializeField] private Transform sceneryPoolRoot;
        [SerializeField] private Camera gameplayCamera;
        [SerializeField] private Light directionalLight;

        [Header("Theme prefab lists and presentation")]
        [SerializeField]
        private ThemeSettings street =
            new ThemeSettings();

        [SerializeField]
        private ThemeSettings beach =
            new ThemeSettings();

        [SerializeField]
        private ThemeSettings dungeon =
            new ThemeSettings();

        [Header("Dungeon background")]
        [SerializeField]
        private Color dungeonBackgroundColor =
            new Color(0.025f, 0.025f, 0.035f);

        private ThemeSettings GetSettings(EnvironmentTheme theme)
        {
            return theme switch
            {
                EnvironmentTheme.Street => street,
                EnvironmentTheme.Water => beach,
                EnvironmentTheme.Dungeon => dungeon,

                _ => throw new ArgumentOutOfRangeException(
                    nameof(theme), theme, "Unknown environment theme.")
            };
        }
    }
}
