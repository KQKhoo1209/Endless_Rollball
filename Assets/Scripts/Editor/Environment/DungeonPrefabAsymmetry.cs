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
        // Each row describes one independently dressed bay, not a mirrored template.
        // R = Quaternius, K = KayKit. Repeated small props retain shared mesh assets.
        static readonly string[][] AsymmetricBayRecipes = {
            new[] {
                "Fox_Guardian|R:Statue_Fox|K:coin_stack_medium|R:Candles_2",
                "Golden_Vault|K:chest_gold|R:Pot3|K:key",
                "Royal_Coin_Cache|K:coin_stack_large|K:chest|K:box_small_decorated",
                "Ceremonial_Urns|R:Pot3|R:Pot1|K:candle_triple",
                "Sealed_Treasury|K:crates_stacked|K:trunk_small_B|K:coin_stack_small",
                "Counting_Desk|K:table_medium_tablecloth|K:chair|K:coin_stack_large|K:bottle_A_green",
                "Traveller_Trunks|K:trunk_large_C|K:trunk_small_A|R:Pot2",
                "Candle_Shrine|K:pillar_decorated|R:Candles_1|K:coin_stack_medium" },
            new[] {
                "Bookcase_Study|R:Bookcase_Full|K:chair|K:trunk_small_B",
                "Key_Catalogue|K:shelf_small|K:box_small_decorated|K:keyring",
                "Candle_Workbench|K:table_long_decorated_A|K:stool|K:candle_triple|K:bottle_C_brown",
                "Potion_Shelves|K:shelves|R:Pot2|K:bottle_A_labeled_green",
                "Abandoned_Bookcase|R:Bookcase_Empty|K:trunk_medium_A|R:Bricks",
                "Broken_Reading_Nook|K:table_medium_broken|K:chair|K:candle_melted",
                "Document_Storage|K:trunk_large_B|K:box_stacked|K:keyring",
                "Relic_Cabinet|K:shelf_large|K:barrel_small|R:Pot1_Broken" },
            new[] {
                "Root_Monolith|R:Support_Tall|R:Bush_Round|R:Pot2_Broken",
                "Pottery_Garden|R:Bush_Large|R:Pot1|K:rubble_half",
                "Fallen_Arch_Stones|R:Wall_ArchRound_Overgrown_Broken|R:Grass|R:Bricks",
                "Overgrown_Chest|R:Chest|R:Bush_2x1|R:Pot3_Broken",
                "Ancient_Roots|R:DeadTree_2|R:Bush_Round|K:floor_dirt_small_weeds",
                "Lost_Column|R:Column_Round_Short|R:Pot1_Broken|R:Grass",
                "Forgotten_Supplies|K:crates_stacked|R:Bush_1x1|K:trunk_small_C",
                "Broken_Urn_Garden|R:Pot2_Broken|R:Bush_2x2|K:rubble_large" },
            new[] {
                "Excavation_Cart|R:Cart|K:box_small|R:Brick",
                "Keg_Store|K:keg_decorated|K:barrel_small|K:box_small_decorated",
                "Recovery_Trunks|K:trunk_large_A|K:trunk_medium_C|R:Pot1",
                "Excavation_Workbench|K:table_long_decorated_C|K:stool|R:Pot1_Broken|K:bottle_B_brown",
                "Crate_Rack|K:crates_stacked|K:barrel_small_stack|K:keyring",
                "Sorting_Table|K:table_long_broken|K:box_small|R:Pot2_Broken",
                "Barrel_Supplies|K:barrel_large_decorated|K:keg|K:box_stacked",
                "Recovered_Pottery|R:Pot3|R:Pot2|K:trunk_small_B" },
            new[] {
                "Stag_Guardian|R:Statue_Stag|K:coin_stack_small|R:Candles_2",
                "Golden_Armour_Altar|K:sword_shield_gold|R:Pot3|K:candle_triple",
                "Fox_Reliquary|R:Statue_Fox|K:chest_gold|K:coin_stack_medium",
                "Ritual_Vessels|R:Pot2|R:Pot1|K:candle_thin_lit",
                "Sealed_Relic_Cache|K:trunk_large_B|K:chest|R:Candles_1",
                "Carved_Column_Shrine|R:Column_Round_Short|K:candle_triple|R:Pot1_Broken",
                "Offering_Table|K:table_small_decorated_B|K:stool|K:coin_stack_large|K:candle_triple",
                "Weathered_Armour|K:sword_shield_broken|K:rubble_half|R:Pot3_Broken" }
        };

        [MenuItem("Tools/Endless Rollball/Environment/Vary Existing Dungeon Bays And Copy A Lamps %#&m")]
        static void VaryExistingDungeonBays()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use Edit Mode; this utility never runs gameplay.");
            if(PrefabStageUtility.GetCurrentPrefabStage()!=null)
                throw new InvalidOperationException("Save and close Prefab Mode first so your latest lamp edits are available on disk.");
            var labels=new[]{"TreasuryGallery","ForgottenArchives","OvergrownSanctum","ExcavationStores","GuardianReliquary"};
            var paths=labels.Select((s,i)=>DungeonPrefabFolder+"/ENV_Dungeon_Scenery_"+(char)('A'+i)+"_"+s+"_24m.prefab").ToArray();
            foreach(var path in paths)if(!File.Exists(path))throw new FileNotFoundException(path);
            var roots=new List<GameObject>();
            string backup="Artifacts/EnvironmentPrefabBackups/DungeonAsymmetry_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            foreach(var path in paths) {
                File.Copy(path,backup+"/"+Path.GetFileName(path));
                File.Copy(path+".meta",backup+"/"+Path.GetFileName(path)+".meta");
            }
            var notes=new StringBuilder("Existing dungeon prefabs revised in place. Scene is not saved or rebuilt.\nOriginal prefabs and GUID metadata backed up alongside this report.\nNo gameplay, Play Mode or performance tests run.\n\n");
            Remapped.Clear();Report.Clear();
            try {
                foreach(var path in paths)roots.Add(PrefabUtility.LoadPrefabContents(path));
                foreach(var root in roots)if(root.transform.Find("07_Individual_Bay_Revision_v02"))
                    throw new InvalidOperationException("Revision already applied; existing edits preserved.");
                var referenceLamps=roots[0].transform.Find("06_Local_Torch_Lighting");
                if(!referenceLamps)throw new InvalidOperationException("First prefab lamp reference is missing.");
                notes.AppendLine("Lamp source: "+paths[0]);
                foreach(var t in referenceLamps.GetComponentsInChildren<Transform>(true))
                    notes.AppendLine("  "+t.name+" localPosition="+t.localPosition.ToString("F4")+" localRotation="+t.localRotation.ToString("F4")+" scale="+t.localScale.ToString("F4"));
                for(int v=0;v<roots.Count;v++) {
                    var root=roots[v].transform;
                    if(v>0) {
                        var old=root.Find("06_Local_Torch_Lighting");
                        if(old)Object.DestroyImmediate(old.gameObject);
                        var copy=Object.Instantiate(referenceLamps.gameObject,root,false);
                        copy.name=referenceLamps.name;
                    }
                    var displays=root.Find("03_Themed_Side_Alcoves");
                    if(!displays)throw new InvalidOperationException("Missing alcove group: "+root.name);
                    foreach(var old in displays.Cast<Transform>().Where(t=>t.name.StartsWith("Alcove_Left_")||t.name.StartsWith("Alcove_Right_")).ToArray())
                        Object.DestroyImmediate(old.gameObject);
                    notes.AppendLine("\n"+root.name);
                    for(int i=0;i<8;i++)CreateIndividualBay(displays,v,i,notes);
                    VaryWallAndGroundDressing(root,v);
                    Group("07_Individual_Bay_Revision_v02",root);
                }
                // All five are composed before any existing prefab is saved.
                for(int i=0;i<roots.Count;i++) {
                    if(!PrefabUtility.SaveAsPrefabAsset(roots[i],paths[i]))throw new IOException("Save failed: "+paths[i]);
                }
                AssetDatabase.SaveAssets();
                notes.AppendLine("\nPrefab A lamp hierarchy retained; B-E receive exact copies including mesh transforms and light settings.\nEnclosure, structural seams, pillars and unknown user-added objects preserved.\n40 independently composed display bays; left/right and successive bays use different recipes.");
                File.WriteAllText(backup+"/RevisionReport.txt",notes.ToString());
                Debug.Log("DUNGEON_ASYMMETRY_SAVED: Five prefabs updated with 40 individual bays. A lamps copied to B-E. Backup: "+backup);
            }
            finally {foreach(var root in roots)if(root)PrefabUtility.UnloadPrefabContents(root);}
        }

        static string BaySource(string code)
        {
            return code.StartsWith("R:")?Ruins+code.Substring(2)+".fbx":Kay+"fbx(unity)/"+code.Substring(2)+".fbx";
        }

        static void CreateIndividualBay(Transform owner,int variant,int index,StringBuilder notes)
        {
            var recipe=AsymmetricBayRecipes[variant][index].Split('|');
            int side=index<4?-1:1,bay=index%4;
            float face=side<0?90:-90;
            var g=Group("Bay_"+(side<0?"Left_":"Right_")+(bay+1).ToString("00")+"_"+recipe[0],owner);
            bool table=recipe[1].Contains("table_");
            bool statue=recipe[1].Contains("Statue_");
            var stone=AssetDatabase.LoadAssetAtPath<Material>(Materials+"/MAT_AD_Shell_WarmStone.mat");
            float pedestal=statue?.45f:0;
            if(statue)Box("Guardian_Dais",g,new Vector3(side*.4f,.225f,-.2f),new Vector3(1.7f,.45f,1.55f),stone);
            var hero=Model(BaySource(recipe[1]),g,"Focal_"+recipe[1].Substring(2),
                new Vector3(side*.4f,pedestal,-.18f),new Vector3(1.8f,statue?4.7f:table?1.8f:3.7f,1.65f),face+(bay-1)*7,false);
            float top=Bounds(hero).max.y;
            for(int p=2;p<recipe.Length;p++) {
                bool tabletop=table&&p>=3;
                Vector3 position=tabletop?new Vector3(side*.4f+(p-3)*.35f,top,-.2f+(p-3)*.4f):
                    p==2?new Vector3(-side*.9f,0,-.85f):new Vector3(-side*.65f,0,1.0f);
                Model(BaySource(recipe[p]),g,(tabletop?"Tabletop_":"Companion_")+p+"_"+recipe[p].Substring(2),position,
                    tabletop?new Vector3(.4f,.7f,.4f):new Vector3(.85f,1.55f,.8f),face+23*p+11*index,false);
            }
            StreetCitySceneryExtractor.StripNonVisualComponents(g.gameObject);
            var bounds=Bounds(g.gameObject);
            float fit=Mathf.Min(1,3.15f/bounds.size.x,5.7f/bounds.size.y,3.15f/bounds.size.z);
            g.localScale*=fit;
            float z=(side<0?new[]{3.1f,8.85f,15.05f,20.75f}:new[]{3.55f,9.25f,14.75f,20.25f})[bay];
            CentreAtGround(g.gameObject,new Vector3(side*(7.65f+(bay%2)*.15f),-.05f,z));
            notes.AppendLine("  "+g.name+" | "+string.Join(", ",recipe.Skip(1))+" | "+Bounds(g.gameObject));
        }

        static void VaryWallAndGroundDressing(Transform root,int variant)
        {
            var decor=root.Find("04_Banners_And_Wall_Reliefs");
            if(decor)foreach(var t in decor.Cast<Transform>().Where(t=>t.name.StartsWith("Hanging_Banner_")).ToArray()) {
                var fields=t.name.Split('_');int side=int.Parse(fields[2]),bay=int.Parse(fields[3]);
                // Alternating sparse stretches reveal stonework instead of repeating every banner.
                if((side<0&&bay==1)||(side>0&&bay==2)) {Object.DestroyImmediate(t.gameObject);continue;}
                float shift=side<0?-.23f+.11f*bay:.31f-.14f*bay;
                t.localScale*=side<0?.88f+.025f*bay:1.02f-.035f*bay;
                var b=Bounds(t.gameObject);
                CentreAtGround(t.gameObject,new Vector3(side*(10.275f-.25f-b.extents.x),4.35f+.22f*((bay+variant+(side>0?1:0))%3),b.center.z+shift));
            }
            var scatter=root.Find("05_Small_Scenic_Dressing");
            if(scatter)foreach(var t in scatter.Cast<Transform>().Where(t=>t.name.StartsWith("Route_Side_Detail_")).ToArray()) {
                var fields=t.name.Split('_');int side=int.Parse(fields[3]),i=int.Parse(fields[4]);
                if((i+variant+(side>0?2:0))%4==0) {Object.DestroyImmediate(t.gameObject);continue;}
                t.localScale*=.72f+.07f*((i+variant+(side>0?3:0))%6);
                t.Rotate(Vector3.up,(side>0?31:-19)*(i+1),Space.World);
                var b=Bounds(t.gameObject);
                CentreAtGround(t.gameObject,new Vector3(side*(5.9f+.13f*((i+variant+(side>0?1:0))%3)),-.05f,b.center.z+(side>0?.22f:-.12f)));
            }
        }
    }
}
