using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    public static partial class EnvironmentVisualBuilder
    {
        private const string BackgroundReviewScene = "Assets/Scenes/EnvironmentPreview_Background_v02.unity";
        private const string BackgroundReviewOutput = "Artifacts/EnvironmentScreenshots/Background_v02";

        [MenuItem("Tools/Endless Rollball/Environment/Build Background v02 Review and Captures")]
        public static void BuildBackgroundReview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before building the background review.");

            Scene previous = SceneManager.GetActiveScene();
            Scene existing = SceneManager.GetSceneByPath(BackgroundReviewScene);
            if (existing.IsValid() && existing.isLoaded)
                throw new InvalidOperationException("Close EnvironmentPreview_Background_v02 before rebuilding it; its unsaved edits are preserved.");

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (ThemeSpec theme in Themes.Take(3))
            {
                var importer = AssetImporter.GetAtPath(theme.SkyboxTexturePath) as TextureImporter;
                if (importer == null) throw new FileNotFoundException(theme.SkyboxTexturePath);
                importer.textureType = TextureImporterType.Default;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.maxTextureSize = 4096;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            EnsureSkyboxMaterials();
            Scene review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(review);
            Directory.CreateDirectory(BackgroundReviewOutput);
            var stages = new List<GameObject>();
            var report = new List<string> { "Theme,Width,Height,SceneryColliders,SceneryRigidbodies" };
            try
            {
                Camera camera = CreateCamera();
                // Isolate rendering from all user scenes, including unsaved scenes.
                const int reviewLayer = 31;
                camera.cullingMask = 1 << reviewLayer;
                camera.transform.position = new Vector3(0f, 5.3f, -9f);
                camera.transform.LookAt(new Vector3(0f, 1.3f, 40f));
                foreach (ThemeSpec theme in Themes.Take(3))
                {
                    var stage = new GameObject(theme.RootName);
                    stages.Add(stage);
                    var scenery = new GameObject("VisualScenery_v02");
                    scenery.transform.SetParent(stage.transform, false);
                    var materials = ReviewMaterials(theme);
                    for (int i = 0; i < PreviewTrackSequence.Length; i++)
                    {
                        var track = InstantiatePrefab($"{TrackPrefabRoot}/{PreviewTrackSequence[i]}.prefab", stage.transform, new Vector3(0, 0, i * 24));
                        ApplyTrackMaterials(track, theme, materials);
                    }
                    // Sparse local landmarks provide parallax without blocking the panoramic surroundings.
                    string prop = theme.Code == "FS" ? "Tower" : theme.Code == "WP" ? "SlideTower" : "Pillar";
                    for (int i = 0; i < 4; i++)
                        foreach (int side in new[] { -1, 1 })
                        {
                            var item = InstantiatePrefab($"{PrefabRoot}/{theme.Folder}/ENV_{theme.Code}_{prop}.prefab", scenery.transform,
                                new Vector3(side * 13f, 0, 20 + i * 48));
                            PlaceOutsideRoute(item, side);
                        }
                    if (theme.Code == "WP")
                        BuildReviewPalms(scenery.transform);

                    var key = new GameObject("LGT_BackgroundReview_Key");
                    key.transform.SetParent(stage.transform, false);
                    key.transform.rotation = Quaternion.Euler(48, -35, 0);
                    var light = key.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.cullingMask = 1 << reviewLayer;
                    light.color = theme.Code == "AD" ? new Color(1, .78f, .48f) : new Color(.9f, .95f, 1);
                    light.intensity = theme.Code == "AD" ? 1.1f : 1.4f;
                    light.shadows = LightShadows.Soft;
                    var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    ball.name = "ReviewOnly_Sphere";
                    ball.transform.SetParent(stage.transform, false);
                    ball.transform.localPosition = new Vector3(0, 1, 4);
                    ball.transform.localScale = Vector3.one * 2;
                    UnityEngine.Object.DestroyImmediate(ball.GetComponent<Collider>());
                    ball.GetComponent<Renderer>().sharedMaterial = ReviewMaterial("Sphere", new Color(.7f,.73f,.77f), .9f, .8f);
                    foreach (Transform child in stage.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = reviewLayer;
                    stage.SetActive(false);
                    Texture texture = AssetDatabase.LoadAssetAtPath<Texture2D>(theme.SkyboxTexturePath);
                    report.Add($"{theme.Folder},{texture.width},{texture.height},{scenery.GetComponentsInChildren<Collider>(true).Length},{scenery.GetComponentsInChildren<Rigidbody>(true).Length}");
                }

                for (int i = 0; i < 3; i++)
                {
                    stages[i].SetActive(true);
                    ApplyReviewSettings(Themes[i], camera);
                    RenderCamera(camera, $"{BackgroundReviewOutput}/{Themes[i].Code}_Gameplay.png");
                    Quaternion rotation = camera.transform.rotation;
                    Vector3 position = camera.transform.position;
                    // Sky-only cardinal captures expose seams and horizon distortion for review.
                    int mask = camera.cullingMask;
                    camera.cullingMask = 0;
                    for (int yaw = 0; yaw < 360; yaw += 90)
                    {
                        camera.transform.rotation = Quaternion.Euler(0, yaw, 0);
                        RenderCamera(camera, $"{BackgroundReviewOutput}/{Themes[i].Code}_Sky_{yaw}.png");
                    }
                    camera.cullingMask = mask;
                    camera.transform.SetPositionAndRotation(position, rotation);
                    stages[i].SetActive(false);
                }
                stages[0].SetActive(true);
                ApplyReviewSettings(Themes[0], camera);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(review, BackgroundReviewScene);
                File.WriteAllLines($"{BackgroundReviewOutput}/Audit.csv", report);
                Debug.Log("Background v02 review and gameplay/cardinal captures saved to " + BackgroundReviewOutput);
            }
            finally
            {
                EditorSceneManager.CloseScene(review, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void ApplyReviewSettings(ThemeSpec theme, Camera camera)
        {
            ApplyRenderSettings(theme, camera);
            RenderSettings.ambientSkyColor = theme.Code == "AD" ? new Color(.27f,.32f,.35f) : new Color(.57f,.67f,.78f);
            RenderSettings.ambientEquatorColor = theme.Code == "AD" ? new Color(.3f,.25f,.18f) : new Color(.38f,.48f,.6f);
            RenderSettings.fogColor = theme.Code == "AD" ? new Color(.12f,.18f,.2f) : new Color(.45f,.64f,.77f);
            RenderSettings.fogStartDistance = 110;
            RenderSettings.fogEndDistance = 260;
        }

        private static Material ReviewMaterial(string name, Color color, float metallic = 0, float smoothness = .35f)
        {
            const string folder = MaterialRoot + "/Shared/BackgroundReview_v02";
            EnsureFolder(MaterialRoot, "Shared");
            EnsureFolder(MaterialRoot + "/Shared", "BackgroundReview_v02");
            string path = folder + "/MAT_ENV_" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Dictionary<string, Material> ReviewMaterials(ThemeSpec theme)
        {
            var result = new Dictionary<string, Material>();
            Color surface = theme.Code == "FS" ? new Color(.26f,.34f,.43f) : theme.Code == "WP" ? new Color(.82f,.76f,.59f) : new Color(.4f,.32f,.21f);
            result[theme.Code + "_TrackSurface"] = ReviewMaterial(theme.Code + "_Surface", surface);
            result[theme.Code + "_TrackEdge"] = ReviewMaterial("RouteYellow", new Color(.96f,.73f,.12f));
            result[theme.Code + "_TrackSupport"] = ReviewMaterial(theme.Code + "_Support", surface * .5f);
            result["Shared_HazardRed"] = ReviewMaterial("HazardRed", new Color(.85f,.08f,.03f));
            result["Shared_HazardOrange"] = ReviewMaterial("HazardOrange", new Color(1,.3f,.03f));
            return result;
        }

        private static void PlaceOutsideRoute(GameObject item, int side)
        {
            var renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            float delta = side < 0 ? Mathf.Min(0, -5.5f - bounds.max.x) : Mathf.Max(0, 5.5f - bounds.min.x);
            item.transform.position += new Vector3(delta, -bounds.min.y, 0);
        }

        private static void BuildReviewPalms(Transform parent)
        {
            // Original review geometry: no imported pack licence dependency.
            Material trunk = ReviewMaterial("PalmTrunk", new Color(.36f,.23f,.1f));
            Material leaf = ReviewMaterial("PalmLeaf", new Color(.15f,.42f,.16f));
            foreach (int side in new[] { -1, 1 })
                for (int i = 0; i < 4; i++)
                {
                    var palm = new GameObject("ENV_WP_ReviewPalm");
                    palm.transform.SetParent(parent, false);
                    palm.transform.localPosition = new Vector3(side * 10, 0, 8 + 48 * i);
                    ReviewPrimitive(palm.transform, PrimitiveType.Cylinder, new Vector3(0, 2.8f, 0), new Vector3(.3f,2.8f,.3f), Quaternion.identity, trunk);
                    for (int j = 0; j < 7; j++)
                    {
                        float angle = j * Mathf.PI * 2 / 7;
                        ReviewPrimitive(palm.transform, PrimitiveType.Sphere, new Vector3(Mathf.Sin(angle)*1.1f,5.4f,Mathf.Cos(angle)*1.1f), new Vector3(.65f,.16f,3), Quaternion.Euler(18,j*360f/7,0), leaf);
                    }
                }
        }

        private static void ReviewPrimitive(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Material mat)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = "SM_ReviewPalm_" + type;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.transform.localRotation = rotation;
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            item.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
