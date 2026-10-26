using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Infrastructure
{
    /// <summary>Caps inactive retention only. Active demand is never capped or reclaimed.</summary>
    public sealed class PrefabPool
    {
        [Serializable]
        public sealed class Counters
        {
            public GameObject Prefab;
            public int Created, Acquired, Returned, Destroyed, Active, Inactive;
        }

        private sealed class Bucket
        {
            internal readonly Queue<GameObject> Idle = new Queue<GameObject>();
            internal readonly Counters Count = new Counters();
        }

        private readonly Dictionary<GameObject, Bucket> buckets = new Dictionary<GameObject, Bucket>();
        private readonly HashSet<GameObject> leased = new HashSet<GameObject>();
        private readonly List<Counters> statistics = new List<Counters>();
        public IReadOnlyList<Counters> Statistics => statistics;

        public event Action<GameObject> Discarding;
        private Bucket Get(GameObject prefab)
        {
            if (!buckets.TryGetValue(prefab, out Bucket bucket))
            {
                bucket = new Bucket();
                bucket.Count.Prefab = prefab;
                buckets.Add(prefab, bucket);
                statistics.Add(bucket.Count);
            }

            return bucket;
        }

        public GameObject Acquire(GameObject prefab, Transform inactiveRoot)
        {
            Bucket bucket = Get(prefab);
            GameObject instance = null;
            while (bucket.Idle.Count > 0 && instance == null)
                instance = bucket.Idle.Dequeue();
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(prefab, inactiveRoot, false);
                instance.name = prefab.name + "_Pooled";
                bucket.Count.Created++;
            }

            instance.SetActive(false);
            leased.Add(instance);
            bucket.Count.Acquired++;
            bucket.Count.Active++;
            bucket.Count.Inactive = bucket.Idle.Count;
            return instance;
        }

        public void Return(GameObject prefab, GameObject instance, Transform inactiveRoot, int retention)
        {
            if (ReferenceEquals(instance, null) || !leased.Remove(instance))
                return;
            Bucket bucket = Get(prefab);
            bucket.Count.Active = Mathf.Max(0, bucket.Count.Active - 1);
            bucket.Count.Returned++;
            // Purge externally destroyed entries before applying the retention policy.
            int entries = bucket.Idle.Count;
            for (int i = 0; i < entries; i++)
            {
                GameObject entry = bucket.Idle.Dequeue();
                if (entry != null)
                    bucket.Idle.Enqueue(entry);
            }

            if (instance != null)
            {
                instance.SetActive(false);
                instance.transform.SetParent(inactiveRoot, false);
                if (inactiveRoot != null && bucket.Idle.Count < Mathf.Max(0, retention))
                    bucket.Idle.Enqueue(instance);
                else
                {
                    Discarding?.Invoke(instance);
                    UnityEngine.Object.Destroy(instance);
                    bucket.Count.Destroyed++;
                }
            }

            bucket.Count.Inactive = bucket.Idle.Count;
        }
    }
}
