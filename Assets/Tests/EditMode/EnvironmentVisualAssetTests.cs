using System;
using System.IO;
using System.Linq;
using EndlessRollball.Track;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EndlessRollball.Tests
{
    public sealed class EnvironmentVisualAssetTests
    {
        private const string EnvironmentPrefabRoot = "Assets/EndlessRollball/Prefabs/Environment";
        private const string EnvironmentArtRoot = "Assets/EndlessRollball/Art/Environment";

        [Test]
        public void EnvironmentPrefabsAreVisualOnly()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { EnvironmentPrefabRoot });
            Assert.That(guids.Length, Is.GreaterThanOrEqualTo(48));

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, path);
                Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty, path);
                Assert.That(prefab.GetComponentsInChildren<TrackSegment>(true), Is.Empty, path);
                Assert.That(prefab.GetComponentsInChildren<MovingObstacle>(true), Is.Empty, path);
                Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty, path);
            }
        }

        [Test]
        public void RoadsideModulesUseTwentyFourMetreContractAndKeepRouteClear()
        {
            string[] themeCodes = { "FS", "WP", "AD" };
            string[] themeFolders = { "FuturisticStreet", "WaterThemePark", "AncientDungeon" };

            for (int theme = 0; theme < themeCodes.Length; theme++)
            {
                foreach (string variant in new[] { "A", "B", "C" })
                {
                    string path = $"{EnvironmentPrefabRoot}/{themeFolders[theme]}/ENV_{themeCodes[theme]}_Roadside_{variant}.prefab";
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Assert.That(prefab, Is.Not.Null, path);
                    ValidateModuleBounds(prefab, path, validateClearance: true);
                }
            }
        }

        [Test]
        public void TransitionTunnelModulesUseTwentyFourMetreContractAndPreserveEnvelope()
        {
            foreach (string name in new[] { "ENV_TR_Entrance", "ENV_TR_Middle", "ENV_TR_Exit" })
            {
                string path = $"{EnvironmentPrefabRoot}/TransitionTunnel/{name}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                ValidateModuleBounds(prefab, path, validateClearance: false);

                GameObject instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    {
                        Bounds bounds = renderer.bounds;
                        bool crossesDriveableWidth = bounds.min.x < 4.5f && bounds.max.x > -4.5f;
                        bool isFloorMarker = bounds.max.y <= 0.2f;
                        if (crossesDriveableWidth && !isFloorMarker)
                        {
                            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(5.92f),
                                $"{path}: {renderer.name} reduces the 6 m clear height.");
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void EnvironmentModelsImportAtMetricScaleWithoutAnimationOrMaterials()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { EnvironmentArtRoot });
            Assert.That(guids.Length, Is.EqualTo(40));

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.globalScale, Is.EqualTo(1f).Within(0.0001f), path);
                Assert.That(importer.importAnimation, Is.False, path);
                Assert.That(importer.materialImportMode, Is.EqualTo(ModelImporterMaterialImportMode.None), path);
            }
        }

        [Test]
        public void PreviewSceneAndChapterFourCapturesExistAtFullHd()
        {
            Assert.That(File.Exists("Assets/Scenes/EnvironmentPreview.unity"), Is.True);
            string folder = Path.GetFullPath("Artifacts/EnvironmentScreenshots");
            string[] names =
            {
                "ER_ENV_FuturisticStreet_01.png",
                "ER_ENV_WaterThemePark_01.png",
                "ER_ENV_AncientDungeon_01.png",
                "ER_ENV_TransitionTunnel_01.png"
            };

            foreach (string name in names)
            {
                string path = Path.Combine(folder, name);
                Assert.That(File.Exists(path), Is.True, path);
                Texture2D image = new Texture2D(2, 2, TextureFormat.RGB24, false);
                try
                {
                    Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True, path);
                    Assert.That(image.width, Is.EqualTo(1920), path);
                    Assert.That(image.height, Is.EqualTo(1080), path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(image);
                }
            }
        }

        private static void ValidateModuleBounds(GameObject prefab, string path, bool validateClearance)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers, Is.Not.Empty, path);
                Bounds combined = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1))
                {
                    combined.Encapsulate(renderer.bounds);
                }

                Assert.That(combined.min.z, Is.GreaterThanOrEqualTo(-0.08f), path);
                Assert.That(combined.max.z, Is.LessThanOrEqualTo(24.08f), path);

                if (!validateClearance)
                {
                    return;
                }

                foreach (Renderer renderer in renderers)
                {
                    Bounds bounds = renderer.bounds;
                    bool belowRoute = bounds.max.y < -0.5f;
                    bool outsideRoute = bounds.max.x <= -5.5f || bounds.min.x >= 5.5f;
                    bool overhead = bounds.min.y >= 6f;
                    Assert.That(belowRoute || outsideRoute || overhead, Is.True,
                        $"{path}: {renderer.name} intrudes into the protected route envelope ({bounds}).");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
