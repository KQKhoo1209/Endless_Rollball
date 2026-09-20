using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EndlessRollball.Editor.Environment
{
    public static class ImportedStreetAssetAudit
    {
        public const string CityModelPath =
            "Assets/EndlessRollball/Prefabs/Environment/Street/source/city.fbx";
        public const string SmallTreesModelPath =
            "Assets/EndlessRollball/Prefabs/Environment/Trees/small-trees/source/Small Trees.fbx";
        public const string PalmTreesModelPath =
            "Assets/EndlessRollball/Prefabs/Environment/Trees/palm-trees/source/PalmTrees.fbx";

        private const string ReportPath = "Artifacts/EnvironmentAssetAudit/ImportedStreetAssets.md";
        private const string SessionAuditKey = "EndlessRollball.ImportedStreetAssetAudit.Version";
        private const int AuditVersion = 2;

        [InitializeOnLoadMethod]
        private static void ScheduleAudit()
        {
            if (Application.isBatchMode || SessionState.GetInt(SessionAuditKey, 0) >= AuditVersion)
            {
                return;
            }

            SessionState.SetInt(SessionAuditKey, AuditVersion);
            EditorApplication.delayCall += AuditImportedAssets;
        }

        [MenuItem("Tools/Endless Rollball/Environment/Audit Imported Street Assets")]
        public static void AuditImportedAssets()
        {
            string fullPath = Path.GetFullPath(ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? string.Empty);

            StringBuilder report = new StringBuilder(32_768);
            report.AppendLine("# Imported Street Asset Audit");
            report.AppendLine();
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();
            AppendModelReport(report, "Stylized city and buildings", CityModelPath);
            AppendModelReport(report, "Small trees", SmallTreesModelPath);
            AppendModelReport(report, "Palm trees", PalmTreesModelPath);

            File.WriteAllText(fullPath, report.ToString(), Encoding.UTF8);
            Debug.Log($"Imported Street asset audit written to {fullPath}.");
        }

        private static void AppendModelReport(StringBuilder report, string heading, string assetPath)
        {
            report.AppendLine($"## {heading}");
            report.AppendLine();
            report.AppendLine($"Asset: `{assetPath}`");
            report.AppendLine();

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null)
            {
                report.AppendLine("Status: **Missing or not imported**");
                report.AppendLine();
                return;
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            int triangles = renderers.Sum(GetTriangleCount);
            Bounds bounds = CalculateModelBounds(model.transform, renderers);

            report.AppendLine($"- Renderers: {renderers.Length}");
            report.AppendLine($"- Evaluated triangles: {triangles:N0}");
            report.AppendLine($"- Imported colliders: {colliders.Length}");
            report.AppendLine($"- Source bounds: {FormatVector(bounds.size)}");
            report.AppendLine($"- Root children: {model.transform.childCount}");
            report.AppendLine();
            report.AppendLine("| Hierarchy path | Mesh | Triangles | Bounds | Materials |");
            report.AppendLine("|---|---:|---:|---:|---|");

            foreach (Renderer renderer in renderers
                         .OrderBy(item => GetHierarchyPath(model.transform, item.transform), StringComparer.Ordinal))
            {
                string meshName = GetMesh(renderer)?.name ?? "-";
                string materials = string.Join(", ", renderer.sharedMaterials
                    .Where(material => material != null)
                    .Select(material => material.name)
                    .Distinct());
                report.AppendLine(
                    $"| `{GetHierarchyPath(model.transform, renderer.transform)}` | " +
                    $"{EscapeTable(meshName)} | {GetTriangleCount(renderer):N0} | " +
                    $"{FormatVector(renderer.bounds.size)} | {EscapeTable(materials)} |");
            }

            report.AppendLine();
        }

        private static Bounds CalculateModelBounds(Transform root, IReadOnlyList<Renderer> renderers)
        {
            if (renderers.Count == 0)
            {
                return new Bounds(root.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Count; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static int GetTriangleCount(Renderer renderer)
        {
            Mesh mesh = GetMesh(renderer);
            if (mesh == null)
            {
                return 0;
            }

            long indexCount = 0;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                indexCount += (long)mesh.GetIndexCount(subMesh);
            }

            return (int)Math.Min(int.MaxValue, indexCount / 3L);
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            return renderer.GetComponent<MeshFilter>()?.sharedMesh;
        }

        private static string GetHierarchyPath(Transform root, Transform item)
        {
            Stack<string> parts = new Stack<string>();
            Transform current = item;
            while (current != null && current != root)
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return parts.Count == 0 ? root.name : string.Join("/", parts);
        }

        private static string FormatVector(Vector3 value)
        {
            return $"{value.x:0.###} × {value.y:0.###} × {value.z:0.###} m";
        }

        private static string EscapeTable(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace("|", "\\|");
        }
    }
}
