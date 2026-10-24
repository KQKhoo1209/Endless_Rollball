using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    internal static class SmallGapBridgeRevision
    {
        // Explicitly approved review revision; the token is consumed before saving assets.
        const string Target = "Assets/EndlessRollballAssets/Environment Prefab/Roads/Gaps/TRK_Street_GapSmall_24m.prefab";
        const string ExpectedHash = "605974EB33D7C9E4C0E3A116EA631125F200874B58D93502ACF97E99BF942278";
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string ReviewName = "SmallGapReview_Street_Clear_Gap_Clear_72m";
        const string Output = "Artifacts/SmallGapBridgeRevision";
        const string Request = Output + "/Apply.request";
        const float Run = 10f;
        const float Rise = .8f;
        static double deadline;

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request) ||
                File.ReadAllText(Request).Trim() != "raise-street-gap-four-metres-v2" ||
                DateTime.UtcNow-File.GetLastWriteTimeUtc(Request) > TimeSpan.FromMinutes(30)) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= Tick;
                File.WriteAllText(Request+".blocked","Editor stayed busy; use the revision menu after compilation.");
                return;
            }
            EditorApplication.update -= Tick;
            try { Apply(); }
            catch (Exception error) { File.WriteAllText(Request+".failed",error.ToString()); Debug.LogException(error); }
        }

        [MenuItem("Tools/Endless Rollball/Track/Revise Street Gap As Raised Bridge")]
        static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Exit Play Mode and Prefab Mode; save edits first.");
            using (var sha = SHA256.Create())
                if (!File.Exists(Target) || BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Target))).Replace("-","") != ExpectedHash)
                    throw new InvalidOperationException("Gap prefab changed since inspection. Preserve edits and review before applying.");
            Scene previous = SceneManager.GetActiveScene();
            Scene preview = SceneManager.GetSceneByPath(ScenePath);
            if (preview.IsValid() && preview.isLoaded && preview.isDirty)
                throw new InvalidOperationException("Save EnvironmentPreview first; unsaved edits preserved.");
            if (!preview.IsValid() || !preview.isLoaded)
                preview = EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            GameObject review = preview.GetRootGameObjects().SingleOrDefault(r=>r.name==ReviewName);
            if (review==null) throw new InvalidOperationException("Existing gap-review root not found.");
            Transform reference = review.transform.Find("ReviewOnly_1m_Sphere_NO_PHYSICS");
            Transform camera = review.transform.Find("CAM_SmallGapReview_Disabled");
            if (reference==null || camera==null || camera.GetComponent<Camera>()==null || camera.GetComponent<Camera>().enabled)
                throw new InvalidOperationException("Review reference/camera changed. Preserve and inspect first.");

            GameObject root = PrefabUtility.LoadPrefabContents(Target);
            try
            {
                Transform visual = Need(root.transform,"RoadVisual");
                Transform bankA = Need(visual,"Road_Takeoff_0_to_11");
                Transform bankB = Need(visual,"Road_Landing_13_to_24");
                Transform floorA = Need(root.transform,"Floor_Takeoff");
                Transform floorB = Need(root.transform,"Floor_Landing");
                Transform slabA = Need(visual,"Exposed_Takeoff_Layer");
                Transform slabB = Need(visual,"Exposed_Landing_Layer");
                Transform warningA = Need(visual,"Warning_Takeoff_Edge");
                Transform warningB = Need(visual,"Warning_Landing_Edge");
                TrackSegment track = root.GetComponent<TrackSegment>();
                if (track==null || !track.IsContractValid(out _) || track.Kind!=TrackSegmentKind.Gap || !track.RequiredJump ||
                    root.GetComponentsInChildren<Collider>(true).Length!=4 ||
                    floorA.GetComponent<BoxCollider>()==null || floorB.GetComponent<BoxCollider>()==null)
                    throw new InvalidOperationException("Unexpected existing review-track contract.");

                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup = Output+"/Before_"+stamp;
                Directory.CreateDirectory(backup);
                foreach (string path in new[] { Target,Target+".meta",ScenePath,ScenePath+".meta" })
                    File.Copy(path,backup+"/"+Path.GetFileName(path));
                if (File.Exists(Request)) File.Move(Request,Request+".consumed_"+stamp);

                float length = Mathf.Sqrt(Run*Run+Rise*Rise);
                float angle = Mathf.Atan2(Rise,Run)*Mathf.Rad2Deg;
                Quaternion up = Quaternion.Euler(-angle,0,0);
                Quaternion down = Quaternion.Euler(angle,0,0);
                Vector3 landingStart = new Vector3(0,Rise,14);
                Bank(bankA,"Road_Takeoff_0_to_10",Vector3.zero,up,length);
                Bank(bankB,"Road_Landing_14_to_24",landingStart,down,length);
                Floor(floorA,Vector3.zero,up,length);
                Floor(floorB,landingStart,down,length);
                Slab(slabA,Vector3.zero,up,length);
                Slab(slabB,landingStart,down,length);
                warningA.localPosition = up*new Vector3(0,.012f,length-.2f);
                warningA.localRotation = up;
                warningB.localPosition = landingStart+down*new Vector3(0,.012f,.2f);
                warningB.localRotation = down;

                CheckPoint(floorA,new Vector3(0,0,0),new Vector3(0,0,0));
                CheckPoint(floorA,new Vector3(0,0,length),new Vector3(0,Rise,10));
                CheckPoint(floorB,new Vector3(0,0,0),new Vector3(0,Rise,14));
                CheckPoint(floorB,new Vector3(0,0,length),new Vector3(0,0,24));
                if (!track.IsContractValid(out string reason)) throw new InvalidOperationException(reason);
                PrefabUtility.SaveAsPrefabAsset(root,Target,out bool saved);
                if (!saved) throw new IOException("Could not save bridge revision.");

                reference.localPosition = new Vector3(0,.5f+Rise*.5f,29);
                camera.localPosition = new Vector3(0,3.8f,23);
                camera.LookAt(review.transform.TransformPoint(new Vector3(0,Rise,36)));
                if (!EditorSceneManager.SaveScene(preview)) throw new IOException("Could not save updated review camera/reference.");

                var report = new StringBuilder("STREET_GAP_BRIDGE_REVISION_SAVED\n");
                report.AppendLine("Target: "+Target+" | GUID preserved | backup="+backup);
                report.AppendLine("Top-edge gap 4m: local Z=10..14, Y=.8m. Review gap Z=34..38 at world X="+review.transform.position.x);
                report.AppendLine("24m root/anchors unchanged: entry=(0,0,0), exit=(0,0,24). Each bank has 10m horizontal run and .8m rise.");
                report.AppendLine("Slope="+angle.ToString("F5")+"deg; slope length="+length.ToString("F6")+"m. Takeoff rotation X=-angle; landing rotation X=+angle.");
                report.AppendLine("Two rotated BoxColliders: size=(11,.5,"+length.ToString("F6")+"); local centre=(0,-.25,"+(length*.5f).ToString("F6")+").");
                report.AppendLine("Collider/visual endpoints matched by authoring calculations; exposed layers and warning strips rotate with banks. No supportive bridge across gap.");
                report.AppendLine("Rotated .5m-thick box underside tips project about .040m past each top edge; fully empty volume between tips is about 3.920m.");
                report.AppendLine("Original normal roads, side boundaries, materials, root metadata/GUID and runtime scripts unchanged. Existing review camera stays disabled; sphere is visual-only.");
                report.AppendLine("This revision supersedes flat 2m geometry. No Play Mode, automated tests, gameplay jumpability/coasting review or profiling performed. Part C remains deferred.");
                File.WriteAllText(Output+"/RevisionReport.txt",report.ToString());
                Selection.activeGameObject = review;
                SceneView.lastActiveSceneView?.FrameSelected();
                Debug.Log("STREET_GAP_BRIDGE_REVISION_SAVED: "+Output+"/RevisionReport.txt");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static Transform Need(Transform parent,string name)
        {
            Transform result = parent.Find(name);
            if (result==null) throw new InvalidOperationException("Missing review part: "+name);
            return result;
        }
        static void Bank(Transform bank,string name,Vector3 start,Quaternion rotation,float length)
        {
            bank.name = name;
            bank.localPosition = start;
            bank.localRotation = rotation;
            bank.localScale = new Vector3(1,1,length/24f);
        }
        static void Floor(Transform floor,Vector3 start,Quaternion rotation,float length)
        {
            floor.localPosition = start;
            floor.localRotation = rotation;
            floor.localScale = Vector3.one;
            BoxCollider box = floor.GetComponent<BoxCollider>();
            box.center = new Vector3(0,-.25f,length*.5f);
            box.size = new Vector3(11,.5f,length);
        }
        static void Slab(Transform slab,Vector3 start,Quaternion rotation,float length)
        {
            slab.localPosition = start+rotation*new Vector3(0,-.27f,length*.5f);
            slab.localRotation = rotation;
            slab.localScale = new Vector3(9,.5f,length);
        }
        static void CheckPoint(Transform floor,Vector3 local,Vector3 expected)
        {
            Vector3 actual = floor.parent.InverseTransformPoint(floor.TransformPoint(local));
            if (Vector3.Distance(actual,expected)>.0001f)
                throw new InvalidOperationException("Bank endpoint does not match the 24m seam/raised gap contract.");
        }
    }
}
