using UnityEngine;

namespace EndlessRollball.Scenery
{
    [DisallowMultipleComponent]
    public sealed class TrackRoadVisual : MonoBehaviour
    {
        [Tooltip("Only the old visible floor renderers. Not obstacles, rails or guidance.")]
        [SerializeField] private Renderer[] originalFloorRenderers;

        private bool[] originalEnabled;
        private bool hidden;

        internal bool TryHideOriginalFloor()
        {
            if (hidden) return true;
            if (originalFloorRenderers == null || originalFloorRenderers.Length == 0)
                return false;

            foreach (Renderer item in originalFloorRenderers)
                if (item == null || !item.transform.IsChildOf(transform))
                    return false;

            originalEnabled = new bool[originalFloorRenderers.Length];
            for (int i = 0; i < originalFloorRenderers.Length; i++)
            {
                originalEnabled[i] = originalFloorRenderers[i].enabled;
                originalFloorRenderers[i].enabled = false;
            }
            hidden = true;
            return true;
        }

        internal void RestoreOriginalFloor()
        {
            if (!hidden) return;
            for (int i = 0; i < originalEnabled.Length; i++)
                if (originalFloorRenderers[i] != null)
                    originalFloorRenderers[i].enabled = originalEnabled[i];
            hidden = false;
            originalEnabled = null;
        }
    }
}