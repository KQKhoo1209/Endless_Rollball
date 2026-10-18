using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EndlessRollball.Editor
{
    internal static partial class BeachResortPreviewBuilder
    {
        [MenuItem("Tools/Endless Rollball/Environment/Refine Edited Beach Resort Layout %#&h")]
        static void RefineEditedBeach()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode.");
            var root=scene.GetRootGameObjects().Single(g=>g.name==RootName);
            var village=root.transform.Find("03_Resort_Village");
            var nature=root.transform.Find("05_Palms_And_Coastal_Nature");
            var dressing=root.transform.Find("07_Beach_Dressing");
            if(nature.Find("Grouped_Palm_Gardens"))throw new InvalidOperationException("Grouped layout already exists; user edits preserved.");
            string backup="Artifacts/EnvironmentPreviewBackups/BeachGarden_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath,backup+"/EnvironmentPreview_SavedBefore.unity");
            EditorSceneManager.SaveScene(scene,backup+"/EnvironmentPreview_LiveBefore.unity",true);
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refine beach resort gardens");
            Undo.RegisterFullObjectHierarchyUndo(root,"Refine beach resort gardens");
            var notes=new StringBuilder("Preserved edited truck, villas, beach cabin, camera, lighting, terrain and deleted objects.\n");
            var changed=new List<GameObject>();
            try
            {
                var blockers=new List<Bounds>();
                foreach(Transform item in village)
                    if(item.name!="Garden_Cabin" && item.gameObject.activeSelf)blockers.Add(BoundsOf(item.gameObject));
                var vendors=root.transform.Find("04_IceCream_And_Beach_Pavilions");
                foreach(Transform item in vendors)if(item.gameObject.activeSelf && item.GetComponentsInChildren<Renderer>(true).Length>0)
                    blockers.Add(BoundsOf(item.gameObject));
                var lookout=dressing.Find("Beach_Lookout_Hut");if(lookout)blockers.Add(BoundsOf(lookout.gameObject));
                var marina=root.transform.Find("06_Jetty_And_Boats");
                foreach(Transform item in marina)if(item.name.StartsWith("Jetty_")&&item.gameObject.activeSelf)blockers.Add(BoundsOf(item.gameObject));
                var cabin=village.Find("Garden_Cabin");
                if(cabin)
                {
                    PutNear(cabin.gameObject,new Vector3(-13.2f,-.2f,23),blockers,5,.4f);
                    blockers.Add(BoundsOf(cabin.gameObject));
                    notes.AppendLine("Existing garden cabin moved beside promenade: "+BoundsOf(cabin.gameObject));
                }
                var spots=new[]{new Vector3(-13.2f,-.2f,0),new Vector3(-13.2f,-.2f,46),new Vector3(10,-.3f,16)};
                for(int i=0;i<spots.Length;i++)
                {
                    var added=Place(Pack+"WoodenHouse/cabin1.fbx",village,spots[i],i==2?-90:90,new Vector3(5.2f,5.2f,6),"Garden_Cabin_"+(i+2).ToString("00"));
                    Undo.RegisterCreatedObjectUndo(added,"Add garden cabin");changed.Add(added);
                    PutNear(added,spots[i],blockers,5,.55f);blockers.Add(BoundsOf(added));
                    notes.AppendLine(added.name+" "+BoundsOf(added));
                    var cb=BoundsOf(added);float side=Mathf.Sign(cb.center.x);
                    float inner=side<0?cb.max.x:cb.min.x;
                    float start=side*5.5f;float length=Mathf.Abs(inner-start);
                    var walk=Group("Cabin_Approach_"+(i+2).ToString("00"),village);Undo.RegisterCreatedObjectUndo(walk.gameObject,"Add cabin approach");
                    Box("Short_Wooden_Path",walk,new Vector3((inner+start)/2,-.15f,cb.center.z),new Vector3(length,.1f,1.4f),Mat("Deck_WarmWood","#A9683E",.2f));
                }

                var landmarks=Group("09_Rear_Landmarks",root.transform);Undo.RegisterCreatedObjectUndo(landmarks.gameObject,"Group rear landmarks");
                var castle=dressing.Find("Beach_Sandcastle");
                if(castle)
                {
                    castle.SetParent(landmarks,true);castle.localScale*=1.3f;
                    PutNear(castle.gameObject,new Vector3(-23,-.2f,56),blockers,5,.6f);
                    blockers.Add(BoundsOf(castle.gameObject));notes.AppendLine("Enlarged rear sandcastle: "+BoundsOf(castle.gameObject));
                }
                // Reuse the supplied rock formations, rather than introduce a castle asset.
                var rocks=nature.Cast<Transform>().Where(t=>t.name.StartsWith("Sandstone_Cluster_")).ToArray();
                var rockSpots=new[]{new Vector3(16,-.3f,53),new Vector3(21,-.3f,57),new Vector3(23,-.3f,51),
                    new Vector3(14,-.3f,59),new Vector3(24,-.3f,59),new Vector3(-28,-.2f,39),
                    new Vector3(-28,-.2f,45),new Vector3(-29,-.2f,32),new Vector3(20,-.3f,61)};
                for(int i=0;i<rocks.Length;i++)
                {
                    rocks[i].SetParent(landmarks,true);rocks[i].localScale*=i<5?3.4f:2.4f;
                    PutNear(rocks[i].gameObject,rockSpots[i%rockSpots.Length],blockers,5,.6f);
                    notes.AppendLine("Rear rock "+rocks[i].name+" "+BoundsOf(rocks[i].gameObject));
                }

                var palms=nature.Cast<Transform>().Where(t=>t.name.StartsWith("Promenade_Palm_")||t.name.StartsWith("Beach_Grove_")).ToList();
                var grass=nature.Cast<Transform>().Where(t=>t.name.StartsWith("Coastal_Grass_")).ToList();
                var groups=Group("Grouped_Palm_Gardens",nature);Undo.RegisterCreatedObjectUndo(groups.gameObject,"Create palm gardens");
                var archive=Group("Previous_Scattered_Planting_Inactive",nature);Undo.RegisterCreatedObjectUndo(archive.gameObject,"Preserve unused planting");archive.gameObject.SetActive(false);
                var anchors=new[]{new Vector3(-9,-.2f,5),new Vector3(-9,-.2f,16.7f),new Vector3(-9,-.2f,29),new Vector3(-9,-.2f,40.5f),
                    new Vector3(18,-.3f,9),new Vector3(18,-.3f,29),new Vector3(19,-.3f,46)};
                var gardenBounds=new List<Bounds>();int palmIndex=0,grassIndex=0;
                for(int i=0;i<anchors.Length;i++)
                {
                    var group=Group("Palm_Garden_"+(i+1).ToString("00"),groups);Undo.RegisterCreatedObjectUndo(group.gameObject,"Create palm garden");
                    int count=i<4?2:3;
                    var offsets=i<4?new[]{new Vector3(-.9f,0,.3f),new Vector3(.9f,0,-.3f)}:
                        new[]{new Vector3(0,0,-.9f),new Vector3(2.4f,0,.6f),new Vector3(-1.7f,0,1.4f)};
                    for(int p=0;p<count;p++)
                    {
                        Transform palm;
                        if(palmIndex<palms.Count)palm=palms[palmIndex++];
                        else {var g=Place(Pirate+"palm-detailed-straight.fbx",group,Vector3.zero,25*p,new Vector3(3.1f,4.7f,3.1f),"Additional_Palm");Undo.RegisterCreatedObjectUndo(g,"Add palm");palm=g.transform;}
                        palm.SetParent(group,true);var size=BoundsOf(palm.gameObject).size;
                        float width=i<4?3.1f:3.5f;
                        palm.localScale*=Mathf.Min(width/size.x,width/size.z);
                        MoveBoundsTo(palm.gameObject,anchors[i]+offsets[p]);
                    }
                    var goff=new[]{new Vector3(-1.7f,0,-.8f),new Vector3(1.6f,0,.9f),new Vector3(-.5f,0,1.6f),new Vector3(.4f,0,-1.5f)};
                    for(int g=0;g<4;g++)
                    {
                        Transform tuft;
                        if(grassIndex<grass.Count)tuft=grass[grassIndex++];
                        else {var added=Place(Pirate+"grass-plant.fbx",group,Vector3.zero,g*53,new Vector3(1,.65f,1),"Additional_Coastal_Grass");Undo.RegisterCreatedObjectUndo(added,"Add coastal grass");tuft=added.transform;}
                        tuft.SetParent(group,true);MoveBoundsTo(tuft.gameObject,anchors[i]+goff[g]);
                    }
                    PutNear(group.gameObject,anchors[i],blockers.Concat(gardenBounds).ToList(),7,.35f);
                    gardenBounds.Add(BoundsOf(group.gameObject));notes.AppendLine(group.name+" palms="+count+" coastal grass=4 "+BoundsOf(group.gameObject));
                }
                for(int i=palmIndex;i<palms.Count;i++)palms[i].SetParent(archive,true);
                for(int i=grassIndex;i<grass.Count;i++)grass[i].SetParent(archive,true);

                var shades=dressing.Cast<Transform>().Where(t=>t.name.StartsWith("Beach_Sunshade_")).ToArray();
                var lounges=Group("Palm_Side_Lounging",dressing);Undo.RegisterCreatedObjectUndo(lounges.gameObject,"Group beach lounging");
                var loungeSpots=new[]{new Vector3(14,-.3f,3),new Vector3(23.5f,-.3f,9),new Vector3(17.5f,-.3f,34.5f),new Vector3(23.5f,-.3f,28.5f),new Vector3(21,-.3f,40.5f)};
                var loungeBlockers=blockers.Concat(gardenBounds).ToList();
                for(int i=0;i<shades.Length;i++)
                {
                    shades[i].SetParent(lounges,true);
                    PutNear(shades[i].gameObject,loungeSpots[i%loungeSpots.Length],loungeBlockers,7,.25f);
                    loungeBlockers.Add(BoundsOf(shades[i].gameObject));notes.AppendLine("Sunshade near gardens: "+BoundsOf(shades[i].gameObject));
                }
                // No camera, skybox, route, truck or villa reset. Save the user's edited scene plus these additions.
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
                File.WriteAllText(backup+"/LayoutChanges.txt",notes.ToString());
                Undo.CollapseUndoOperations(undo);Selection.activeGameObject=groups.gameObject;
                Debug.Log("BEACH_GARDENS_SAVED: three added garden cabins, seven palm gardens (2–3 palms, four grasses each), five adjacent sunshades, enlarged rear sandcastle and rocks. User edits preserved. Backup: "+backup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);throw;
            }
        }

        static void MoveBoundsTo(GameObject item,Vector3 ground)
        {
            var b=BoundsOf(item);item.transform.position+=ground-new Vector3(b.center.x,b.min.y,b.center.z);
        }
        static bool FitsGarden(Bounds b,List<Bounds> blockers,float margin)
        {
            // Conservative whole-renderer footprints include canopy and roof overhangs.
            if(b.min.x<5.5f && b.max.x>-5.5f)return false;
            if(b.min.x<-32 || b.max.x>25.5f || b.min.z<-14 || b.max.z>64)return false;
            return !blockers.Any(o=>b.min.x<o.max.x+margin && b.max.x>o.min.x-margin && b.min.z<o.max.z+margin && b.max.z>o.min.z-margin);
        }
        static void PutNear(GameObject item,Vector3 desired,List<Bounds> blockers,int radius,float margin)
        {
            var candidates=new List<Vector3>{desired};
            for(int x=-radius*2;x<=radius*2;x++)for(int z=-radius*2;z<=radius*2;z++)
                candidates.Add(desired+new Vector3(x*.5f,0,z*.5f));
            foreach(var point in candidates.OrderBy(p=>(p-desired).sqrMagnitude))
            {
                MoveBoundsTo(item,point);
                if(FitsGarden(BoundsOf(item),blockers,margin))return;
            }
            throw new InvalidOperationException("No clear space for "+item.name+"; layout reverted rather than overlap a building.");
        }

        [MenuItem("Tools/Endless Rollball/Environment/Inspect Edited Beach Layout %#&q")]
        static void InspectEditedBeach()
        {
            var scene=SceneManager.GetActiveScene();
            var report=new StringBuilder();
            foreach(var root in scene.GetRootGameObjects())
            {
                report.AppendLine("ROOT "+root.name+" active="+root.activeSelf+" position="+root.transform.position);
                if(root.name!=RootName)continue;
                foreach(Transform group in root.transform)
                {
                    report.AppendLine("GROUP "+group.name+" position="+group.position+" scale="+group.localScale);
                    foreach(Transform item in group)
                    {
                        var renderers=item.GetComponentsInChildren<Renderer>(true);
                        report.AppendLine(item.name+" | active="+item.gameObject.activeSelf+" | pos="+item.position.ToString("F2")+" | rot="+item.eulerAngles.ToString("F1")+" | scale="+item.localScale.ToString("F3")+" | "+(renderers.Length>0?BoundsOf(item.gameObject).ToString("F2"):"empty")+" | source="+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(item.gameObject));
                    }
                }
            }
            Directory.CreateDirectory("Artifacts/BeachLayout");
            File.WriteAllText("Artifacts/BeachLayout/EditedLayoutBefore.txt",report.ToString());
            Debug.Log("Edited beach layout recorded without changes.");
        }
    }
}
