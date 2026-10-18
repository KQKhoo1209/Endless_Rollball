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
    internal static partial class BeachResortPreviewBuilder
    {
        [MenuItem("Tools/Endless Rollball/Environment/Arrange Beach Foreground And Resort Background %#&f")]
        static void ArrangeDepthLayers()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode.");
            var root=scene.GetRootGameObjects().Single(g=>g.name==RootName);
            if(root.transform.Find("Foreground_Beach"))throw new InvalidOperationException("Depth layers already exist; existing edits preserved.");
            var village=root.transform.Find("03_Resort_Village");
            var nature=root.transform.Find("05_Palms_And_Coastal_Nature");
            var vendors=root.transform.Find("04_IceCream_And_Beach_Pavilions");
            var dressing=root.transform.Find("07_Beach_Dressing");
            var landmarks=root.transform.Find("09_Rear_Landmarks");
            var gardens=nature.Find("Grouped_Palm_Gardens");
            var lounges=dressing.Find("Palm_Side_Lounging");
            if(!village || !gardens || !lounges || !landmarks)throw new InvalidOperationException("Expected edited beach layout not found.");
            string backup="Artifacts/EnvironmentPreviewBackups/BeachLayers_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath,backup+"/EnvironmentPreview_SavedBefore.unity");
            EditorSceneManager.SaveScene(scene,backup+"/EnvironmentPreview_LiveBefore.unity",true);
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Arrange beach foreground and resort background");
            Undo.RegisterFullObjectHierarchyUndo(root,"Arrange beach depth layers");
            var report=new StringBuilder("Foreground and background are depth bands beside the entire 48m route, not consecutive track sections.\nPreserved source assets, materials, camera, lighting, skybox, sea, marina, edited rotations and deleted route geometry.\n");
            try
            {
                var foreground=LayerGroup("Foreground_Beach",root.transform);
                var background=LayerGroup("Background_Resort",root.transform);
                var villas=LayerGroup("01_Villas",background);
                var cabins=LayerGroup("02_Garden_Cabins",background);
                var bigCastles=LayerGroup("03_Large_Sandcastles",background);
                var rocks=LayerGroup("04_Rock_Landmarks",background);
                var paths=LayerGroup("05_Cabin_Approaches",background);
                var trucks=LayerGroup("03_IceCream_Trucks",foreground);
                var smallCastles=LayerGroup("04_Small_Sandcastles",foreground);
                var facilities=LayerGroup("05_Beach_Pavilions_And_Dressing",foreground);
                gardens.SetParent(foreground,true);gardens.name="01_Palm_Gardens";
                lounges.SetParent(foreground,true);lounges.name="02_Sunshades";

                var buildingBounds=new List<Bounds>();
                foreach(var t in village.Cast<Transform>().Where(t=>t.name.StartsWith("Resort_Villa_")).ToArray())
                {
                    var b=BoundsOf(t.gameObject);t.SetParent(villas,true);
                    PutDepth(t.gameObject,new Vector3(-24,b.min.y,b.center.z),buildingBounds,false,4,.6f);
                    buildingBounds.Add(BoundsOf(t.gameObject));report.AppendLine(t.name+" background: "+BoundsOf(t.gameObject));
                }
                foreach(var t in village.Cast<Transform>().Where(t=>t.name.Contains("Cabin")&&!t.name.Contains("Approach")).ToArray())
                {
                    var b=BoundsOf(t.gameObject);t.SetParent(cabins,true);
                    float side=Mathf.Sign(b.center.x);
                    PutDepth(t.gameObject,new Vector3(side*20.5f,b.min.y,b.center.z),buildingBounds,false,4,.6f);
                    buildingBounds.Add(BoundsOf(t.gameObject));report.AppendLine(t.name+" background: "+BoundsOf(t.gameObject));
                }
                foreach(var t in village.Cast<Transform>().ToArray())
                {
                    if(!t.name.Contains("Approach"))continue;
                    t.SetParent(paths,true);
                    string suffix=t.name.Substring(t.name.LastIndexOf('_')+1);
                    var cabin=cabins.Find("Garden_Cabin_"+suffix);
                    if(!cabin)continue;
                    var cb=BoundsOf(cabin.gameObject);float inner=cb.center.x<0?cb.max.x:cb.min.x;
                    float near=Mathf.Sign(cb.center.x)*16.8f;
                    var slab=t.GetComponentsInChildren<MeshRenderer>().FirstOrDefault();
                    if(slab)
                    {
                        slab.transform.position=new Vector3((inner+near)/2,cb.min.y+.04f,cb.center.z);
                        slab.transform.localScale=new Vector3(Mathf.Abs(inner-near),.08f,1.4f);
                    }
                }
                foreach(var t in landmarks.Cast<Transform>().ToArray())
                {
                    var b=BoundsOf(t.gameObject);
                    if(t.name.Contains("Sandcastle"))
                    {
                        t.SetParent(bigCastles,true);
                        PutDepth(t.gameObject,new Vector3(-23,b.min.y,56.5f),buildingBounds,false,4,.4f);
                    }
                    else
                    {
                        t.SetParent(rocks,true);
                        if(b.center.x<0)
                        {
                            float z=t.name.EndsWith("5")?20:t.name.EndsWith("6")?43:26;
                            PutDepth(t.gameObject,new Vector3(-28.7f,b.min.y,z),buildingBounds,false,5,.4f);
                        }
                        else PutDepth(t.gameObject,new Vector3(Mathf.Max(18,b.center.x),b.min.y,b.center.z),buildingBounds,false,5,.4f);
                    }
                    report.AppendLine(t.name+" background landmark: "+BoundsOf(t.gameObject));
                }

                var foregroundBlockers=new List<Bounds>(buildingBounds);
                foreach(var t in vendors.Cast<Transform>().ToArray())
                {
                    if(t.name.StartsWith("IceCream"))t.SetParent(trucks,true);
                    else t.SetParent(facilities,true);
                    if(t.gameObject.activeSelf && t.GetComponentsInChildren<Renderer>(true).Length>0)
                        foregroundBlockers.Add(BoundsOf(t.gameObject));
                }
                var lookout=dressing.Find("Beach_Lookout_Hut");
                if(lookout)
                {
                    lookout.SetParent(facilities,true);var b=BoundsOf(lookout.gameObject);
                    PutDepth(lookout.gameObject,new Vector3(13.5f,b.min.y,0),foregroundBlockers,true,5,.4f);
                    foregroundBlockers.Add(BoundsOf(lookout.gameObject));
                }
                foreach(var t in dressing.Cast<Transform>().Where(t=>t.name.StartsWith("Resort_Barrel_")).ToArray())
                {
                    t.SetParent(facilities,true);
                    if(t.gameObject.activeSelf)foregroundBlockers.Add(BoundsOf(t.gameObject));
                }
                var truck=trucks.Cast<Transform>().FirstOrDefault(t=>t.name=="IceCream_Truck");
                if(truck)
                {
                    var extra=Object.Instantiate(truck.gameObject,trucks);extra.name="IceCream_Truck_Beach_Stop";
                    Undo.RegisterCreatedObjectUndo(extra,"Add second ice cream stop");
                    PutDepth(extra,new Vector3(9,-.3f,39),foregroundBlockers,true,5,.6f);
                    foregroundBlockers.Add(BoundsOf(extra));report.AppendLine(extra.name+" "+BoundsOf(extra));
                }
                var marina=root.transform.Find("06_Jetty_And_Boats");
                foreach(Transform t in marina)
                    if(t.name.StartsWith("Jetty_")&&t.gameObject.activeSelf)foregroundBlockers.Add(BoundsOf(t.gameObject));
                var desiredGardens=new[]{new Vector3(-9,-.2f,5),new Vector3(-12.5f,-.2f,16),new Vector3(-9,-.2f,29),new Vector3(-9,-.2f,41),
                    new Vector3(10.5f,-.3f,14),new Vector3(10.5f,-.3f,32),new Vector3(10.5f,-.3f,45)};
                var gardenList=gardens.Cast<Transform>().ToArray();
                for(int i=0;i<gardenList.Length;i++)
                {
                    PutDepth(gardenList[i].gameObject,desiredGardens[i%desiredGardens.Length],foregroundBlockers,true,6,.4f);
                    foregroundBlockers.Add(BoundsOf(gardenList[i].gameObject));report.AppendLine(gardenList[i].name+" foreground: "+BoundsOf(gardenList[i].gameObject));
                }
                var shadeSpots=new[]{new Vector3(13,-.3f,4),new Vector3(13,-.3f,21),new Vector3(13.8f,-.3f,37),new Vector3(-13,-.2f,9),new Vector3(-13,-.2f,34)};
                var shades=lounges.Cast<Transform>().ToArray();
                for(int i=0;i<shades.Length;i++)
                {
                    PutDepth(shades[i].gameObject,shadeSpots[i%shadeSpots.Length],foregroundBlockers,true,8,.3f);
                    foregroundBlockers.Add(BoundsOf(shades[i].gameObject));report.AppendLine(shades[i].name+" foreground: "+BoundsOf(shades[i].gameObject));
                }
                var castleSpots=new[]{new Vector3(-6.8f,-.2f,10),new Vector3(14.5f,-.3f,22.5f),new Vector3(-6.8f,-.2f,34)};
                for(int i=0;i<castleSpots.Length;i++)
                {
                    var g=Place(Pack+"SandCastle/SandCastle.fbx",smallCastles,castleSpots[i],i*63,new Vector3(1.6f,1.2f,1.6f),"Small_Sandcastle_"+(i+1).ToString("00"));
                    Undo.RegisterCreatedObjectUndo(g,"Add small beach sandcastle");
                    PutDepth(g,castleSpots[i],foregroundBlockers,true,5,.25f);foregroundBlockers.Add(BoundsOf(g));
                    report.AppendLine(g.name+" foreground: "+BoundsOf(g));
                }
                foreach(var t in dressing.Cast<Transform>().ToArray())t.SetParent(facilities,true);
                foreach(var t in nature.Cast<Transform>().Where(t=>t.name.StartsWith("Planter_Greenery_")).ToArray())t.SetParent(facilities,true);
                // Keep old organization and any unused user content, rather than deleting it.
                var previous=LayerGroup("Previous_Organization_Inactive",root.transform);
                foreach(var old in new[]{village,nature,vendors,dressing,landmarks})old.SetParent(previous,true);
                previous.gameObject.SetActive(false);
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
                File.WriteAllText(backup+"/LayerArrangement.txt",report.ToString());
                Undo.CollapseUndoOperations(undo);Selection.activeGameObject=foreground.gameObject;
                Debug.Log("BEACH_DEPTH_LAYERS_SAVED: Foreground_Beach and Background_Resort arranged. Two ice cream trucks, three small castles, seven palm gardens, five sunshades, background villas/cabins and large landmarks. Backup: "+backup);
            }
            catch {Undo.RevertAllDownToGroup(undo);throw;}
        }

        static Transform LayerGroup(string name,Transform parent)
        {
            var t=Group(name,parent);Undo.RegisterCreatedObjectUndo(t.gameObject,"Organize beach depth layers");return t;
        }
        static void PutDepth(GameObject item,Vector3 desired,List<Bounds> blockers,bool foreground,int radius,float margin)
        {
            var candidates=new List<Vector3>{desired};
            for(int x=-radius*2;x<=radius*2;x++)for(int z=-radius*2;z<=radius*2;z++)
                candidates.Add(desired+new Vector3(x*.5f,0,z*.5f));
            foreach(var point in candidates.OrderBy(p=>(p-desired).sqrMagnitude))
            {
                if(Mathf.Sign(point.x)!=Mathf.Sign(desired.x))continue;
                MoveBoundsTo(item,point);var b=BoundsOf(item);
                if(!FitsGarden(b,blockers,margin))continue;
                if(foreground && (b.min.x<-16.5f || b.max.x>16.5f))continue;
                if(!foreground && b.min.z<48 && (b.min.x<17.2f && b.max.x>-17.2f))continue;
                return;
            }
            throw new InvalidOperationException("No clear "+(foreground?"foreground":"background")+" placement for "+item.name+". Changes reverted.");
        }
    }
}
