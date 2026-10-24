using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // Create-only authoring. No runtime obstacle scripts or scene edits.
    internal static class SharedObstaclePrefabBuilder
    {
        const string Root = "Assets/EndlessRollballAssets/Obstacles/Shared";
        const string Kenney = "Assets/EndlessRollballAssets/Obstacles/kenney_survival-kit";
        const string Kay = "Assets/EndlessRollballAssets/Obstacles/KayKit_ResourceBits_1.0_FREE";
        const string Request = "Artifacts/SharedObstacles/Create.request";
        const string RefineRequest = "Artifacts/SharedObstacles/NeutralBarrels.request";
        const string Report = "Artifacts/SharedObstacles/AuthoringReport.txt";
        static readonly string[] Names = { "CrateStack", "BarrelCluster", "StoneStack", "RockCluster", "LowTimberBundle", "LowLogPair" };
        static readonly string[] Roles = { "NavigationCandidate", "NavigationCandidate", "NavigationCandidate", "NavigationCandidate", "JumpOverCandidate", "JumpOverCandidate" };
        static readonly Queue<string> PreviewQueue = new Queue<string>();
        static double deadline;
        static double nextPreviewAttempt;
        static string activeRequest;
        static string previewDirectory = "Artifacts/SharedObstacles/Previews";

        [Serializable] class Manifest
        {
            public string scope = "Static physics-ready prefab art. Not spawned, scored, moving or gameplay-accepted.";
            public string modifications = "Source meshes/atlases unchanged; uniform scale, grouping, simple compound collision and URP material overrides on new prefabs only.";
            public string[] materials;
            public Source[] sources;
            public Entry[] obstacles;
        }
        [Serializable] class Source
        {
            public string author, url, license, licenseFile;
        }
        [Serializable] class Entry
        {
            public string name, intendedRole, destination;
            public Vector3 size, colliderSize;
            public int colliderCount, triangles;
            public string[] modelSources;
        }

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            activeRequest = File.Exists(RefineRequest) ? RefineRequest : Request;
            if (Application.isBatchMode || !File.Exists(activeRequest)) return;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(activeRequest) > TimeSpan.FromMinutes(30)) return;
            string expected = activeRequest == RefineRequest ? "neutral-shared-barrel-material-v1" : "create-six-shared-obstacles-v1";
            if (File.ReadAllText(activeRequest).Trim() != expected) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += RunRequest;
        }

        static void RunRequest()
        {
            if ((EditorApplication.isCompiling || EditorApplication.isUpdating) &&
                EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= RunRequest;
            if (!File.Exists(activeRequest)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                File.WriteAllText(activeRequest + ".blocked", "Exit Play Mode/Prefab Mode before shared-obstacle authoring.");
                return;
            }
            try { if (activeRequest == RefineRequest) NeutralizeBarrels(); else Build(); }
            catch (Exception error)
            {
                File.WriteAllText(activeRequest + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Endless Rollball/Obstacles/Create Shared Obstacle Prefabs")]
        static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play Mode and Prefab Mode first.");
            foreach (string name in Names)
                if (File.Exists(PrefabPath(name))) throw new IOException("Existing prefab preserved: " + PrefabPath(name));
            foreach (string name in new[] { "Kenney", "KayKit", "BarrelWood" })
                if (File.Exists(MaterialPath(name))) throw new IOException("Existing material preserved: " + MaterialPath(name));
            if (File.Exists(Root + "/AssetManifest.json")) throw new IOException("Existing manifest preserved.");
            string[] sourcePaths = { K("box"), K("barrel"), K("rock-a"), K("rock-b"), Y("Stone_Bricks_Stack_Large"), Y("Wood_Planks_Stack_Medium"), Y("Wood_Log_A") };
            foreach (string path in sourcePaths)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!model || model.GetComponentsInChildren<MeshRenderer>(true).Length == 0)
                    throw new FileNotFoundException("Missing imported model: " + path);
            }
            var kenneyAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Kenney + "/Models/Textures/variation-a.png");
            var kayAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(Kay + "/Textures/resource_bits_texture.png");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!kenneyAtlas || !kayAtlas || !shader) throw new InvalidOperationException("Missing pack atlas or URP/Lit shader.");
            if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Materials");
            Directory.CreateDirectory("Artifacts/SharedObstacles/Previews");
            Material kenneyMat = CreateMaterial("Kenney", kenneyAtlas, shader);
            Material kayMat = CreateMaterial("KayKit", kayAtlas, shader);
            Material barrelMat = CreateBarrelMaterial(kenneyMat);
            Scene staging = EditorSceneManager.NewPreviewScene();
            var entries = new List<Entry>();
            var report = new StringBuilder("SHARED_OBSTACLES_SAVED\nSix static shared-theme obstacle prefabs. No scene edits or Play Mode/tests.\n");
            try
            {
                for (int index = 0; index < Names.Length; index++)
                {
                    var root = new GameObject("OBS_Shared_" + Names[index]);
                    SceneManager.MoveGameObjectToScene(root, staging);
                    var visual = new GameObject("Visual").transform;
                    visual.SetParent(root.transform, false);
                    var collision = new GameObject("Collision").transform;
                    collision.SetParent(root.transform, false);
                    var used = new HashSet<string>();
                    Action<string, string, Material, Vector3, Vector3, bool, Quaternion> part =
                        (label, path, material, maxSize, groundPosition, capsule, rotation) =>
                        AddPart(visual, collision, label, path, material, maxSize, groundPosition, capsule, rotation, used, staging);
                    Quaternion identity = Quaternion.identity;
                    switch (index)
                    {
                        case 0:
                            // Cubical source crates: two base crates and one supported above.
                            part("Crate_Left", K("box"), kenneyMat, Vector3.one * 1.15f, new Vector3(-0.605f, 0, 0), false, identity);
                            part("Crate_Right", K("box"), kenneyMat, Vector3.one * 1.15f, new Vector3(0.605f, 0, 0), false, identity);
                            part("Crate_Top", K("box"), kenneyMat, Vector3.one * 1.15f, new Vector3(0, 1.15f, 0), false, Quaternion.Euler(0, 90, 0));
                            break;
                        case 1:
                            part("Barrel_Left", K("barrel"), barrelMat, new Vector3(1, 1.2f, 1), new Vector3(-0.44f, 0, 0), true, identity);
                            part("Barrel_Right", K("barrel"), barrelMat, new Vector3(1, 1.2f, 1), new Vector3(0.44f, 0, 0), true, identity);
                            part("Barrel_Top", K("barrel"), barrelMat, new Vector3(1, 1.2f, 1), new Vector3(0, 1.2f, 0), true, identity);
                            break;
                        case 2:
                            Bounds bottom = AddPart(visual, collision, "Stone_Base", Y("Stone_Bricks_Stack_Large"), kayMat,
                                new Vector3(2.4f, 1.25f, 1.65f), Vector3.zero, false, identity, used, staging);
                            part("Stone_Top", Y("Stone_Bricks_Stack_Large"), kayMat, new Vector3(2.4f, 1.25f, 1.65f),
                                new Vector3(0, bottom.max.y, 0), false, Quaternion.Euler(0, 180, 0));
                            break;
                        case 3:
                            part("Rock_Left", K("rock-a"), kenneyMat, new Vector3(1.15f, 1.15f, 1.35f), new Vector3(-0.57f, 0, 0), false, Quaternion.Euler(0, 25, 0));
                            part("Rock_Right", K("rock-b"), kenneyMat, new Vector3(1.15f, 1.5f, 1.35f), new Vector3(0.57f, 0, 0), false, Quaternion.Euler(0, -20, 0));
                            Bounds support = RendererBounds(visual.gameObject);
                            part("Rock_Cap", K("rock-a"), kenneyMat, new Vector3(1.15f, 1.1f, 1.25f),
                                new Vector3(0, support.max.y - 0.15f, 0), false, Quaternion.Euler(0, 70, 0));
                            break;
                        case 4:
                            part("Timber_Left", Y("Wood_Planks_Stack_Medium"), kayMat, new Vector3(1.15f, 0.65f, 1.15f), new Vector3(-0.6f, 0, 0), false, identity);
                            part("Timber_Right", Y("Wood_Planks_Stack_Medium"), kayMat, new Vector3(1.15f, 0.65f, 1.15f), new Vector3(0.6f, 0, 0), false, Quaternion.Euler(0, 180, 0));
                            break;
                        case 5:
                            part("Log_Left", Y("Wood_Log_A"), kayMat, new Vector3(1.2f, 0.65f, 0.7f), new Vector3(-0.625f, 0, 0), true, Quaternion.Euler(0, 90, 0));
                            part("Log_Right", Y("Wood_Log_A"), kayMat, new Vector3(1.2f, 0.65f, 0.7f), new Vector3(0.625f, 0, 0), true, Quaternion.Euler(0, 270, 0));
                            break;
                    }
                    Bounds bounds = RendererBounds(root);
                    Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                    if (root.GetComponentsInChildren<Rigidbody>(true).Length != 0 || root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 ||
                        colliders.Any(c => c.isTrigger || !c.enabled || !c.transform.IsChildOf(collision)))
                        throw new InvalidOperationException("Unexpected gameplay component/collider in " + root.name);
                    if (Mathf.Abs(bounds.min.y) > 0.005f || bounds.size.x > 2.65f)
                        throw new InvalidOperationException("Ground pivot or single-lane width failed for " + root.name);
                    // Preview scenes need not have a synchronized physics world.
                    // Read the authored shapes instead of querying PhysX bounds.
                    Bounds collisionBounds = ShapeBounds(colliders[0]);
                    foreach (Collider collider in colliders.Skip(1)) collisionBounds.Encapsulate(ShapeBounds(collider));
                    int triangles = root.GetComponentsInChildren<MeshFilter>(true).Sum(mf =>
                    {
                        int count = 0;
                        for (int sub = 0; sub < mf.sharedMesh.subMeshCount; sub++) count += (int)mf.sharedMesh.GetIndexCount(sub) / 3;
                        return count;
                    });
                    string path = PrefabPath(Names[index]);
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                    if (!success) throw new IOException("Could not save " + path);
                    AssetDatabase.SetLabels(saved, new[] { "EndlessRollball", "SharedObstacle", Roles[index] });
                    entries.Add(new Entry { name = root.name, intendedRole = Roles[index], destination = path, size = bounds.size,
                        colliderSize = collisionBounds.size, colliderCount = colliders.Length, triangles = triangles, modelSources = used.OrderBy(p => p).ToArray() });
                    report.AppendLine(root.name + " | visual=" + bounds.size.ToString("F3") + " | colliders=" + colliders.Length + " | triangles=" + triangles + " | " + Roles[index]);
                    PreviewQueue.Enqueue(path);
                    Object.DestroyImmediate(root);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(staging); }
            var manifest = new Manifest
            {
                sources = new[]
                {
                    new Source { author="Kenney", url="https://kenney.nl/assets/survival-kit", license="CC0", licenseFile=Kenney+"/License.txt" },
                    new Source { author="Kay Lousberg", url="https://kaylousberg.itch.io/resource-bits", license="CC0", licenseFile=Kay+"/License.txt" }
                },
                materials = new[] { MaterialPath("Kenney"), MaterialPath("KayKit"), MaterialPath("BarrelWood") },
                obstacles = entries.ToArray()
            };
            File.WriteAllText(Root + "/AssetManifest.json", JsonUtility.ToJson(manifest, true));
            AssetDatabase.ImportAsset(Root + "/AssetManifest.json");
            report.AppendLine("Roots: identity transforms, ground Y=0, +Z approach. Collider shapes use the same visual bounds.");
            report.AppendLine("URP/Lit atlas materials reuse the imported textures. Original models/import settings/materials unchanged.");
            report.AppendLine("No spawning, moving logic, scored-task triggers, floor/gap changes or difficulty changes.");
            report.AppendLine("Low obstacles are jump-over candidates only; jump acceptance/landing/scoring are not implemented or tested.");
            File.WriteAllText(Report, report.ToString());
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += SavePreviews;
            Debug.Log("SHARED_OBSTACLES_SAVED: " + Report);
        }

        internal static Bounds AddPart(Transform visual, Transform collision, string name, string source, Material material,
            Vector3 maxSize, Vector3 position, bool capsule, Quaternion rotation, HashSet<string> used, Scene scene)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(visual, false);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(source), scene);
            instance.transform.SetParent(holder, false);
            // Only render data is kept from each source instance.
            foreach (Component component in instance.GetComponentsInChildren<Component>(true))
                if (component is Collider || component is Rigidbody || component is Animator || component is Animation ||
                    component is MonoBehaviour || component is Camera || component is Light) Object.DestroyImmediate(component);
            if (material)
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
            holder.localRotation = rotation;
            Bounds bounds = RendererBounds(holder.gameObject);
            if (bounds.size.x < 0.0001f || bounds.size.y < 0.0001f || bounds.size.z < 0.0001f)
                throw new InvalidOperationException("Degenerate imported model: " + source);
            float scale = Mathf.Min(maxSize.x / bounds.size.x, Mathf.Min(maxSize.y / bounds.size.y, maxSize.z / bounds.size.z));
            holder.localScale = Vector3.one * scale; // Preserve source proportions.
            bounds = RendererBounds(holder.gameObject);
            holder.localPosition += position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = RendererBounds(holder.gameObject);
            var owner = new GameObject("COL_" + name).transform;
            owner.SetParent(collision, false);
            owner.localPosition = bounds.center;
            if (capsule)
            {
                var shape = owner.gameObject.AddComponent<CapsuleCollider>();
                int axis = bounds.size.y > bounds.size.x ? 1 : 0;
                shape.direction = axis;
                shape.height = axis == 1 ? bounds.size.y : bounds.size.x;
                shape.radius = axis == 1 ? Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f : Mathf.Max(bounds.size.y, bounds.size.z) * 0.5f;
            }
            else owner.gameObject.AddComponent<BoxCollider>().size = bounds.size;
            used.Add(source);
            return bounds;
        }

        internal static Bounds RendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No renderers in " + root.name);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static Bounds ShapeBounds(Collider collider)
        {
            if (collider is BoxCollider box)
                return new Bounds(box.transform.TransformPoint(box.center), box.size);
            var capsule = (CapsuleCollider)collider;
            Vector3 size = Vector3.one * capsule.radius * 2f;
            size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
            return new Bounds(capsule.transform.TransformPoint(capsule.center), size);
        }
        static Material CreateBarrelMaterial(Material source)
        {
            var material = new Material(source) { name="MAT_OBS_Shared_BarrelWood" };
            // Warm multiply tint uses the same atlas; no imported texture edits.
            material.SetColor("_BaseColor", new Color(1f, 0.52f, 0.08f, 1f));
            AssetDatabase.CreateAsset(material, MaterialPath("BarrelWood"));
            return material;
        }
        [MenuItem("Tools/Endless Rollball/Obstacles/Use Neutral Barrel Material")]
        static void NeutralizeBarrels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play Mode and Prefab Mode first.");
            string path = PrefabPath("BarrelCluster");
            Material source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath("Kenney"));
            if (!File.Exists(path) || !source) throw new FileNotFoundException("Create shared obstacles first.");
            if (File.Exists(MaterialPath("BarrelWood"))) throw new IOException("Existing barrel material preserved.");
            if (File.Exists(RefineRequest)) File.Move(RefineRequest, RefineRequest + ".consumed_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            string backup = "Artifacts/SharedObstacles/BarrelBeforeNeutral_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            File.Copy(path, backup + "/" + Path.GetFileName(path));
            File.Copy(path + ".meta", backup + "/" + Path.GetFileName(path) + ".meta");
            Material material = CreateBarrelMaterial(source);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) throw new IOException("Could not save neutral barrel material overrides.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            string manifestPath = Root + "/AssetManifest.json";
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            manifest.materials = new[] { MaterialPath("Kenney"), MaterialPath("KayKit"), MaterialPath("BarrelWood") };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
            AssetDatabase.ImportAsset(manifestPath);
            File.AppendAllText(Report, "Neutral barrel refinement: warm multiply-tinted URP material; source atlas unchanged. Backup: " + backup + "\n");
            PreviewQueue.Enqueue(path);
            deadline = EditorApplication.timeSinceStartup + 90;
            nextPreviewAttempt = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update += SavePreviews;
            Debug.Log("SHARED_BARREL_MATERIAL_SAVED");
        }
        static Material CreateMaterial(string name, Texture2D atlas, Shader shader)
        {
            var material = new Material(shader) { name="MAT_OBS_Shared_"+name, enableInstancing=true };
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(material, MaterialPath(name));
            return material;
        }
        static string K(string name) => Kenney + "/Models/FBX format/" + name + ".fbx";
        static string Y(string name) => Kay + "/Assets/fbx(unity)/" + name + ".fbx";
        static string PrefabPath(string name) => Root + "/Prefabs/OBS_Shared_" + name + ".prefab";
        static string MaterialPath(string name) => Root + "/Materials/MAT_OBS_Shared_" + name + ".mat";
        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        internal static void QueuePreviews(IEnumerable<string> paths, string directory)
        {
            PreviewQueue.Clear();
            foreach (string path in paths) PreviewQueue.Enqueue(path);
            previewDirectory = directory;
            Directory.CreateDirectory(directory);
            deadline = EditorApplication.timeSinceStartup + 180;
            nextPreviewAttempt = EditorApplication.timeSinceStartup + 2;
            EditorApplication.update -= SavePreviews;
            EditorApplication.update += SavePreviews;
        }

        static void SavePreviews()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup > deadline)
            { EditorApplication.update -= SavePreviews; return; }
            if (EditorApplication.timeSinceStartup < nextPreviewAttempt) return;
            nextPreviewAttempt = EditorApplication.timeSinceStartup + 0.25;
            if (PreviewQueue.Count == 0) { EditorApplication.update -= SavePreviews; return; }
            string path = PreviewQueue.Peek();
            Texture2D preview = AssetPreview.GetAssetPreview(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            if (!preview) return;
            RenderTexture target = RenderTexture.GetTemporary(preview.width, preview.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            var copy = new Texture2D(preview.width, preview.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(preview, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, preview.width, preview.height), 0, 0);
                copy.Apply();
                File.WriteAllBytes(previewDirectory + "/" + Path.GetFileNameWithoutExtension(path) + ".png", copy.EncodeToPNG());
                PreviewQueue.Dequeue();
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(copy); }
        }
    }
}
