using System;
using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent]
    public sealed class PalmObstacle : MonoBehaviour
    {
        [SerializeField] private bool blocksLeft = true;
        [SerializeField] private Collider[] leafSensors = Array.Empty<Collider>();
        [SerializeField, Range(0.05f, 0.6f)] private float maximumDeflection = 0.55f;
        [SerializeField, Min(1f)] private float leafStiffness = 18f;
        [SerializeField, Min(0f)] private float leafDamping = 5.5f;
        [SerializeField] private bool rootedOnBeach;
        [SerializeField, Min(0f)] private float beachSetback = 3.3f;
        private sealed class LeafMesh
        {
            internal MeshFilter Filter;
            internal Mesh Original, Working;
            internal Vector3[] Rest, Deformed;
            internal float[] Weights;
        }
        private LeafMesh[] meshes;
        private Vector3 offset, velocity, impact;
        private bool running, touched, animating;
        public bool BlocksLeft => blocksLeft;
        public bool RootedOnBeach => rootedOnBeach;
        public float RoadsideX(float halfWidth) => (blocksLeft ? -1f : 1f) * (halfWidth + beachSetback);
        public bool IsLeafSensor(Collider shape) => shape != null && Array.IndexOf(leafSensors, shape) >= 0;
        public void SetRunning(bool value) => running = value;

        public void TouchLeaves(Vector3 point, Vector3 direction)
        {
            if (!running || touched) return;
            touched = true;
            EnsureMeshes();
            impact = point;
            velocity = Vector3.ClampMagnitude(direction, 1f) * 4.2f;
            animating = true;
        }

        private void EnsureMeshes()
        {
            if (meshes != null) return;
            MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
            var result = new System.Collections.Generic.List<LeafMesh>();
            foreach (MeshFilter filter in filters)
            {
                Mesh original = filter.sharedMesh;
                if (original == null || !original.isReadable) continue;
                Vector3[] rest = original.vertices;
                Vector2[] uv = original.uv;
                if (uv.Length != rest.Length) continue;
                Vector3 crown = Vector3.zero;
                int count = 0;
                for (int i = 0; i < uv.Length; i++)
                    if (IsGreenPalette(uv[i])) { crown += rest[i]; count++; }
                if (count == 0) continue;
                crown /= count;
                float reach = 0.01f;
                for (int i = 0; i < rest.Length; i++)
                    if (IsGreenPalette(uv[i])) reach = Mathf.Max(reach, Vector3.Distance(rest[i], crown));
                float[] weights = new float[rest.Length];
                for (int i = 0; i < rest.Length; i++)
                    if (IsGreenPalette(uv[i]))
                        weights[i] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Vector3.Distance(rest[i], crown) / reach - 0.12f) / 0.88f));
                Mesh working = Instantiate(original);
                working.name = original.name + " (Palm leaves)";
                working.MarkDynamic();
                filter.sharedMesh = working;
                result.Add(new LeafMesh { Filter = filter, Original = original, Working = working,
                    Rest = rest, Deformed = new Vector3[rest.Length], Weights = weights });
            }
            meshes = result.ToArray();
        }

        // These two Kenney palms use green UVs in the left palette column;
        // brown trunk UVs are in the right column and remain completely fixed.
        private static bool IsGreenPalette(Vector2 uv) => uv.x > 0.18f && uv.x < 0.26f && uv.y > 0.25f && uv.y < 0.50f;

        private void Update()
        {
            if (!running || !animating) return;
            float remaining = Mathf.Min(Time.deltaTime, 0.1f);
            while (remaining > 0f)
            {
                float dt = Mathf.Min(remaining, 1f / 120f);
                velocity += (-offset * leafStiffness - velocity * leafDamping) * dt;
                offset = Vector3.ClampMagnitude(offset + velocity * dt, maximumDeflection);
                remaining -= dt;
            }
            if (offset.sqrMagnitude < 0.000001f && velocity.sqrMagnitude < 0.00001f)
            {
                offset = velocity = Vector3.zero;
                animating = false;
            }
            foreach (LeafMesh leaf in meshes)
            {
                Vector3 localOffset = leaf.Filter.transform.InverseTransformVector(offset);
                Vector3 localImpact = leaf.Filter.transform.InverseTransformPoint(impact);
                for (int i = 0; i < leaf.Rest.Length; i++)
                {
                    float proximity = 1f / (1f + 0.35f * (leaf.Rest[i] - localImpact).sqrMagnitude);
                    leaf.Deformed[i] = leaf.Rest[i] + localOffset * leaf.Weights[i] * proximity;
                }
                leaf.Working.vertices = leaf.Deformed;
                leaf.Working.RecalculateNormals();
                leaf.Working.RecalculateBounds();
            }
        }

        private void OnDisable()
        {
            running = touched = animating = false;
            offset = velocity = Vector3.zero;
            if (meshes == null) return;
            foreach (LeafMesh leaf in meshes)
            {
                leaf.Working.vertices = leaf.Rest;
                leaf.Working.normals = leaf.Original.normals;
                leaf.Working.bounds = leaf.Original.bounds;
            }
        }

        private void OnDestroy()
        {
            if (meshes == null) return;
            foreach (LeafMesh leaf in meshes)
            {
                if (leaf.Filter != null) leaf.Filter.sharedMesh = leaf.Original;
                if (leaf.Working != null) Destroy(leaf.Working);
            }
        }
    }
}
