using UnityEngine;
namespace EndlessRollball.Infrastructure
{
    // References existing authored assets; loading the group loads their dependencies.
    public sealed class GameplayPreloadGroup : ScriptableObject
    {
        public Object[] assets;
    }
}
