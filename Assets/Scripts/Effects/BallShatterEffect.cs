using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Track
{
    // Presentation owned by the run controller. No colliders, rigidbodies, global RNG or asset edits.
    internal sealed class BallShatterEffect
    {
        private sealed class Piece
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Vector3 Centre, Start, Velocity, Spin;
            public Quaternion Rotation;
        }

        private GameObject root;
        private readonly List<Piece> pieces = new List<Piece>();
        private Renderer[] originals = Array.Empty<Renderer>();
        private bool[] originalVisibility = Array.Empty<bool>();
        internal void Play(BallMovement ball)
        {
            Stop();
            Renderer source = null;
            Renderer[] renderers = ball.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.sharedMaterial != null)
                {
                    source = renderer;
                    break;
                }

            if (source == null)
                return; // Keep the original visible if there is no usable appearance.
            if (root == null)
                Build(ball.gameObject.scene);
            SphereCollider sphere = ball.GetComponent<SphereCollider>();
            Vector3 scale = sphere.transform.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 origin = sphere.transform.TransformPoint(sphere.center);
            Quaternion rotation = sphere.transform.rotation;
            root.transform.SetPositionAndRotation(origin, rotation);
            root.transform.localScale = Vector3.one * radius;
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                float variation = Mathf.Repeat(i * .618034f, 1f);
                Vector3 outward = rotation * piece.Centre.normalized;
                piece.Start = origin + rotation * (piece.Centre * radius);
                piece.Velocity = outward * Mathf.Lerp(1.8f, 2.8f, variation) + Vector3.up * 1.7f;
                piece.Spin = new Vector3(90f + i * 13f, -180f + i * 23f, 120f - i * 17f);
                piece.Rotation = rotation;
                piece.Transform.SetPositionAndRotation(piece.Start, rotation);
                piece.Transform.localScale = Vector3.one;
                piece.Renderer.sharedMaterial = source.sharedMaterial;
            }

            originals = renderers;
            originalVisibility = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                originalVisibility[i] = renderers[i].enabled;
                renderers[i].enabled = false;
            }

            root.SetActive(true);
        }

        internal void Sample(float elapsed, float duration)
        {
            if (root == null || !root.activeSelf)
                return;
            float t = Mathf.Clamp(elapsed, 0f, Mathf.Max(0f, duration));
            float progress = duration > 0f ? Mathf.Clamp01(t / duration) : 1f;
            float size = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.6f, 1f, progress));
            foreach (Piece piece in pieces)
            {
                piece.Transform.SetPositionAndRotation(piece.Start + piece.Velocity * t + Vector3.down * (2.5f * t * t), piece.Rotation * Quaternion.Euler(piece.Spin * t));
                piece.Transform.localScale = Vector3.one * size;
            }
        }

        internal void Stop()
        {
            if (root != null)
                root.SetActive(false);
            for (int i = 0; i < originals.Length; i++)
                if (originals[i] != null)
                    originals[i].enabled = originalVisibility[i];
            originals = Array.Empty<Renderer>();
            originalVisibility = Array.Empty<bool>();
        }

        internal void Dispose()
        {
            Stop();
            foreach (Piece piece in pieces)
                if (piece.Mesh != null)
                    UnityEngine.Object.Destroy(piece.Mesh);
            pieces.Clear();
            if (root != null)
                UnityEngine.Object.Destroy(root);
            root = null;
        }

        private void Build(Scene scene)
        {
            root = new GameObject("BallShatter_Runtime");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.SetActive(false);
            const int rings = 3, sectors = 6;
            for (int ring = 0; ring < rings; ring++)
                for (int sector = 0; sector < sectors; sector++)
                {
                    float v0 = ring / (float)rings, v1 = (ring + 1f) / rings;
                    float u0 = sector / (float)sectors, u1 = (sector + 1f) / sectors;
                    Vector3 centre = Surface((u0 + u1) * .5f, (v0 + v1) * .5f) * .55f;
                    var go = new GameObject($"BallFragment_{ring}_{sector}", typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(root.transform, false);
                    Mesh mesh = BuildWedge(u0, u1, v0, v1, centre);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    pieces.Add(new Piece { Transform = go.transform, Renderer = go.GetComponent<MeshRenderer>(), Mesh = mesh, Centre = centre });
                }
        }

        private static Vector3 Surface(float u, float v)
        {
            float longitude = u * Mathf.PI * 2f, latitude = v * Mathf.PI;
            return new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
        }

        private static Mesh BuildWedge(float u0, float u1, float v0, float v1, Vector3 centre)
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            const int subdivisions = 3;
            Vector3 Point(int x, int y) => Surface(Mathf.Lerp(u0, u1, x / (float)subdivisions), Mathf.Lerp(v0, v1, y / (float)subdivisions));
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < .00000001f)
                    return; // Pole triangles have coincident vertices.
                if (Vector3.Dot(normal, (a + b + c) / 3f - centre) < 0f)
                {
                    Vector3 swap = b;
                    b = c;
                    c = swap;
                }

                foreach (Vector3 point in new[]
                {
                    a,
                    b,
                    c
                }

                )
                {
                    triangles.Add(vertices.Count);
                    vertices.Add(point - centre);
                    uv.Add(new Vector2(Mathf.Repeat(Mathf.Atan2(point.z, point.x) / (Mathf.PI * 2f), 1f), 1f - Mathf.Acos(Mathf.Clamp(point.y, -1f, 1f)) / Mathf.PI));
                }
            }

            for (int y = 0; y < subdivisions; y++)
                for (int x = 0; x < subdivisions; x++)
                {
                    Face(Point(x, y), Point(x + 1, y), Point(x + 1, y + 1));
                    Face(Point(x, y), Point(x + 1, y + 1), Point(x, y + 1));
                }

            for (int edge = 0; edge < subdivisions; edge++)
            {
                Face(Vector3.zero, Point(edge, 0), Point(edge + 1, 0));
                Face(Vector3.zero, Point(edge, subdivisions), Point(edge + 1, subdivisions));
                Face(Vector3.zero, Point(0, edge), Point(0, edge + 1));
                Face(Vector3.zero, Point(subdivisions, edge), Point(subdivisions, edge + 1));
            }

            var mesh = new Mesh
            {
                name = "BallFragment_Runtime"
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
