using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball
{
    // One mesh and texture shared by the entire prepared route.
    public static class CloudPlatformVisuals
    {
        private const int CornerSteps = 6;
        private const int RingVertices = 4 * (CornerSteps + 1);

        public static Mesh CreateMesh(Vector3 size, float bevel, float cornerRadius)
        {
            var vertices = new List<Vector3>(500);
            var uv = new List<Vector2>(500);
            var triangles = new List<int>(2400);
            AddRing(vertices, uv, size.x - 2f * bevel, size.z - 2f * bevel, size.y * .5f, cornerRadius, size);
            AddRing(vertices, uv, size.x, size.z, size.y * .5f - bevel, cornerRadius + bevel, size);
            AddRing(vertices, uv, size.x, size.z, -size.y * .25f, cornerRadius + bevel, size);
            AddRing(vertices, uv, size.x * .82f, size.z * .82f, -size.y * .5f, cornerRadius, size);
            for (int ring = 0; ring < 3; ring++)
                for (int j = 0; j < RingVertices; j++)
                {
                    int a = ring * RingVertices + j;
                    int b = ring * RingVertices + (j + 1) % RingVertices;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + RingVertices);
                    triangles.Add(b); triangles.Add(b + RingVertices); triangles.Add(a + RingVertices);
                }
            int top = vertices.Count;
            vertices.Add(new Vector3(0f, size.y * .5f, 0f)); uv.Add(new Vector2(.5f, .5f));
            int bottom = vertices.Count;
            vertices.Add(new Vector3(0f, -size.y * .5f, 0f)); uv.Add(new Vector2(.5f, .5f));
            for (int j = 0; j < RingVertices; j++)
            {
                int next = (j + 1) % RingVertices;
                triangles.Add(top); triangles.Add(next); triangles.Add(j);
                triangles.Add(bottom); triangles.Add(3 * RingVertices + j); triangles.Add(3 * RingVertices + next);
            }
            // Soft lobes beneath the readable flat landing surface, merged into the same mesh.
            AddPuff(vertices, uv, triangles, new Vector3(-size.x * .25f, -size.y * .3f, 0f),
                new Vector3(size.x * .25f, size.y * .6f, size.z * .32f), size);
            AddPuff(vertices, uv, triangles, new Vector3(size.x * .23f, -size.y * .35f, size.z * .12f),
                new Vector3(size.x * .26f, size.y * .65f, size.z * .3f), size);
            AddPuff(vertices, uv, triangles, new Vector3(0f, -size.y * .4f, -size.z * .22f),
                new Vector3(size.x * .3f, size.y * .65f, size.z * .27f), size);
            var mesh = new Mesh { name = "Rounded Cloud Platform" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddRing(List<Vector3> vertices, List<Vector2> uv, float width,
            float depth, float y, float radius, Vector3 size)
        {
            radius = Mathf.Min(radius, Mathf.Min(width, depth) * .5f);
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = (corner == 0 || corner == 3 ? 1f : -1f) * (width * .5f - radius);
                float cz = (corner < 2 ? 1f : -1f) * (depth * .5f - radius);
                for (int j = 0; j <= CornerSteps; j++)
                {
                    float angle = (corner * 90f + j * 90f / CornerSteps) * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(cx + Mathf.Cos(angle) * radius, y, cz + Mathf.Sin(angle) * radius);
                    vertices.Add(p); uv.Add(new Vector2(p.x / size.x + .5f, p.z / size.z + .5f));
                }
            }
        }

        private static void AddPuff(List<Vector3> vertices, List<Vector2> uv, List<int> triangles,
            Vector3 center, Vector3 radii, Vector3 size)
        {
            const int rows = 6, columns = 12;
            int start = vertices.Count;
            for (int row = 0; row <= rows; row++)
                for (int column = 0; column <= columns; column++)
                {
                    float latitude = Mathf.PI * row / rows;
                    float longitude = 2f * Mathf.PI * column / columns;
                    Vector3 unit = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude),
                        Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                    Vector3 p = center + Vector3.Scale(unit, radii);
                    vertices.Add(p); uv.Add(new Vector2(p.x / size.x + .5f, p.z / size.z + .5f));
                }
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int a = start + row * (columns + 1) + column;
                    int b = a + columns + 1;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
        }

        public static Texture2D CreateTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Soft Cloud Mottling",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f), v = y / (size - 1f);
                    float noise = Mathf.PerlinNoise(u * 4f + 17f, v * 4f + 31f);
                    float edge = Mathf.Clamp01(1f - 1.4f * new Vector2(u - .5f, v - .5f).magnitude);
                    pixels[y * size + x] = Color.Lerp(new Color(.72f, .84f, .94f),
                        new Color(1f, .99f, .97f), Mathf.Clamp01(.2f + .5f * noise + .3f * edge));
                }
            texture.SetPixels(pixels); texture.Apply(true, true);
            return texture;
        }
    }
}
