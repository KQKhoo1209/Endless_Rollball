using UnityEngine;

namespace EndlessRollball
{
    // Owns one reusable particle instance for a carrier or moving obstacle.
    // Controllers supply movement explicitly so previews and docking stay quiet.
    internal sealed class WaterMovementWake
    {
        private static GameObject prefab;
        private static readonly int WaveHeight = Shader.PropertyToID("_WaveHeight");
        private static readonly int WaveFrequency = Shader.PropertyToID("_WaveFrequency");
        private static readonly int WaveSpeed = Shader.PropertyToID("_WaveSpeed");
        private readonly Transform root;
        private readonly ParticleSystem[] emitters;
        private readonly Vector3 footprintExtents;
        private readonly Vector3 centreOffset;
        private bool emitting;

        private WaterMovementWake(Transform owner, Transform visual)
        {
            // Measure before adding particle renderers to the owner's hierarchy.
            // Renderer bounds include the authored visual scale.
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            var bounds = new Bounds(owner.position, Vector3.one);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
            }
            footprintExtents = new Vector3(
                Mathf.Clamp(bounds.extents.x * .65f, .3f, 6f),
                0f,
                Mathf.Clamp(bounds.extents.z * .75f, .5f, 8f));
            centreOffset = bounds.center - owner.position;
            centreOffset.y = 0f;

            var instance = Object.Instantiate(prefab, owner);
            root = instance.transform;
            root.name = "Moving Water Wake";
            emitters = instance.GetComponentsInChildren<ParticleSystem>(true);
            Clear();
        }

        internal static WaterMovementWake Create(Transform owner, Transform visual)
        {
            if (prefab == null)
                prefab = Resources.Load<GameObject>("WaterEffects/MovingWaterWake");
            if (prefab == null)
            {
                Debug.LogWarning("MovingWaterWake prefab is missing from Resources/WaterEffects.", owner);
                return null;
            }
            return new WaterMovementWake(owner, visual);
        }

        internal void ResetAt(Vector3 position, float waterY, Material surface)
        {
            Clear();
            root.position = new Vector3(position.x + centreOffset.x, waterY + .09f, position.z + centreOffset.z);
            if (surface == null) return;

            var properties = new MaterialPropertyBlock();
            if (surface.HasProperty(WaveHeight))
                properties.SetFloat(WaveHeight, surface.GetFloat(WaveHeight));
            if (surface.HasProperty(WaveFrequency))
                properties.SetFloat(WaveFrequency, surface.GetFloat(WaveFrequency));
            if (surface.HasProperty(WaveSpeed))
                properties.SetFloat(WaveSpeed, surface.GetFloat(WaveSpeed));
            foreach (var emitter in emitters)
                emitter.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(properties);
        }

        internal void Move(Vector3 position, Vector3 velocity, float waterY, Vector3? visualHeading = null)
        {
            velocity.y = 0f;
            if (velocity.sqrMagnitude < .04f)
            {
                Stop();
                return;
            }

            Vector3 direction = visualHeading.HasValue ? visualHeading.Value : velocity;
            direction.y = 0f;
            direction.Normalize();
            Quaternion heading = Quaternion.LookRotation(direction, Vector3.up);
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            // Both measurements came from world-space renderer bounds. Project
            // them onto the emitter heading without applying the rig rotation again.
            float halfLength = Vector3.Dot(footprintExtents,
                new Vector3(Mathf.Abs(direction.x), 0, Mathf.Abs(direction.z)));
            float halfWidth = Vector3.Dot(footprintExtents,
                new Vector3(Mathf.Abs(right.x), 0, Mathf.Abs(right.z)));
            root.SetPositionAndRotation(
                new Vector3(position.x + centreOffset.x, waterY + .09f, position.z + centreOffset.z),
                heading);

            foreach (var emitter in emitters)
            {
                bool left = emitter.name.StartsWith("Left", System.StringComparison.Ordinal);
                bool tail = emitter.name == "Tail Foam";
                float lift = emitter.name.EndsWith("Spray", System.StringComparison.Ordinal) ? .12f : 0f;
                emitter.transform.localPosition = tail
                    ? new Vector3(0, 0, -halfLength)
                    : new Vector3(left ? -halfWidth : halfWidth, lift, halfLength * .35f);
            }
            if (emitting) return;
            foreach (var emitter in emitters)
                emitter.Play(false);
            emitting = true;
        }

        internal void Stop()
        {
            if (!emitting) return;
            foreach (var emitter in emitters)
                emitter.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            emitting = false;
        }

        internal void Clear()
        {
            foreach (var emitter in emitters)
                emitter.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            emitting = false;
        }
    }
}
