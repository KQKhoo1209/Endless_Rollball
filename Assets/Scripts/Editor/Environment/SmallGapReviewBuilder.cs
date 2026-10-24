using System;
using System.IO;
using System.Linq;
using System.Text;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // Part B authoring only. No GameplayScene, runtime gap spawning or physics changes.
    internal static class SmallGapReviewBuilder
    {
        const string Source = "Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Street_Road_24m.prefab";
        const string Folder = "Assets/EndlessRollballAssets/Environment Prefab/Roads/Gaps";
        const string Target = Folder + "/TRK_Street_GapSmall_24m.prefab";
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string Output = "Artifacts/SmallGapPartB";
        const string Request = Output + "/Create.request";
        const string ReviewName = "SmallGapReview_Street_Clear_Gap_Clear_72m";
        static double deadline;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request) ||
                File.ReadAllText(Request).Trim() != "create-street-small-gap-part-b-v1" ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Run;
        }

        static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= Run;
                File.WriteAllText(Request + ".blocked", "Editor stayed busy. Wait, then use the Part B authoring menu.");
                return;
            }
            EditorApplication.update -= Run;
            try { Build(); }
            catch (Exception error)
            {
                File.WriteAllText(Request + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Endless Rollball/Track/Create Street Small Gap Part B Review")]
        static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play Mode and Prefab Mode first.");
            if (File.Exists(Target) || File.Exists(Folder + "/MAT_Gap_Warning.mat") || File.Exists(Folder + "/MAT_Gap_ExposedSlab.mat"))
                throw new IOException("Existing gap review assets preserved. This builder is create-once.");
            if (!File.Exists(ScenePath)) throw new FileNotFoundException(ScenePath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            var sourceTrack = source != null ? source.GetComponent<TrackSegment>() : null;
            if (sourceTrack == null || !sourceTrack.IsContractValid(out _) || sourceTrack.Kind != TrackSegmentKind.Empty ||
                sourceTrack.RequiredJump || source.transform.localScale != Vector3.one)
                throw new InvalidOperationException("Expected the existing clear unit-scale Street track.");
            Transform sourceVisual = source.transform.Find("RoadVisual");
            Transform sourceFloor = source.transform.Find("FloorCollider");
            BoxCollider floorBox = sourceFloor != null ? sourceFloor.GetComponent<BoxCollider>() : null;
            if (floorBox == null || !floorBox.enabled || floorBox.isTrigger || floorBox.size != new Vector3(11,.5f,24) ||
                floorBox.center != new Vector3(0,-.25f,12) || source.GetComponentsInChildren<Collider>(true).Length != 3 ||
                source.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                throw new InvalidOperationException("Source floor/boundaries changed; do not author from an unreviewed layout.");
            if (sourceVisual == null || sourceVisual.localPosition != Vector3.zero || sourceVisual.localScale != Vector3.one ||
                sourceVisual.localRotation != Quaternion.identity || sourceVisual.GetComponentsInChildren<Renderer>(true).Length == 0 ||
                sourceVisual.GetComponentsInChildren<Component>(true).Any(c => c == null ||
                    (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer))))
                throw new InvalidOperationException("Expected an identity visual-only RoadVisual hierarchy.");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit is unavailable.");

            Scene previous = SceneManager.GetActiveScene();
            Scene preview = SceneManager.GetSceneByPath(ScenePath);
            if (preview.IsValid() && preview.isLoaded && preview.isDirty)
                throw new InvalidOperationException("Save EnvironmentPreview first; unsaved edits were preserved.");
            if (!preview.IsValid() || !preview.isLoaded)
                preview = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (preview.GetRootGameObjects().Any(r => r.name == ReviewName))
                throw new InvalidOperationException("Existing small-gap review root preserved.");

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            string backup = Output + "/Before_" + stamp;
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            File.Copy(ScenePath + ".meta", backup + "/EnvironmentPreview.unity.meta");
            File.Copy(Source, backup + "/TRK_Street_Road_24m.prefab");
            File.Copy(Source + ".meta", backup + "/TRK_Street_Road_24m.prefab.meta");
            if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + stamp);

            Scene staging = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(root, staging);
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "TRK_Street_GapSmall_24m";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;

                Transform visual = root.transform.Find("RoadVisual");
                Transform floor = root.transform.Find("FloorCollider");
                if (floor.localPosition != Vector3.zero || floor.localScale != Vector3.one || floor.localRotation != Quaternion.identity)
                    throw new InvalidOperationException("The source floor owner must have identity transform.");

                // Fit shared source geometry into two supported banks. Only new visual parents scale;
                // the original source mesh/materials, track root and anchors remain untouched.
                GameObject takeoff = Object.Instantiate(visual.gameObject, root.transform, false);
                GameObject landing = Object.Instantiate(visual.gameObject, root.transform, false);
                foreach (Transform child in visual.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                FitBank(takeoff, visual, "Road_Takeoff_0_to_11", 0f);
                FitBank(landing, visual, "Road_Landing_13_to_24", 13f);
                Bounds takeoffBounds = BoundsOf(takeoff);
                Bounds landingBounds = BoundsOf(landing);
                if (Mathf.Abs(takeoffBounds.min.z) > .01f || Mathf.Abs(takeoffBounds.max.z - 11) > .01f ||
                    Mathf.Abs(landingBounds.min.z - 13) > .01f || Mathf.Abs(landingBounds.max.z - 24) > .01f)
                    throw new InvalidOperationException("Visual banks do not leave the intended 2m opening.");

                CopyFloor(floor, root.transform, "Floor_Takeoff", 5.5f);
                CopyFloor(floor, root.transform, "Floor_Landing", 18.5f);
                Object.DestroyImmediate(floor.gameObject);
                var serialized = new SerializedObject(root.GetComponent<TrackSegment>());
                serialized.FindProperty("segmentId").stringValue = root.name;
                serialized.FindProperty("kind").enumValueIndex = (int)TrackSegmentKind.Gap;
                serialized.FindProperty("requiredJump").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EnsureFolder(Folder);
                var slab = MaterialAsset(shader, "ExposedSlab", new Color(.21f,.24f,.27f));
                var warning = MaterialAsset(shader, "Warning", new Color(244f/255f,197f/255f,66f/255f));
                BoxVisual(visual, "Exposed_Takeoff_Layer", new Vector3(0,-.27f,5.5f), new Vector3(9,.5f,11), slab);
                BoxVisual(visual, "Exposed_Landing_Layer", new Vector3(0,-.27f,18.5f), new Vector3(9,.5f,11), slab);
                BoxVisual(visual, "Warning_Takeoff_Edge", new Vector3(0,.012f,10.80f), new Vector3(8.8f,.018f,.12f), warning);
                BoxVisual(visual, "Warning_Landing_Edge", new Vector3(0,.012f,13.20f), new Vector3(8.8f,.018f,.12f), warning);

                TrackSegment track = root.GetComponent<TrackSegment>();
                if (!track.IsContractValid(out string reason)) throw new InvalidOperationException(reason);
                Collider[] shapes = root.GetComponentsInChildren<Collider>(true);
                if (shapes.Length != 4 || shapes.Any(c => !(c is BoxCollider) || c.isTrigger || !c.enabled))
                    throw new InvalidOperationException("Expected only two floor and two side-boundary colliders.");
                foreach (Collider shape in shapes)
                {
                    Bounds b = BoxBounds((BoxCollider)shape);
                    if (b.min.x < 4.49f && b.max.x > -4.49f && b.min.z < 12.9999f && b.max.z > 11.0001f &&
                        b.min.y < .5f && b.max.y > -.5f)
                        throw new InvalidOperationException("Unexpected support in playable gap: " + shape.name);
                }
                if (root.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                    root.GetComponentsInChildren<MonoBehaviour>(true).Length != 1)
                    throw new InvalidOperationException("Unexpected runtime components in gap track.");
                PrefabUtility.SaveAsPrefabAsset(root, Target, out bool saved);
                if (!saved) throw new IOException("Could not save gap prefab.");

                // Put the review beyond all existing renderer/collider bounds, so no old floor
                // or scenery can accidentally support/cover its opening. Existing roots stay unchanged.
                GameObject[] existing = preview.GetRootGameObjects();
                float maxX = 0f;
                foreach (GameObject old in existing)
                {
                    foreach (Renderer r in old.GetComponentsInChildren<Renderer>(true)) maxX = Mathf.Max(maxX,r.bounds.max.x);
                    foreach (Collider c in old.GetComponentsInChildren<Collider>(true)) maxX = Mathf.Max(maxX,c.bounds.max.x);
                }
                if (float.IsNaN(maxX) || float.IsInfinity(maxX) || maxX > 100000)
                    throw new InvalidOperationException("Existing preview bounds are too large; place review manually.");
                var review = new GameObject(ReviewName);
                SceneManager.MoveGameObjectToScene(review, preview);
                review.transform.position = new Vector3(Mathf.Ceil((maxX+32)/24)*24,0,0);
                GameObject gapAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Target);
                for (int i = 0; i < 3; i++)
                {
                    GameObject piece = (GameObject)PrefabUtility.InstantiatePrefab(i == 1 ? gapAsset : source, preview);
                    piece.transform.SetParent(review.transform, false);
                    piece.transform.localPosition = new Vector3(0,0,i*24);
                    piece.name = i == 1 ? "02_Gap_Track_24_to_48" : (i == 0 ? "01_Clear_Approach_0_to_24" : "03_Clear_Runoff_48_to_72");
                }
                var reference = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                reference.name = "ReviewOnly_1m_Sphere_NO_PHYSICS";
                reference.transform.SetParent(review.transform,false);
                reference.transform.localPosition = new Vector3(0,.5f,29);
                Object.DestroyImmediate(reference.GetComponent<Collider>());
                var cameraObject = new GameObject("CAM_SmallGapReview_Disabled");
                cameraObject.transform.SetParent(review.transform,false);
                cameraObject.transform.localPosition = new Vector3(0,3,23);
                cameraObject.transform.LookAt(review.transform.TransformPoint(new Vector3(0,.4f,36)));
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 60;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 200;
                camera.clearFlags = CameraClearFlags.Skybox;
                if (!EditorSceneManager.SaveScene(preview)) throw new IOException("Could not save EnvironmentPreview.");

                var report = new StringBuilder("STREET_SMALL_GAP_PART_B_SAVED\n");
                report.AppendLine("Prefab: " + Target);
                report.AppendLine("Scene: " + ScenePath + " | root=" + ReviewName + " | position=" + review.transform.position.ToString("F3"));
                report.AppendLine("Backup: " + backup);
                report.AppendLine("24m segment; 9m playable route; 11m physical support width; void local Z=11..13; no ramp.");
                report.AppendLine("Floors: size=(11,.5,11); centers=(0,-.25,5.5)/(0,-.25,18.5); top Y=0. Side boundaries copied unchanged.");
                report.AppendLine("Visual takeoff: " + takeoffBounds.min.ToString("F4") + " -> " + takeoffBounds.max.ToString("F4"));
                report.AppendLine("Visual landing: " + landingBounds.min.ToString("F4") + " -> " + landingBounds.max.ToString("F4"));
                report.AppendLine("Review roots local Z=0,24,48. Physical gap in review local Z=35..37. Camera disabled; sphere visual-only.");
                report.AppendLine("New bank visual parents use Z scale 11/24 for the review; source meshes/materials unchanged. Road markings are compressed, not mesh-trimmed final artwork.");
                report.AppendLine("Existing scene roots/cameras/lighting/settings preserved. GameplayScene, original roads and runtime gap integration unchanged.");
                report.AppendLine("No Play Mode, automated tests, jumpability verification, render capture or profiling performed. User acceptance needed before other themes/Part C.");
                File.WriteAllText(Output + "/AuthoringReport.txt", report.ToString());
                Selection.activeGameObject = review;
                SceneView.lastActiveSceneView?.FrameSelected();
                Debug.Log("STREET_SMALL_GAP_PART_B_SAVED: " + Output + "/AuthoringReport.txt");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(staging);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static void FitBank(GameObject bank, Transform parent, string name, float offset)
        {
            bank.name = name;
            bank.transform.SetParent(parent,false);
            bank.transform.localPosition = new Vector3(0,0,offset);
            bank.transform.localRotation = Quaternion.identity;
            bank.transform.localScale = new Vector3(1,1,11f/24f);
        }
        static Bounds BoundsOf(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        static void CopyFloor(Transform template, Transform parent, string name, float z)
        {
            GameObject floor = Object.Instantiate(template.gameObject,parent,false);
            floor.name = name;
            var box = floor.GetComponent<BoxCollider>();
            box.size = new Vector3(11,.5f,11);
            box.center = new Vector3(0,-.25f,z);
        }
        static Bounds BoxBounds(BoxCollider box)
        {
            Bounds result = new Bounds(box.transform.TransformPoint(box.center),Vector3.zero);
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                result.Encapsulate(box.transform.TransformPoint(box.center +
                    Vector3.Scale(box.size * .5f,new Vector3(x,y,z))));
            return result;
        }
        static Material MaterialAsset(Shader shader, string label, Color color)
        {
            var material = new Material(shader) { name = "MAT_Gap_" + label, enableInstancing = true };
            material.SetColor("_BaseColor",color);
            material.SetFloat("_Smoothness",.15f);
            AssetDatabase.CreateAsset(material,Folder + "/" + material.name + ".mat");
            return material;
        }
        static void BoxVisual(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent,false);
            box.transform.localPosition = position;
            box.transform.localScale = scale;
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.GetComponent<Renderer>().sharedMaterial = material;
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0,path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,path.Substring(path.LastIndexOf('/')+1));
        }
    }
}
