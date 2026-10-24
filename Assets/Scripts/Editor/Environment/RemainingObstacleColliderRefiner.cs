using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    internal static class RemainingObstacleColliderRefiner
    {
        const string Root = "Assets/EndlessRollballAssets/Obstacles/Shared/Prefabs/";
        const string Output = "Artifacts/ObstacleColliderCorrection";
        const string Request = Output + "/Apply.request";
        const string OldReport = "Artifacts/ObstacleColliderRefinement/ColliderFitReport.json";
        static double deadline;
        static readonly string[] Others =
        {
            "NavigateLeftRight/OBS_NAV_02_SlidingBarrels.prefab",
            "NavigateLeftRight/OBS_NAV_03_SlidingStone.prefab",
            "NavigateLeftRight/OBS_NAV_04_SlidingTimber.prefab",
            "NavigateLeftRight/OBS_NAV_06_SlidingFence.prefab",
            "NavigateLeftRight/OBS_NAV_07_CrossingCompactCar.prefab",
            "NavigateLeftRight/OBS_NAV_08_CrossingSUV.prefab",
            "NavigateLeftRight/OBS_NAV_09_CrossingPickup.prefab",
            "NavigateLeftRight/OBS_NAV_10_CrossingTruck.prefab",
            "JumpableOnly/OBS_JMP_01_LogRow.prefab",
            "JumpableOnly/OBS_JMP_02_TimberBundleRow.prefab",
            "JumpableOnly/OBS_JMP_03_StoneBrickRow.prefab",
            "JumpableOnly/OBS_JMP_04_LowCrateRow.prefab",
            "JumpableOnly/OBS_JMP_05_FallenBarrelRow.prefab",
            "JumpableOnly/OBS_JMP_06_FenceRailRow.prefab",
            "JumpableOnly/OBS_JMP_07_RubbleBand.prefab",
            "JumpableOnly/OBS_JMP_08_FortifiedFenceRow.prefab",
            "JumpableOnly/OBS_JMP_09_TextileBundleRow.prefab",
            "JumpableOnly/OBS_JMP_10_IronBundleRow.prefab"
        };

        [Serializable] sealed class PriorReport { public PriorAsset[] assets; }
        [Serializable] sealed class PriorAsset { public string path; public bool saved; public PriorShape[] colliders; }
        [Serializable] sealed class PriorShape { public string owner; public Vector3 oldCenter, oldSize, center, size; public bool changed; }
        sealed class Plan
        {
            public string Path, ArtState;
            public GameObject Root;
            public Transform OldCollision;
            public readonly List<Shape> Shapes = new List<Shape>();
            public bool Reference, Changed;
        }
        sealed class Shape
        {
            public Transform Owner;
            public BoxCollider Template;
            public Bounds Fit;
            public BoxCollider Created;
        }
        [Serializable] sealed class Entry
        {
            public string path, operation;
            public int boxes;
            public string[] owners;
            public Vector3 min, max;
        }
        [Serializable] sealed class Result
        {
            public string scope = "Fixed prefabs and NAV01 are references, not refitting targets. Undo only still-matching changes from the prior mistaken pass. Other moving/jump colliders are fitted directly on existing visual owners.";
            public string preserved = "Artwork, model transforms/materials, Rigidbody/motion settings, prefab GUIDs, runtime code and scenes. Empty legacy Collision containers removed only from non-reference targets. No gameplay tests.";
            public string backup;
            public Entry[] assets;
        }

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            if (Application.isBatchMode || !File.Exists(Request) ||
                DateTime.UtcNow-File.GetLastWriteTimeUtc(Request)>TimeSpan.FromMinutes(30) ||
                File.ReadAllText(Request).Trim()!="refine-other-nav-and-jump-keep-references-v2") return;
            deadline=EditorApplication.timeSinceStartup+120;
            EditorApplication.update+=Run;
        }
        static void Run()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (EditorApplication.timeSinceStartup<deadline) return;
                EditorApplication.update-=Run;
                File.WriteAllText(Request+".blocked","Editor remained busy; wait for compilation and use the correction menu.");
                return;
            }
            EditorApplication.update-=Run;
            if (!File.Exists(Request)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage()!=null)
            { File.WriteAllText(Request+".blocked","Exit Play/Prefab Mode and save edits, then use the correction menu."); return; }
            try { Apply(); }
            catch(Exception error) { File.WriteAllText(Request+".failed",error.ToString()); Debug.LogException(error); }
        }

        [MenuItem("Tools/Endless Rollball/Obstacles/Refine Other Colliders - Preserve References")]
        static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Exit Play/Prefab Mode first.");
            PriorReport prior=JsonUtility.FromJson<PriorReport>(File.ReadAllText(OldReport));
            var plans=new List<Plan>();
            try
            {
                // Plan guarded field-only rollback; later user edits are never overwritten.
                foreach(PriorAsset asset in prior.assets.Where(a=>a.saved))
                {
                    Plan plan=Load(asset.path,true,plans);
                    foreach(PriorShape old in asset.colliders.Where(c=>c.changed))
                    {
                        Transform owner=plan.Root.transform.Find(old.owner);
                        BoxCollider box=owner!=null?owner.GetComponent<BoxCollider>():null;
                        if(box==null) throw new InvalidOperationException("Reference collider was reorganized; preserve and review manually: "+asset.path);
                        Vector3 center=UndoValue(box.center,old.oldCenter,old.center,asset.path+" center");
                        Vector3 size=UndoValue(box.size,old.oldSize,old.size,asset.path+" size");
                        if(Vector3.Distance(center,box.center)>0.00001f || Vector3.Distance(size,box.size)>0.00001f)
                        {
                            plan.Shapes.Add(new Shape { Owner=owner,Template=box,Fit=new Bounds(center,size) });
                            plan.Changed=true;
                        }
                    }
                }

                foreach(string target in Others)
                {
                    Plan plan=Load(Root+target,false,plans);
                    bool moving=target.StartsWith("NavigateLeftRight/",StringComparison.Ordinal);
                    Transform visual=plan.Root.transform.Find(moving?"Mover/Visual":"Visual");
                    plan.OldCollision=plan.Root.transform.Find(moving?"Mover/Collision":"Collision");
                    if(visual==null || plan.OldCollision==null)
                        throw new InvalidOperationException("Expected untouched non-reference Visual/Collision layout: "+plan.Path);
                    Collider[] old=plan.Root.GetComponentsInChildren<Collider>(true);
                    if(old.Length==0 || old.Any(c=>!(c is BoxCollider) || c.isTrigger || !c.enabled || !c.transform.IsChildOf(plan.OldCollision)))
                        throw new InvalidOperationException("Unexpected collider outside replaceable legacy group: "+plan.Path);
                    if(plan.OldCollision.GetComponentsInChildren<Component>(true).Any(c=>!(c is Transform) && !(c is BoxCollider)))
                        throw new InvalidOperationException("Legacy collision group owns non-collider content: "+plan.Path);

                    if(moving)
                    {
                        Rigidbody[] bodies=plan.Root.GetComponentsInChildren<Rigidbody>(true);
                        if(bodies.Length!=1 || !bodies[0].isKinematic || bodies[0].useGravity || visual.parent!=bodies[0].transform)
                            throw new InvalidOperationException("Unexpected Mover physics owner: "+plan.Path);
                        foreach(Transform part in visual)
                        {
                            Renderer[] renderers=EditedObstacleColliderFitter.MeshRenderers(part);
                            if(renderers.Length==0) throw new InvalidOperationException("Empty visual part: "+part.name);
                            BoxCollider template=old.Cast<BoxCollider>().FirstOrDefault(c=>c.name=="COL_"+part.name) ?? (BoxCollider)old[0];
                            AddShape(plan,part,template,renderers);
                        }
                    }
                    else
                    {
                        if(old.Length!=1) throw new InvalidOperationException("Jump row must retain one continuous collider: "+plan.Path);
                        AddShape(plan,visual,(BoxCollider)old[0],EditedObstacleColliderFitter.MeshRenderers(visual));
                        Shape shape=plan.Shapes[0];
                        if(Mathf.Abs(shape.Fit.size.x-9f)>.005f || Mathf.Abs(shape.Fit.size.y-.72f)>.005f)
                            throw new InvalidOperationException("Preserve the authored 9m / 0.72m jump row: "+plan.Path);
                    }
                    plan.Changed=true;
                }

                string stamp=DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string backup=Output+"/Before_"+stamp;
                Directory.CreateDirectory(backup);
                foreach(Plan plan in plans)
                {
                    File.Copy(plan.Path,backup+"/"+Path.GetFileName(plan.Path));
                    File.Copy(plan.Path+".meta",backup+"/"+Path.GetFileName(plan.Path)+".meta");
                }
                if(File.Exists(Request)) File.Move(Request,Request+".consumed_"+stamp);
                var entries=new List<Entry>();
                foreach(Plan plan in plans)
                {
                    foreach(Shape shape in plan.Shapes)
                    {
                        BoxCollider box=plan.Reference?shape.Template:shape.Owner.gameObject.AddComponent<BoxCollider>();
                        if(!plan.Reference)
                        {
                            box.sharedMaterial=shape.Template.sharedMaterial;
                            box.contactOffset=shape.Template.contactOffset;
                            box.includeLayers=shape.Template.includeLayers;
                            box.excludeLayers=shape.Template.excludeLayers;
                            box.layerOverridePriority=shape.Template.layerOverridePriority;
                            box.providesContacts=shape.Template.providesContacts;
                            box.enabled=true;
                            box.isTrigger=false;
                        }
                        box.center=shape.Fit.center;
                        box.size=shape.Fit.size;
                        shape.Created=box;
                    }
                    if(!plan.Reference) Object.DestroyImmediate(plan.OldCollision.gameObject);
                    if(ArtState(plan.Root)!=plan.ArtState)
                        throw new InvalidOperationException("Unexpected artwork/motion change: "+plan.Path);
                    BoxCollider[] final=plan.Root.GetComponentsInChildren<BoxCollider>(true);
                    if(!plan.Reference && final.Length!=plan.Shapes.Count)
                        throw new InvalidOperationException("Duplicate or missing colliders: "+plan.Path);
                    foreach(Shape shape in plan.Shapes)
                        if(Vector3.Distance(shape.Created.center,shape.Fit.center)>.00002f || Vector3.Distance(shape.Created.size,shape.Fit.size)>.00002f)
                            throw new InvalidOperationException("Collider fit mismatch: "+plan.Path);
                    if(plan.Changed)
                    {
                        PrefabUtility.SaveAsPrefabAsset(plan.Root,plan.Path,out bool saved);
                        if(!saved) throw new IOException("Could not save "+plan.Path);
                    }
                    Bounds bounds=EditedObstacleColliderFitter.BoxBoundsInRoot(plan.Root,final);
                    entries.Add(new Entry
                    {
                        path=plan.Path,
                        operation=plan.Reference?(plan.Changed?"restored only previous assistant collider edits":"reference already restored; preserved without save"):"direct-owner collider refinement",
                        boxes=final.Length, min=bounds.min,max=bounds.max,
                        owners=final.Select(c=>AnimationUtility.CalculateTransformPath(c.transform,plan.Root.transform)).ToArray()
                    });
                }
                var result=new Result { backup=backup,assets=entries.ToArray() };
                File.WriteAllText(Output+"/CorrectionReport.json",JsonUtility.ToJson(result,true));
                var text=new StringBuilder("OTHER_OBSTACLE_COLLIDERS_REFINED\nBackup: "+backup+"\n");
                foreach(Entry entry in entries)
                    text.AppendLine(Path.GetFileName(entry.path)+" | "+entry.operation+" | boxes="+entry.boxes+" | "+string.Join(", ",entry.owners));
                text.AppendLine("Reference geometry/structure preserved; original collider edits restored only when still matching our prior changes. No scene/runtime-code changes or gameplay tests.");
                File.WriteAllText(Output+"/CorrectionReport.txt",text.ToString());
                Debug.Log("OTHER_OBSTACLE_COLLIDERS_REFINED: "+Output+"/CorrectionReport.txt");
            }
            finally { foreach(Plan plan in plans) if(plan.Root!=null) PrefabUtility.UnloadPrefabContents(plan.Root); }
        }

        static Plan Load(string path,bool reference,List<Plan> plans)
        {
            if(!File.Exists(path)) throw new FileNotFoundException(path);
            var plan=new Plan { Path=path,Reference=reference,Root=PrefabUtility.LoadPrefabContents(path) };
            plans.Add(plan);
            plan.ArtState=ArtState(plan.Root);
            return plan;
        }
        static void AddShape(Plan plan,Transform owner,BoxCollider template,Renderer[] renderers)
        {
            if(owner.gameObject.layer!=template.gameObject.layer || owner.GetComponents<Collider>().Length!=0)
                throw new InvalidOperationException("Unexpected target owner/layer: "+owner.name);
            Bounds fit=EditedObstacleColliderFitter.FitBounds(owner,renderers);
            if(fit.size.x<=0 || fit.size.y<=0 || fit.size.z<=0) throw new InvalidOperationException("Degenerate shape.");
            plan.Shapes.Add(new Shape { Owner=owner,Template=template,Fit=fit });
        }
        static Vector3 UndoValue(Vector3 current,Vector3 original,Vector3 ours,string label)
        {
            if(Vector3.Distance(original,ours)<=.00001f || Vector3.Distance(current,original)<=.00001f) return current;
            if(Vector3.Distance(current,ours)<=.00001f) return original;
            throw new InvalidOperationException("Later user collider edit detected; do not overwrite: "+label);
        }
        static string ArtState(GameObject root)
        {
            var text=new StringBuilder();
            foreach(Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string path=AnimationUtility.CalculateTransformPath(t,root.transform);
                if(path=="Collision" || path.StartsWith("Collision/",StringComparison.Ordinal) ||
                    path=="Mover/Collision" || path.StartsWith("Mover/Collision/",StringComparison.Ordinal)) continue;
                text.AppendLine(path+"|"+t.localPosition.ToString("F9")+"|"+t.localRotation.ToString("F9")+"|"+t.localScale.ToString("F9")+
                    "|"+t.gameObject.activeSelf+"|"+t.gameObject.layer+"|"+t.gameObject.tag);
                foreach(Component c in t.GetComponents<Component>())
                {
                    if(c==null) throw new InvalidOperationException("Missing script: "+path);
                    if(!(c is Transform) && !(c is Collider)) text.AppendLine(c.GetType().FullName+"|"+EditorJsonUtility.ToJson(c));
                }
            }
            return text.ToString();
        }
    }
}
