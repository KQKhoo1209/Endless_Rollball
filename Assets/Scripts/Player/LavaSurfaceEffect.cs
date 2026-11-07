using UnityEngine;

namespace EndlessRollball
{
    // One reusable instance per encounter, anchored to the course's lava surface.
    internal sealed class LavaSurfaceEffect
    {
        private readonly Transform root;
        private readonly ParticleSystem[] prepare;
        private readonly ParticleSystem[] impact;

        private LavaSurfaceEffect(GameObject instance)
        {
            root = instance.transform;
            prepare = new[]
            {
                root.Find("Prepare Bubbles").GetComponent<ParticleSystem>(),
                root.Find("Prepare Ripples").GetComponent<ParticleSystem>()
            };
            impact = new[]
            {
                root.Find("Impact Droplets").GetComponent<ParticleSystem>(),
                root.Find("Impact Ripples").GetComponent<ParticleSystem>()
            };
            Clear();
        }

        internal static LavaSurfaceEffect Create(Transform owner)
        {
            var prefab = Resources.Load<GameObject>("LavaEffects/LavaSurfaceEffect");
            if (prefab == null)
            {
                Debug.LogWarning("LavaSurfaceEffect is missing from Resources/LavaEffects.", owner);
                return null;
            }
            return new LavaSurfaceEffect(Object.Instantiate(prefab, owner));
        }

        internal void Reset(Transform course, Vector3 position)
        {
            Clear();
            root.SetParent(course, true);
            root.SetPositionAndRotation(position, Quaternion.identity);
        }

        internal void BeginPrepare(Vector3 position)
        {
            root.position = position;
            foreach (var system in prepare)
                system.Play(false);
        }

        internal void EndPrepare()
        {
            foreach (var system in prepare)
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }

        internal void Impact(Vector3 position)
        {
            EndPrepare();
            root.position = position;
            foreach (var system in impact)
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(false);
            }
        }

        internal void Clear()
        {
            foreach (var system in prepare)
                if (system != null)
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var system in impact)
                if (system != null)
                    system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        internal void Dispose()
        {
            if (root != null)
                Object.Destroy(root.gameObject);
        }
    }
}
