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
    internal static partial class DungeonResortPreviewBuilder
    {
        static readonly string[][] RichFloorProps = {
            new[]{"R:Column_Round_Short","R:Bush_Large","R:Pot3_Broken","R:Grass","R:Wall_Broken","R:Bush_2x1","R:Pot1_Broken","K:rubble_large",
                "R:DeadTree_3","R:Bush_Round","R:Pot2","R:Bricks","K:chest","R:Bush_1x1","R:Pot3","R:Grass"},
            new[]{"K:barrel_large","K:box_stacked","R:Pot2","K:trunk_medium_A","K:shelf_large","K:barrel_small_stack","K:box_small_decorated","R:Bricks",
                "K:crates_stacked","K:keg_decorated","K:trunk_small_A","R:Pot1_Broken","K:barrel_large_decorated","K:trunk_medium_B","R:Pot3","K:box_small"},
            new[]{"R:Column_Round_Short","R:Pot3","K:chest_gold","R:Candles_1","K:pillar_decorated","R:Pot2","K:coin_stack_large","K:trunk_small_C",
                "R:Statue_Fox","R:Pot1","K:chest","R:Candles_2","R:Statue_Stag","K:coin_stack_medium","R:Pot3_Broken","K:trunk_small_B"}
        };

        [MenuItem("Tools/Endless Rollball/Environment/Enrich Dungeon Prefabs C D E Floor Displays %#&z")]
        static void EnrichLastThreeDungeonPrefabs()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Save and close Prefab Mode, and remain in Edit Mode first.");
            var labels=new[]{"TreasuryGallery","ForgottenArchives","OvergrownSanctum","ExcavationStores","GuardianReliquary"};
            var paths=labels.Select((s,i)=>DungeonPrefabFolder+"/ENV_Dungeon_Scenery_"+(char)('A'+i)+"_"+s+"_24m.prefab").ToArray();
            var loaded=new List<GameObject>();
            string backup="Artifacts/EnvironmentPrefabBackups/DungeonFloorRichness_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            var notes=new StringBuilder("Floor-display enrichment for C-E only. A/B are read-only scale and density references.\nScene, tunnel shell, lamps, banners and track remain unchanged. No gameplay or performance tests run.\n\n");
            Remapped.Clear();Report.Clear();
            try {
                foreach(var path in paths)loaded.Add(PrefabUtility.LoadPrefabContents(path));
                for(int i=0;i<2;i++) {
                    notes.AppendLine("REFERENCE ONLY: "+paths[i]);
                    var bays=loaded[i].transform.Find("03_Themed_Side_Alcoves");
                    foreach(Transform bay in bays)notes.AppendLine("  "+bay.name+" scale="+bay.localScale+" bounds="+Bounds(bay.gameObject));
                }
                for(int v=2;v<5;v++) {
                    if(loaded[v].transform.Find("08_Enriched_Floor_Displays_v03"))throw new InvalidOperationException("Enrichment already applied; preserving edits.");
                    File.Copy(paths[v],backup+"/"+Path.GetFileName(paths[v]));
                    File.Copy(paths[v]+".meta",backup+"/"+Path.GetFileName(paths[v])+".meta");
                }
                for(int v=2;v<5;v++) {
                    var root=loaded[v].transform;
                    var displays=root.Find("03_Themed_Side_Alcoves");
                    notes.AppendLine("\nENRICHED: "+root.name);
                    var occupied=new List<Bounds>();
                    foreach(Transform bay in displays) {
                        if(!bay.name.StartsWith("Bay_"))continue;
                        bool left=bay.name.StartsWith("Bay_Left_");int side=left?-1:1;
                        int index=int.Parse(bay.name.Split('_')[2])-1;
                        var b=Bounds(bay.gameObject);
                        float desired=(v==2?1.48f:v==3?1.52f:1.42f)+.035f*((index+(left?0:1))%3);
                        float scale=Mathf.Min(desired,3.65f/b.size.x,6.3f/b.size.y,3.85f/b.size.z);
                        bay.localScale*=scale;
                        float z=new[]{3.3f,9.0f,15.0f,20.75f}[index]+(left?-.10f:.12f);
                        CentreAtGround(bay.gameObject,new Vector3(side*7.55f,-.05f,z));
                        foreach(Transform prop in bay)if(prop.GetComponentInChildren<Renderer>())occupied.Add(Bounds(prop.gameObject));
                        notes.AppendLine("  Enlarged "+bay.name+" factor="+scale.ToString("F2")+" bounds="+Bounds(bay.gameObject));
                    }
                    foreach(Transform structure in root.Find("02_Arches_And_Columns"))occupied.Add(Bounds(structure.gameObject));
                    var extra=Group("08_Enriched_Floor_Displays_v03",root);
                    int added=0;
                    for(int i=0;i<8;i++) {
                        int side=i<4?-1:1, bay=i%4;
                        float z=new[]{3.3f,9f,15f,20.75f}[bay];
                        var cluster=Group("Floor_Cluster_"+(side<0?"Left_":"Right_")+(bay+1).ToString("00"),extra);
                        for(int p=0;p<4;p++) {
                            string code=RichFloorProps[v-2][(i*2+p)%16];
                            bool tall=p==0;
                            var limit=tall?new Vector3(1.05f,3.8f,1.05f):p==1?new Vector3(1.1f,1.65f,1.1f):new Vector3(.85f,1.2f,.85f);
                            var prop=Model(BaySource(code),cluster,"Floor_"+p+"_"+code.Substring(2),Vector3.zero,limit,(side<0?90:-90)+17*i+31*p,false);
                            bool placed=false;
                            // Prefer rear-wall clusters, then gaps alongside the enlarged displays.
                            var slots=new List<Vector3>();
                            foreach(float x in new[]{9.55f,9.3f,6.15f,6.4f,8.6f,7.65f})
                                foreach(float dz in new[]{-1.45f,1.40f,-.65f,.65f,0f})slots.Add(new Vector3(side*x,-.05f,z+dz));
                            for(int shrink=0;shrink<2&&!placed;shrink++) {
                                foreach(var position in slots.Skip((i+p)%3).Concat(slots.Take((i+p)%3))) {
                                    CentreAtGround(prop,position);
                                    var b=Bounds(prop);
                                    float inner=side<0?-b.max.x:b.min.x,outer=side<0?-b.min.x:b.max.x;
                                    if(inner<5.6f||outer>10.04f||b.min.z<1.28f||b.max.z>22.78f)continue;
                                    var padded=b;padded.Expand(new Vector3(.12f,.02f,.12f));
                                    if(occupied.Any(o=>padded.Intersects(o)))continue;
                                    occupied.Add(b);placed=true;added++;
                                    notes.AppendLine("  Added "+cluster.name+"/"+prop.name+" "+b);break;
                                }
                                if(!placed)prop.transform.localScale*=.82f;
                            }
                            if(!placed)Object.DestroyImmediate(prop);
                        }
                        if(cluster.childCount==0)Object.DestroyImmediate(cluster.gameObject);
                    }
                    StreetCitySceneryExtractor.StripNonVisualComponents(extra.gameObject);
                    notes.AppendLine("  Added floor props: "+added);
                }
                for(int v=2;v<5;v++)if(!PrefabUtility.SaveAsPrefabAsset(loaded[v],paths[v]))throw new IOException("Save failed: "+paths[v]);
                AssetDatabase.SaveAssets();File.WriteAllText(backup+"/EnrichmentReport.txt",notes.ToString());
                Debug.Log("DUNGEON_CDE_ENRICHED: Larger floor displays and additional asymmetric dressing saved. A/B unchanged. Backup: "+backup);
            }
            finally {foreach(var root in loaded)if(root)PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
