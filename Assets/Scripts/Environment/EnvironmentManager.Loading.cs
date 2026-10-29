using UnityEngine;

namespace EndlessRollball.Scenery
{
    public sealed partial class EnvironmentManager
    {
        private readonly EnvironmentAssetLoader assetLoader = new EnvironmentAssetLoader();
        [SerializeField, Min(1f)] private float themePrefetchSeconds = 60f;
        public bool IsReady => sceneryReady;
        public Camera GameplayCamera => gameplayCamera;
        public string LoadingError { get; private set; }
        public bool StreamingHoldRequired { get; private set; }

        private bool TryGetScenery(EnvironmentTheme theme, out EnvironmentScenerySet set)
        {
            EnvironmentThemeConfig settings = GetSettings(theme);
            bool ready = assetLoader.TryGet(settings == null ? null : settings.sceneryResourcePath,
                theme != EnvironmentTheme.Dungeon, out set, out string error);
            if (error != null) LoadingError = error;
            return ready;
        }

        internal bool IsSegmentSceneryReady(int index)
        {
            var slot = GetScenerySlot(index);
            return slot.IsTunnel || TryGetScenery(slot.Theme, out _);
        }

        internal void CoverForPendingTrack()
        {
            // The follow camera can still be behind the tunnel while the ball reaches
            // its exit. Do not wait for the camera-driven fade to protect the ball.
            if (transitionRequests.Count == 0) return;
            SetJourneyFadeAlpha(1f);
            StreamingHoldRequired = true;
            journeyFadeStage = JourneyFadeStage.Covered;
            journeyCoveredFrame = Time.frameCount;
            journeyPhaseElapsed = Mathf.Max(0f, journeyCoveredHold);
        }

        internal void Prefetch(float playerZ, float aheadDistance, float maxSpeed, int nextIndex)
        {
            if (!sceneryReady || !HasScheduledJourney) return;
            // Look only as far as the next theme; journey planning remains ordered and seeded.
            int currentZone = GetScenerySlot(nextIndex).ZoneId;
            float limit = playerZ + Mathf.Max(aheadDistance * 2f, Mathf.Max(0f, maxSpeed) * themePrefetchSeconds);
            int last = journey.IndexAtWorldZ(limit);
            for (int i = nextIndex; i <= last; i++)
            {
                var slot = GetScenerySlot(i);
                if (!slot.IsTunnel && (slot.ZoneId > visibleZone || slot.ZoneId > currentZone))
                {
                    TryGetScenery(slot.Theme, out _);
                    break;
                }
            }
        }

        private void OnDestroy()
        {
            assetLoader.Clear();
            sceneryPool.Clear();
            sceneryPoolStatistics.Clear();
        }
    }
}
