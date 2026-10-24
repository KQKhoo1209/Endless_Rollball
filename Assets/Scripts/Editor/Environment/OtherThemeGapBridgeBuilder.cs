using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    internal static class OtherThemeGapBridgeBuilder
    {
        const string Roads = "Assets/EndlessRollballAssets/Environment Prefab/Roads";
        const string Folder = Roads+"/Gaps";
        const string Template = Folder+"/TRK_Street_GapSmall_24m.prefab";
        const string TemplateHash = "4A84008C25F7DFA671FF14DEC49753009FDD9632800091B0E2297CAEB901CA75";
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string Output = "Artifacts/OtherThemeGapBridges";
        const string Request = Output+"/Create.request";
        static readonly string[] Themes = { "Beach", "Dungeon" };
        static double deadline;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request) ||
                File.ReadAllText(Request).Trim()!="create-beach-dungeon-raised-gaps-v2" ||
                DateTime.UtcNow-File.GetLastWriteTimeUtc(Request)>TimeSpan.FromMinutes(30)) return;
            deadline = EditorApplication.timeSinceStartup+120;
            EditorApplication.update += Tick;
        }
        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup<deadline) return;
                EditorApplication.update-=Tick;
                File.WriteAllText(Request+".blocked","Editor stayed busy; use the authoring menu after compilation.");
                return;
            }
            EditorApplication.update-=Tick;
            try { Build(); }
            catch(Exception error) { File.WriteAllText(Request+".failed",error.ToString()); Debug.LogException(error); }
        }

        [MenuItem("Tools/Endless Rollball/Track/Create Beach And Dungeon Raised Gap Reviews")]
        static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Exit Play Mode/Prefab Mode and save edits first.");
            using(var sha = SHA256.Create())
                if (!File.Exists(Template) || BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Template))).Replace("-","")!=TemplateHash)
                    throw new InvalidOperationException("Street template changed since inspection; review before building other themes.");
            foreach(string theme in Themes)
                if (File.Exists(Target(theme)) || File.Exists(MaterialPath(theme)))
                    throw new IOException("Existing generated theme assets preserved: "+theme);
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(Template);
            var sources = Themes.Select(t=>AssetDatabase.LoadAssetAtPath<GameObject>(Source(t))).ToArray();
            foreach(GameObject source in sources)
            {
                var track = source!=null?source.GetComponent<TrackSegment>():null;
                Transform visual = source!=null?source.transform.Find("RoadVisual"):null;
                if (track==null || !track.IsContractValid(out _) || track.Kind!=TrackSegmentKind.Empty || track.RequiredJump ||
                    track.EntryLanes!=TrackLaneMask.All || track.SafeLanes!=TrackLaneMask.All || visual==null ||
                    source.transform.localScale!=Vector3.one || visual.localPosition!=Vector3.zero ||
                    visual.localRotation!=Quaternion.identity || visual.localScale.x<=0f ||
                    visual.localScale.y!=1f || visual.localScale.z!=1f ||
                    visual.GetComponentsInChildren<Component>(true).Any(c=>c==null ||
                        (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer))))
                    throw new InvalidOperationException("Expected the existing clear themed track and visual-only RoadVisual.");
            }
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader==null) throw new InvalidOperationException("Missing URP/Lit.");
            Scene previous = SceneManager.GetActiveScene();
            Scene preview = SceneManager.GetSceneByPath(ScenePath);
            if (preview.IsValid() && preview.isLoaded && preview.isDirty)
                throw new InvalidOperationException("Save EnvironmentPreview before authoring; unsaved edits preserved.");
            if (!preview.IsValid() || !preview.isLoaded)
                preview = EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            foreach(string theme in Themes)
                if (preview.GetRootGameObjects().Any(r=>r.name==ReviewName(theme)))
                    throw new InvalidOperationException("Existing review root preserved: "+theme);

            Scene staging = EditorSceneManager.NewPreviewScene();
            var roots = new List<GameObject>();
            try
            {
                string collisionContract = CollisionState(template);
                // Preflight both themed compositions before writing any new asset.
                for(int i=0;i<Themes.Length;i++)
                {
                    GameObject root = Object.Instantiate(template);
                    SceneManager.MoveGameObjectToScene(root,staging);
                    if (PrefabUtility.IsPartOfPrefabInstance(root))
                        PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                    roots.Add(root);
                    root.name = "TRK_"+Themes[i]+"_GapSmall_24m";
                    root.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    root.transform.localScale = Vector3.one;
                    var serialized = new SerializedObject(root.GetComponent<TrackSegment>());
                    var original = new SerializedObject(sources[i].GetComponent<TrackSegment>());
                    serialized.FindProperty("segmentId").stringValue = root.name;
                    serialized.FindProperty("allowedModes").intValue = original.FindProperty("allowedModes").intValue;
                    foreach(string field in new[] { "easyWeight","normalWeight","hardWeight","endlessWeight" })
                        serialized.FindProperty(field).floatValue = original.FindProperty(field).floatValue;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Transform visual = root.transform.Find("RoadVisual");
                    foreach(string bankName in new[] { "Road_Takeoff_0_to_10","Road_Landing_14_to_24" })
                    {
                        Transform bank = visual.Find(bankName);
                        if (bank==null) throw new InvalidOperationException("Template bank missing.");
                        foreach(Transform child in bank.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                        GameObject road = Object.Instantiate(sources[i].transform.Find("RoadVisual").gameObject,bank,false);
                        road.name = "Source_"+Themes[i]+"_RoadVisual";
                        road.transform.localPosition = Vector3.zero;
                        road.transform.localRotation = Quaternion.identity;
                        // Preserve the user's wider RoadVisual (currently X=1.2),
                        // while the bank's own Z scale supplies the bridge length.
                        road.transform.localScale = sources[i].transform.Find("RoadVisual").localScale;
                        Bounds b = BoundsOf(bank.gameObject);
                        if ((bankName.Contains("Takeoff") && (Mathf.Abs(b.min.z)>.15f || Mathf.Abs(b.max.z-10)>.15f)) ||
                            (bankName.Contains("Landing") && (Mathf.Abs(b.min.z-14)>.15f || Mathf.Abs(b.max.z-24)>.15f)))
                            throw new InvalidOperationException("The themed visual no longer fits the 24m raised banks.");
                    }
                    var track = root.GetComponent<TrackSegment>();
                    if (!track.IsContractValid(out string reason)) throw new InvalidOperationException(reason);
                    if (track.Kind!=TrackSegmentKind.Gap || !track.RequiredJump || CollisionState(root)!=collisionContract ||
                        root.GetComponentsInChildren<Rigidbody>(true).Length!=0 || root.GetComponentsInChildren<MonoBehaviour>(true).Length!=1)
                        throw new InvalidOperationException("Theme replacement altered the shared gap collision contract.");
                }

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup = Output+"/Before_"+stamp;
                Directory.CreateDirectory(backup);
                foreach(string path in new[] { ScenePath,Template,Source("Beach"),Source("Dungeon") })
                {
                    File.Copy(path,backup+"/"+Path.GetFileName(path));
                    File.Copy(path+".meta",backup+"/"+Path.GetFileName(path)+".meta");
                }
                if (File.Exists(Request)) File.Move(Request,Request+".consumed_"+stamp);
                var report = new StringBuilder("BEACH_DUNGEON_GAP_BRIDGES_SAVED\nBackup: "+backup+"\n");
                for(int i=0;i<Themes.Length;i++)
                {
                    var material = new Material(shader) { name="MAT_Gap_"+Themes[i]+"_ExposedWood", enableInstancing=true };
                    material.SetColor("_BaseColor",i==0?new Color(.34f,.22f,.11f):new Color(.17f,.12f,.085f));
                    material.SetFloat("_Smoothness",.15f);
                    AssetDatabase.CreateAsset(material,MaterialPath(Themes[i]));
                    foreach(string name in new[] { "Exposed_Takeoff_Layer","Exposed_Landing_Layer" })
                        roots[i].transform.Find("RoadVisual/"+name).GetComponent<Renderer>().sharedMaterial = material;
                    PrefabUtility.SaveAsPrefabAsset(roots[i],Target(Themes[i]),out bool saved);
                    if (!saved) throw new IOException("Could not save "+Themes[i]+" gap prefab.");
                    report.AppendLine("Prefab: "+Target(Themes[i])+" | visuals from "+Source(Themes[i]));
                }
                float maxX = 0;
                foreach(GameObject old in preview.GetRootGameObjects())
                {
                    foreach(Renderer r in old.GetComponentsInChildren<Renderer>(true)) maxX=Mathf.Max(maxX,r.bounds.max.x);
                    foreach(Collider c in old.GetComponentsInChildren<Collider>(true)) maxX=Mathf.Max(maxX,c.bounds.max.x);
                }
                if (float.IsNaN(maxX) || float.IsInfinity(maxX) || maxX>100000)
                    throw new InvalidOperationException("Existing scene bounds unsuitable; place reviews manually.");
                float startX = Mathf.Ceil((maxX+32)/24)*24;
                GameObject selected = null;
                for(int i=0;i<Themes.Length;i++)
                {
                    var review = new GameObject(ReviewName(Themes[i]));
                    SceneManager.MoveGameObjectToScene(review,preview);
                    review.transform.position = new Vector3(startX+i*48,0,0);
                    GameObject gap = AssetDatabase.LoadAssetAtPath<GameObject>(Target(Themes[i]));
                    for(int j=0;j<3;j++)
                    {
                        var piece = (GameObject)PrefabUtility.InstantiatePrefab(j==1?gap:sources[i],preview);
                        piece.transform.SetParent(review.transform,false);
                        piece.transform.localPosition = new Vector3(0,0,j*24);
                        piece.name = j==1?"02_Gap_Track_24_to_48":(j==0?"01_Clear_Approach_0_to_24":"03_Clear_Runoff_48_to_72");
                    }
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.name = "ReviewOnly_1m_Sphere_NO_PHYSICS";
                    sphere.transform.SetParent(review.transform,false);
                    sphere.transform.localPosition = new Vector3(0,.9f,29);
                    Object.DestroyImmediate(sphere.GetComponent<Collider>());
                    var cameraObject = new GameObject("CAM_"+Themes[i]+"GapReview_Disabled");
                    cameraObject.transform.SetParent(review.transform,false);
                    cameraObject.transform.localPosition = new Vector3(0,3.8f,23);
                    cameraObject.transform.LookAt(review.transform.TransformPoint(new Vector3(0,.8f,36)));
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.enabled=false;
                    camera.fieldOfView=60;
                    camera.nearClipPlane=.1f;
                    camera.farClipPlane=200;
                    camera.clearFlags=CameraClearFlags.Skybox;
                    report.AppendLine("Review: "+review.name+" | position="+review.transform.position.ToString("F3"));
                    selected=review;
                }
                if (!EditorSceneManager.SaveScene(preview)) throw new IOException("Could not save EnvironmentPreview.");
                report.AppendLine("Identical Street-template collision: 24m, 9m playable width, 11m support width, 4m top-edge gap Z=10..14, lips Y=.8m, slopes +/-4.57392deg.");
                report.AppendLine("Warning material shared with Street; theme road meshes/materials shared with original Beach/Dungeon sources. Only two new exposed-wood materials.");
                report.AppendLine("Source RoadVisual X scales retained: Beach="+sources[0].transform.Find("RoadVisual").localScale.x+", Dungeon="+sources[1].transform.Find("RoadVisual").localScale.x+".");
                report.AppendLine("Existing source prefabs, Street gap, scene roots, lights/cameras and presentation settings unchanged. Review cameras disabled; spheres visual-only.");
                report.AppendLine("No GameplayScene changes, runtime spawning integration, Play Mode, automated tests, screenshot capture or profiling. User gameplay acceptance remains required.");
                File.WriteAllText(Output+"/AuthoringReport.txt",report.ToString());
                Selection.activeGameObject=selected;
                SceneView.lastActiveSceneView?.FrameSelected();
                Debug.Log("BEACH_DUNGEON_GAP_BRIDGES_SAVED: "+Output+"/AuthoringReport.txt");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(staging);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static string Source(string theme)=>Roads+"/TRK_"+theme+"_Road_24m.prefab";
        static string Target(string theme)=>Folder+"/TRK_"+theme+"_GapSmall_24m.prefab";
        static string MaterialPath(string theme)=>Folder+"/MAT_Gap_"+theme+"_ExposedWood.mat";
        static string ReviewName(string theme)=>"SmallGapReview_"+theme+"_Clear_Gap_Clear_72m";
        static Bounds BoundsOf(GameObject root)
        {
            Renderer[] renderers=root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length==0) throw new InvalidOperationException("Themed bank has no visual geometry.");
            Bounds b=renderers[0].bounds;
            foreach(Renderer renderer in renderers.Skip(1)) b.Encapsulate(renderer.bounds);
            return b;
        }
        static string CollisionState(GameObject root)
        {
            var text=new StringBuilder();
            foreach(Collider shape in root.GetComponentsInChildren<Collider>(true).OrderBy(c=>c.name))
            {
                var box=shape as BoxCollider;
                if (box==null || box.isTrigger || !box.enabled) throw new InvalidOperationException("Unexpected collider type/state.");
                Transform t=box.transform;
                text.AppendLine(AnimationUtility.CalculateTransformPath(t,root.transform)+"|"+t.localPosition.ToString("F7")+"|"+
                    t.localRotation.ToString("F7")+"|"+t.localScale.ToString("F7")+"|"+box.center.ToString("F7")+"|"+box.size.ToString("F7"));
            }
            return text.ToString();
        }
    }
}
