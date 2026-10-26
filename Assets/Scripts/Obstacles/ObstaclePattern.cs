using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    [CreateAssetMenu(menuName = "Endless Rollball/Obstacle Pattern")]
    public sealed class ObstaclePattern : ScriptableObject
    {
        [Serializable]
        public struct Placement
        {
            public GameObject prefab;
            [Tooltip("Desired collider centre X and local segment Z, in metres.")]
            public Vector2 centre;
            public bool requiredJump;
            [Min(0)]
            public int stageIndex;
        }

        [Serializable]
        public struct Stage
        {
            public float centreZ;
            public float passageX;
            public bool requiredJump;
        }

        [SerializeField]
        private string family;
        [SerializeField]
        private Stage[] stages = Array.Empty<Stage>();
        [SerializeField, Min(0)]
        private int gapStageIndex;
        public string Family => family;
        public Stage[] Stages => stages;
        public int GapStageIndex => gapStageIndex;

        [SerializeField]
        private Placement[] placements = Array.Empty<Placement>();
        [SerializeField]
        private bool usesGap;
        [SerializeField]
        private bool alternateSide;
        [SerializeField]
        private float exitCentreX = 1.5f;
        public Placement[] Placements => placements;
        public bool UsesGap => usesGap;
        public bool AlternateSide => alternateSide;
        public float ExitCentreX => exitCentreX;
    }
}
