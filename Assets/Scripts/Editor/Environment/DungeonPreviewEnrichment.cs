using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessRollball.Editor
{
    internal static partial class DungeonResortPreviewBuilder
    {
        [MenuItem("Tools/Endless Rollball/Environment/Enrich Existing Dungeon Preview %#&a")]
        static void EnrichDungeon()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode first.");
            var root=scene.GetRootGameObjects().Single(g=>g.name==RootName);
            if(root.transform.Find("10_Enriched_Dungeon_Dressing"))
                throw new InvalidOperationException("Enrichment already exists; your edits are preserved.");
            string backup="Artifacts/EnvironmentPreviewBackups/DungeonEnrichment_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath,backup+"/EnvironmentPreview_SavedBefore.unity");
            EditorSceneManager.SaveScene(scene,backup+"/EnvironmentPreview_LiveBefore.unity",true);
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Enrich existing dungeon preview");
            Undo.RegisterFullObjectHierarchyUndo(root,"Enrich existing dungeon preview");
            var notes=new StringBuilder("In-place enrichment of the approved 48m dungeon. Camera, lighting, fog, route, walls and ceiling preserved.\n");
            Remapped.Clear();Report.Clear();
            try
            {
                var alcoves=root.transform.Find("04_Treasure_And_Statue_Alcoves");
                foreach(Transform t in alcoves)
                {
                    var b=Bounds(t.gameObject);
                    if(t.name.StartsWith("Guardian_Statue_"))
                    {
                        t.localScale*=1.5f;
                        CentreAtGround(t.gameObject,new Vector3(b.center.x,b.min.y,b.center.z-1.2f));
                        notes.AppendLine("Enlarged guardian 150%: "+t.name);
                    }
                    else if(t.name.StartsWith("Statue_Dais_"))
                    {
                        t.localScale=Vector3.Scale(t.localScale,new Vector3(2.7f/b.size.x,1,2.5f/b.size.z));
                        CentreAtGround(t.gameObject,new Vector3(b.center.x,b.min.y,b.center.z-1.2f));
                    }
                    else if(t.name.StartsWith("Treasure_Chest_"))
                    {
                        t.localScale*=1.35f;
                        CentreAtGround(t.gameObject,new Vector3(b.center.x,b.min.y,b.center.z-1.2f));
                    }
                    else if(t.name.StartsWith("Treasure_Coins_"))
                    {
                        t.localScale*=1.4f;CentreAtGround(t.gameObject,new Vector3(b.center.x,b.min.y,b.center.z));
                    }
                }
                foreach(Transform t in root.transform.Find("05_Pots_Rubble_And_Storage"))
                {
                    if(!t.name.StartsWith("Ancient_Urn_"))continue;
                    var b=Bounds(t.gameObject);t.localScale*=1.3f;
                    CentreAtGround(t.gameObject,new Vector3(b.center.x,b.min.y,b.center.z));
                }
                var extra=EnrichmentGroup("10_Enriched_Dungeon_Dressing",root.transform);
                var treasures=EnrichmentGroup("01_Grouped_Treasure_Displays",extra);
                var urns=EnrichmentGroup("02_Urn_And_Offering_Groups",extra);
                var banners=EnrichmentGroup("03_Hanging_Hall_Banners",extra);
                var shelves=EnrichmentGroup("04_Relic_Shelves_And_Storage",extra);
                var stones=EnrichmentGroup("05_Rubble_And_Moss_Groups",extra);
                var occupied=new List<Bounds>();
                foreach(string category in new[]{"03_Columns_And_Arches","04_Treasure_And_Statue_Alcoves","05_Pots_Rubble_And_Storage","06_Moss_And_Overgrowth","07_Torches_And_Warm_Lighting"})
                    foreach(Transform item in root.transform.Find(category))
                        if(item.gameObject.activeInHierarchy && item.GetComponentsInChildren<Renderer>().Length>0)
                            occupied.Add(Bounds(item.gameObject));

                // High-level accents fill wall space without using the playable route.
                for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++)
                {
                    var g=Model(Kay+"fbx(unity)/banner_pattern"+(i%2==0?"A_red":"B_blue")+".fbx",banners,
                        "Hall_Banner_"+side+"_"+i,new Vector3(side*10.02f,4.7f,4.8f+i*12),new Vector3(.4f,3.8f,2.3f),side<0?90:-90,false);
                    Undo.RegisterCreatedObjectUndo(g,"Add dungeon hall banner");
                    PlaceEnrichment(g,new Vector3(side*10.02f,4.7f,4.8f+i*12),occupied,true,notes);
                }
                for(int side=-1;side<=1;side+=2)for(int i=0;i<3;i++)
                {
                    var group=EnrichmentGroup("Treasure_Display_"+side+"_"+i,treasures);
                    Model(Kay+"fbx(unity)/chest_gold.fbx",group,"Open_Gold_Chest",new Vector3(0,0,0),new Vector3(1.9f,1.6f,1.5f),side<0?65:-65,false);
                    Model(Kay+"fbx(unity)/coin_stack_large.fbx",group,"Coin_Pile_Large",new Vector3(.75f,0,.9f),new Vector3(.85f,.6f,.85f),i*31,false);
                    Model(Kay+"fbx(unity)/coin_stack_medium.fbx",group,"Coin_Pile_Medium",new Vector3(-.6f,0,.95f),new Vector3(.6f,.45f,.6f),i*51,false);
                    Model(Ruins+"Pot3.fbx",group,"Treasure_Offering_Pot",new Vector3(-.85f,0,-.6f),new Vector3(.7f,1,.7f),i*61,false);
                    PlaceEnrichment(group.gameObject,new Vector3(side*7.4f,0,10+i*15.5f),occupied,false,notes);
                }
                for(int side=-1;side<=1;side+=2)for(int i=0;i<3;i++)
                {
                    var group=EnrichmentGroup("Ceramic_Offering_"+side+"_"+i,urns);
                    Model(Ruins+(i%2==0?"Pot1.fbx":"Pot2.fbx"),group,"Large_Ancient_Urn",Vector3.zero,new Vector3(1.2f,1.9f,1.2f),i*67,false);
                    Model(Ruins+"Pot1_Broken.fbx",group,"Broken_Companion_Urn",new Vector3(.8f,0,.7f),new Vector3(.65f,.8f,.65f),i*41,false);
                    Model(Ruins+"Candles_1.fbx",group,"Offering_Candles",new Vector3(-.5f,0,.75f),new Vector3(.55f,.75f,.55f),0,false);
                    PlaceEnrichment(group.gameObject,new Vector3(side*7.2f,0,8+i*15),occupied,false,notes);
                }
                for(int side=-1;side<=1;side+=2)for(int i=0;i<2;i++)
                {
                    var g=Model(Ruins+"Bookcase_Full.fbx",shelves,"Relic_Shelf_"+side+"_"+i,Vector3.zero,
                        new Vector3(1,3.2f,2.2f),side<0?90:-90,false);
                    Undo.RegisterCreatedObjectUndo(g,"Add dungeon relic shelf");
                    PlaceEnrichment(g,new Vector3(side*9.5f,0,15+i*24),occupied,false,notes);
                }
                for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++)
                {
                    var group=EnrichmentGroup("Weathered_Masonry_"+side+"_"+i,stones);
                    Model(Ruins+"Bricks.fbx",group,"Fallen_Stone_Blocks",Vector3.zero,new Vector3(.7f,.45f,.8f),i*43,false);
                    Model(Ruins+"Bush_Round.fbx",group,"Moss_And_Foliage",new Vector3(.25f,0,.35f),new Vector3(.45f,.4f,.45f),i*59,false);
                    PlaceEnrichment(group.gameObject,new Vector3(side*9,0,9+i*11),occupied,false,notes);
                }
                // Only newly added content is cleaned. Existing lighting and user components are untouched.
                StreetCitySceneryExtractor.StripNonVisualComponents(extra.gameObject);
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
                notes.AppendLine("Imported models remain sourced from the existing Dungeon folder. No tests, Play Mode, collider or gameplay changes.");
                File.WriteAllText(backup+"/Enrichment.txt",notes.ToString());
                Undo.CollapseUndoOperations(undo);Selection.activeGameObject=extra.gameObject;
                Debug.Log("DUNGEON_ENRICHMENT_SAVED: larger guardians/treasure/urns, 6 treasure displays, 6 ceramic offerings, 8 banners, 4 relic shelves and compact rubble where space permits. Backup: "+backup);
            }
            catch {Undo.RevertAllDownToGroup(undo);throw;}
        }

        static Transform EnrichmentGroup(string name,Transform parent)
        {var group=Group(name,parent);Undo.RegisterCreatedObjectUndo(group.gameObject,"Enrich dungeon dressing");return group;}

        static void CentreAtGround(GameObject item,Vector3 point)
        {var b=Bounds(item);item.transform.position+=point-new Vector3(b.center.x,b.min.y,b.center.z);}

        static void PlaceEnrichment(GameObject item,Vector3 desired,List<Bounds> occupied,bool wallMounted,StringBuilder notes)
        {
            var options=new List<Vector3>{desired};
            float sign=Mathf.Sign(desired.x);
            // Stay on the original side. Search in full 3D so banners can sit above low dressing.
            for(float x=wallMounted?9.8f:6.2f;x<=10.1f;x+=.3f)
                for(float z=1.5f;z<47;z+=.5f)options.Add(new Vector3(sign*x,desired.y,z));
            foreach(var point in options.OrderBy(p=>(p-desired).sqrMagnitude))
            {
                CentreAtGround(item,point);var b=Bounds(item);
                if(b.min.z<.35f || b.max.z>47.65f)continue;
                if(sign<0 && (b.min.x<-10.35f || b.max.x>-5.5f))continue;
                if(sign>0 && (b.min.x<5.5f || b.max.x>10.35f))continue;
                if(occupied.Any(o=>b.min.x<o.max.x+.12f && b.max.x>o.min.x-.12f &&
                    b.min.z<o.max.z+.12f && b.max.z>o.min.z-.12f && b.min.y<o.max.y+.04f && b.max.y>o.min.y-.04f))continue;
                occupied.Add(b);notes.AppendLine(item.name+" | "+b.ToString("F2"));return;
            }
            if(item.name.StartsWith("Weathered_Masonry_"))
            {
                // Secondary ground scatter is optional once the main displays occupy a bay.
                notes.AppendLine("Omitted optional scatter (no clear pocket): "+item.name);
                UnityEngine.Object.DestroyImmediate(item);return;
            }
            throw new InvalidOperationException("No uncluttered roadside space for "+item.name+". Changes reverted.");
        }
    }
}
