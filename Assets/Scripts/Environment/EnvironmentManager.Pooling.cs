using System.Collections.Generic;
using EndlessRollball.Track;
using UnityEngine;
using EndlessRollball.Infrastructure;

namespace EndlessRollball.Scenery
{
    public sealed partial class EnvironmentManager
    {
        private sealed class SceneryPair
        {
            public GameObject Prefab;
            public GameObject Instance;
            public int ZoneId;
            public bool HiddenForTransition;
            public TrackSegment Track;
        }

        private sealed class VariantBag
        {
            public readonly List<GameObject> Items = new List<GameObject>();
            public int Index;
            public GameObject Last;
        }

        private readonly Dictionary<TrackSegment, SceneryPair> activeScenery = new Dictionary<TrackSegment, SceneryPair>();
        [SerializeField, Min(0)]
        private int maxInactiveSceneryPerPrefab = 2;
        private readonly PrefabPool sceneryPool = new PrefabPool();
        [Header("Scenery pool diagnostics (runtime)")]
        [SerializeField]
        private List<PrefabPool.Counters> sceneryPoolStatistics = new List<PrefabPool.Counters>();
        public IReadOnlyList<PrefabPool.Counters> SceneryPoolStatistics => sceneryPool.Statistics;

        private readonly Dictionary<EnvironmentThemeConfig, VariantBag> variantBags = new Dictionary<EnvironmentThemeConfig, VariantBag>();
        private System.Random sceneryRandom;
        private EnvironmentThemeConfig currentSettings;
        private bool sceneryReady;
        public void BeginRun(GameMode mode, DifficultyMode difficulty, int trackSeed)
        {
            // Compatibility with the original generator origin.
            BeginRun(mode, difficulty, trackSeed, -12f);
        }

        public void BeginRun(GameMode mode, DifficultyMode difficulty, int trackSeed, float trackOriginZ)
        {
            EndRun();
            if (!isActiveAndEnabled)
                return;
            CurrentTheme = SelectStartingTheme(mode, difficulty, trackSeed);
            currentSettings = GetSettings(CurrentTheme);
            if (activeSceneryRoot == null || sceneryPoolRoot == null || activeSceneryRoot == sceneryPoolRoot || activeSceneryRoot.IsChildOf(sceneryPoolRoot) || sceneryPoolRoot.IsChildOf(activeSceneryRoot))
            {
                Debug.LogWarning("Assign separate, non-nested active and pool roots.", this);
                return;
            }

            if (!activeSceneryRoot.gameObject.activeInHierarchy || !HasValidTheme(CurrentTheme))
            {
                Debug.LogWarning("The active root must be active; the starting theme " + "needs a valid prefab list and outdoor skybox.", this);
                return;
            }

            if (!ConfigureJourney(mode, difficulty, trackSeed, trackOriginZ))
                return;
            sceneryPoolRoot.gameObject.SetActive(false);
            sceneryRandom = new System.Random(unchecked(trackSeed ^ 0x13579BDF));
            CapturePresentation();
            ApplyPresentation();
            sceneryReady = true;
            Debug.Log($"Environment run: {CurrentTheme} | {mode}/{difficulty} | " + $"Seed {trackSeed} | {JourneyIdentifier}", this);
        }

        public void Attach(TrackSegment segment)
        {
            Attach(segment, -1);
        }

        public void Attach(TrackSegment segment, int segmentIndex)
        {
            if (!isActiveAndEnabled || !sceneryReady || segment == null || activeScenery.ContainsKey(segment))
                return;
            if (HasScheduledJourney && segmentIndex < 0)
            {
                Debug.LogWarning("Scheduled scenery requires the forward spawn index. " + "Use Attach(segment, segmentIndex).", this);
                return;
            }

            EnvironmentJourneyPlanner.ScenerySlot slot = GetScenerySlot(segmentIndex);
            GameObject prefab = slot.IsTunnel ? transitionTunnelSceneryPrefab : NextVariant(GetSettings(slot.Theme));
            bool hidden = slot.ZoneId > visibleZone;
            GameObject instance = AcquireScenery(prefab);
            instance.transform.SetParent(activeSceneryRoot, false);
            instance.transform.SetPositionAndRotation(segment.transform.position, segment.transform.rotation);
            instance.transform.localScale = Vector3.one;
            var pair = new SceneryPair
            {
                Prefab = prefab,
                Instance = instance,
                ZoneId = slot.ZoneId,
                HiddenForTransition = hidden,
                Track = segment
            };
            activeScenery.Add(segment, pair);
            if (slot.IsTunnel)
                RegisterJourneyTunnel(segment, segmentIndex, slot.ZoneId);
            instance.SetActive(!hidden);
            segment.SetPresentationVisible(!hidden);
        }

        public void Release(TrackSegment segment)
        {
            if (segment == null || !activeScenery.TryGetValue(segment, out SceneryPair pair))
                return;
            activeScenery.Remove(segment);
            ReturnScenery(pair);
        }

        public void EndRun()
        {
            sceneryReady = false;
            ResetJourneyPresentation();
            foreach (SceneryPair pair in activeScenery.Values)
                ReturnScenery(pair);
            activeScenery.Clear();
            variantBags.Clear();
            sceneryRandom = null;
            currentSettings = null;
            ResetJourneyPlan();
            RestorePresentation();
        }

        private GameObject AcquireScenery(GameObject prefab)
        {
            GameObject scenery = sceneryPool.Acquire(prefab, sceneryPoolRoot);
            if (sceneryPoolStatistics.Count != sceneryPool.Statistics.Count)
            {
                sceneryPoolStatistics.Clear();
                sceneryPoolStatistics.AddRange(sceneryPool.Statistics);
            }

            return scenery;
        }

        private void ReturnScenery(SceneryPair pair)
        {
            if (pair.Track != null)
                pair.Track.SetPresentationVisible(true);
            sceneryPool.Return(pair.Prefab, pair.Instance, sceneryPoolRoot, maxInactiveSceneryPerPrefab);
        }

        private GameObject NextVariant(EnvironmentThemeConfig settings)
        {
            if (!variantBags.TryGetValue(settings, out VariantBag bag))
            {
                bag = new VariantBag();
                variantBags.Add(settings, bag);
            }

            if (bag.Index >= bag.Items.Count)
            {
                bag.Items.Clear();
                bag.Items.AddRange(settings.sceneryPrefabs);
                for (int i = bag.Items.Count - 1; i > 0; i--)
                {
                    int other = sceneryRandom.Next(i + 1);
                    GameObject temporary = bag.Items[i];
                    bag.Items[i] = bag.Items[other];
                    bag.Items[other] = temporary;
                }

                if (bag.Items.Count > 1 && bag.Items[0] == bag.Last)
                {
                    GameObject temporary = bag.Items[0];
                    bag.Items[0] = bag.Items[1];
                    bag.Items[1] = temporary;
                }

                bag.Index = 0;
            }

            GameObject selected = bag.Items[bag.Index++];
            bag.Last = selected;
            return selected;
        }

        private void RevealVisibleZones()
        {
            foreach (SceneryPair pair in activeScenery.Values)
            {
                if (!pair.HiddenForTransition || pair.ZoneId > visibleZone)
                    continue;
                pair.HiddenForTransition = false;
                if (pair.Instance != null)
                    pair.Instance.SetActive(true);
                if (pair.Track != null)
                    pair.Track.SetPresentationVisible(true);
            }
        }

        private void OnDisable()
        {
            EndRun();
        }
    }
}
