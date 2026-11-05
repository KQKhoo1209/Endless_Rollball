using System;
using EndlessRollball;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Export the runtime cloud's exact visuals once, without rebuilding existing assets.
public static class CloudBouncePrefabBuilder
{
    private const string Folder = "Assets/EndlessRollballAssets/CloudTheme/CloudBouncePlatform";
    private const string PrefabPath = Folder + "/CloudBouncePlatform.prefab";

    [MenuItem("Tools/Cloud Event/Create Bounce Platform Prefab")]
    public static void CreatePrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play Mode before creating the cloud platform prefab.");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        if (!AssetDatabase.IsValidFolder(Folder))
            throw new InvalidOperationException("The CloudBouncePlatform destination folder is missing.");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("A Lit shader is required.");

        Vector3 size = new Vector3(2.6f, .65f, 2.4f);
        const float bevel = .16f;
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/CloudPlatformMesh.asset");
        if (mesh == null)
        {
            mesh = CloudPlatformVisuals.CreateMesh(size, bevel, .45f);
            AssetDatabase.CreateAsset(mesh, Folder + "/CloudPlatformMesh.asset");
        }
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/CloudPlatformTexture.asset");
        if (texture == null)
        {
            texture = CloudPlatformVisuals.CreateTexture();
            AssetDatabase.CreateAsset(texture, Folder + "/CloudPlatformTexture.asset");
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/CloudPlatformMaterial.mat");
        if (material == null)
        {
            material = new Material(shader) { name = "Cloud Platform", color = Color.white, enableInstancing = true };
            material.mainTexture = texture;
            material.SetFloat("_Smoothness", .12f);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, Folder + "/CloudPlatformMaterial.mat");
        }

        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("CloudBouncePlatform");
            SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            BoxCollider collider = root.AddComponent<BoxCollider>();
            // Match the flat landing surface rather than the decorative lower puffs.
            collider.center = Vector3.zero;
            collider.size = new Vector3(size.x - 2f * bevel, size.y, size.z - 2f * bevel);
            collider.isTrigger = false;
            root.AddComponent<CloudBouncePlatform>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Cloud prefab could not be saved.");
            AssetDatabase.SaveAssets();
            Debug.Log("Created cloud bounce prefab: " + PrefabPath, prefab);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
