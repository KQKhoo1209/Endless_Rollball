using UnityEngine;

namespace EndlessRollball.Scenery
{
    // Loaded by path, never referenced directly by a gameplay scene/configuration.
    public sealed class EnvironmentScenerySet : ScriptableObject
    {
        public GameObject[] prefabs = System.Array.Empty<GameObject>();
        public Material skybox;
        [Tooltip("Replaces one regular scenery block when a junction track is selected.")]
        public GameObject junctionScenery;
    }
}
