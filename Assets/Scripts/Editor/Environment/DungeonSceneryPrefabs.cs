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
    internal static partial class DungeonResortPreviewBuilder
    {
        const string DungeonPrefabFolder="Assets/EndlessRollballAssets/Environment Prefab/Dungeon";
        const string KenneyDungeon=Source+"Modular Dungeon Kit/Models/FBX format/";

        [MenuItem("Tools/Endless Rollball/Environment/Create Five Rich Dungeon Scenery Prefabs %#&c")]
        static void CreateDungeonScenery()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode first.");
            var labels=new[]{"TreasuryGallery","ForgottenArchives","OvergrownSanctum","ExcavationStores","GuardianReliquary"};
            var names=labels.Select((s,i)=>"ENV_Dungeon_Scenery_"+(char)('A'+i)+"_"+s+"_24m").ToArray();
            foreach(var name in names)
                if(File.Exists(DungeonPrefabFolder+"/"+name+".prefab"))throw new InvalidOperationException("Existing dungeon prefabs preserved; creation will not overwrite them.");
            Folder(DungeonPrefabFolder);Folder(Materials);Remapped.Clear();Report.Clear();
            var temporary=EditorSceneManager.NewPreviewScene();
            var roots=new List<GameObject>();
            var manifest=new StringBuilder("Five separately composed 24m enclosed dungeon scenery modules.\n"+
                "Entrance pivot (0,0,0), forward +Z. Shared wall/ceiling seam dimensions.\n"+
                "Scenery only: no central floor, colliders, rigidbodies, scripts, obstacles or gameplay changes.\n"+
                "Local decorative torch lights included; scene-wide ambient and fog remain scene configuration.\n"+
                "Approved preview, including its banners, is unchanged. No Play Mode, performance or gameplay tests run.\n\n");
            try
            {
                for(int i=0;i<names.Length;i++)
                {
                    var root=new GameObject(names[i]);SceneManager.MoveGameObjectToScene(root,temporary);roots.Add(root);
                    var set=new DungeonModuleSet(root.transform,i);set.Compose();
                    StreetCitySceneryExtractor.StripNonVisualComponents(root);
                    set.AddLocalLights();
                    manifest.AppendLine(names[i]+" | "+root.GetComponentsInChildren<Renderer>().Length+" renderers | "+set.Models.Count+" unique source models");
                    foreach(var asset in set.Models.OrderBy(s=>s))manifest.AppendLine("  Source: "+asset);
                    foreach(var note in set.Notes)manifest.AppendLine("  "+note);
                }
                foreach(var root in roots)
                    if(!PrefabUtility.SaveAsPrefabAsset(root,DungeonPrefabFolder+"/"+root.name+".prefab"))
                        throw new IOException("Could not save "+root.name);
                AssetDatabase.SaveAssets();Directory.CreateDirectory("Artifacts/EnvironmentExtraction");
                File.WriteAllText("Artifacts/EnvironmentExtraction/DungeonPrefabs_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt",manifest.ToString());
                Selection.objects=names.Select(n=>AssetDatabase.LoadAssetAtPath<GameObject>(DungeonPrefabFolder+"/"+n+".prefab")).ToArray();
                EditorGUIUtility.PingObject(Selection.objects[0]);
                Debug.Log("DUNGEON_FIVE_PREFABS_SAVED: "+DungeonPrefabFolder+". Five varied 24m scenery modules. Banner bounds sit clear of wall faces; original preview untouched.");
            }
            finally {EditorSceneManager.ClosePreviewScene(temporary);}
        }

        sealed class DungeonModuleSet
        {
            readonly Transform root,shell,structure,displays,wallDecor,scatter,lighting;
            readonly int variant;
            readonly List<Vector3> lamps=new List<Vector3>();
            public readonly HashSet<string> Models=new HashSet<string>();
            public readonly List<string> Notes=new List<string>();
            Material stone;
            public DungeonModuleSet(Transform owner,int index)
            {
                root=owner;variant=index;
                shell=Group("01_Enclosure_And_Side_Platforms_24m",root);
                structure=Group("02_Arches_And_Columns",root);
                displays=Group("03_Themed_Side_Alcoves",root);
                wallDecor=Group("04_Banners_And_Wall_Reliefs",root);
                scatter=Group("05_Small_Scenic_Dressing",root);
                lighting=Group("06_Local_Torch_Lighting",root);
            }

            public void Compose()
            {
                stone=Solid("Shell_WarmStone",new Color(.28f,.26f,.22f));
                Enclosure();
                for(int side=-1;side<=1;side+=2)
                {
                    for(int bay=0;bay<4;bay++)
                    {
                        var g=Group("Alcove_"+(side<0?"Left_":"Right_")+(bay+1).ToString("00"),displays);
                        BuildBay(g,side,bay);
                        FitBay(g.gameObject,new Vector3(side*7.65f,-.05f,new[]{3.35f,9f,15f,20.65f}[bay]));
                    }
                    for(int bay=0;bay<4;bay++)Banner(side,bay);
                    for(int i=0;i<8;i++)
                    {
                        string path=variant==2?Ruins+(i%2==0?"Grass.fbx":"Bush_Round.fbx"):
                            i%3==0?Ruins+"Pot1_Broken.fbx":Ruins+"Bricks.fbx";
                        var small=M(path,scatter,"Route_Side_Detail_"+side+"_"+i,
                            new Vector3(side*5.85f,-.05f,1.8f+i*2.85f),new Vector3(.35f,.45f,.4f),i*47);
                        var b=Bounds(small);
                        if(b.min.x<5.5f && b.max.x>-5.5f)throw new InvalidOperationException("Route detail entered clearance.");
                    }
                }
                // All geometry fits inside this 24m interval; overhead pieces are allowed above the route.
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    var b=renderer.bounds;
                    if(b.min.z<-.035f || b.max.z>24.035f)throw new InvalidOperationException("Module seam overhang: "+renderer.name+" "+b);
                }
                Notes.Add("Central 9m track and its 1m side clearance contain no floor/prop geometry; only overhead structure crosses above.");
            }

            void Enclosure()
            {
                for(int side=-1;side<=1;side+=2)
                {
                    Box("Wall_Backing_"+side,shell,new Vector3(side*11.1f,5.6f,12),new Vector3(.5f,11.6f,24),stone);
                    Box("Side_Foundation_"+side,shell,new Vector3(side*8.2f,-.35f,12),new Vector3(5.4f,.55f,24),stone);
                    for(int bay=0;bay<6;bay++)
                    {
                        string wall=variant==1?KayModel("wall_cracked"):variant==3?KayModel("wall_scaffold"):
                            Ruins+(variant==2?(bay%2==0?"Wall_Overgrown.fbx":"Wall_ArchRound_Overgrown.fbx"):
                            variant==4&&bay%2==1?"Wall_ArchGothic.fbx":"Wall.fbx");
                        M(wall,shell,"Lower_Wall_"+side+"_"+bay,new Vector3(side*10.65f,-.05f,bay*4+2),new Vector3(.75f,5.7f,4),side<0?90:-90,true);
                        M(Ruins+"Wall.fbx",shell,"Upper_Wall_"+side+"_"+bay,new Vector3(side*10.65f,5.6f,bay*4+2),new Vector3(.75f,5.6f,4),side<0?90:-90,true);
                        M(variant==3?KayModel("floor_wood_large_dark"):variant==2?KayModel("floor_tile_small_weeds_A"):Ruins+"Floor_Squares.fbx",
                            shell,"Alcove_Paving_"+side+"_"+bay,new Vector3(side*8.2f,-.15f,bay*4+2),new Vector3(5.4f,.1f,4),0,true);
                    }
                    for(int i=0;i<2;i++)
                    {
                        string pillar=variant==1||variant==4?KayModel("pillar_decorated"):Ruins+(variant==3?"Column_Square.fbx":"Column_Round.fbx");
                        M(pillar,structure,"Hall_Pillar_"+side+"_"+i,new Vector3(side*9.4f,-.05f,6+i*12),new Vector3(1.25f,9,1.25f),0,true);
                        var torch=M(KayModel(variant==4?"torch_mounted":"torch_lit"),lighting,"Torch_"+side+"_"+i,
                            new Vector3(side*7.9f,2.5f,6+i*12),new Vector3(.7f,1.65f,.7f),side<0?90:-90);
                        var tb=Bounds(torch);lamps.Add(new Vector3(tb.center.x,tb.max.y-.1f,tb.center.z));
                    }
                }
                Box("Ceiling_Backing",shell,new Vector3(0,11.5f,12),new Vector3(22.7f,.6f,24),stone);
                for(int z=0;z<6;z++)for(int x=-2;x<=2;x++)
                    M(KayModel("ceiling_tile"),shell,"Ceiling_Coffer_"+x+"_"+z,new Vector3(x*4.4f,10.95f,z*4+2),new Vector3(4.4f,.35f,4),0,true);
                for(int i=0;i<3;i++)
                    M(Ruins+(variant==4&&i==1?"Arch_Gothic_RoundColumn.fbx":variant==2&&i==1?"Arch_Round.fbx":"Arch_Round_RoundColumn.fbx"),structure,
                        "Processional_Arch_"+i,new Vector3(0,-.05f,new[]{.65f,12,23.35f}[i]),new Vector3(19,10.7f,1.1f),0,true);
            }

            void BuildBay(Transform g,int side,int bay)
            {
                float face=side<0?90:-90;
                if(variant==0) Treasury(g,side,bay,face);
                else if(variant==1) Archives(g,side,bay,face);
                else if(variant==2) Overgrown(g,side,bay,face);
                else if(variant==3) Stores(g,side,bay,face);
                else Reliquary(g,side,bay,face);
            }

            void Treasury(Transform g,int side,int bay,float face)
            {
                if(bay%2==0)
                {
                    Box("Guardian_Platform",g,new Vector3(0,.25f,0),new Vector3(2.4f,.5f,2.2f),stone);
                    M(Ruins+(side<0?"Statue_Fox.fbx":"Statue_Stag.fbx"),g,"Treasure_Guardian",new Vector3(0,.5f,0),new Vector3(2.2f,5.1f,2.1f),face);
                    M(KayModel("coin_stack_medium"),g,"Guardian_Offering",new Vector3(-.9f,0,1.1f),new Vector3(.65f,.55f,.65f),40);
                }
                else
                {
                    M(KayModel("chest_gold"),g,"Open_Treasure_Chest",new Vector3(0,0,-.2f),new Vector3(2,1.8f,1.5f),face);
                    M(KayModel("chest"),g,"Sealed_Treasure_Chest",new Vector3(.8f,0,.9f),new Vector3(1.1f,.9f,1),face+20);
                    M(KayModel("coin_stack_large"),g,"Gold_Hoard",new Vector3(-.8f,0,.9f),new Vector3(.9f,.65f,.9f),20);
                    M(KayModel("key"),g,"Vault_Key",new Vector3(-.8f,.65f,.9f),new Vector3(.4f,.15f,.5f),70);
                }
                M(Ruins+"Pot3.fbx",g,"Ceremonial_Vessel",new Vector3(.9f,0,-1),new Vector3(.8f,1.5f,.8f),bay*53);
                M(Ruins+"Candles_2.fbx",g,"Treasure_Candles",new Vector3(-.9f,0,-1),new Vector3(.65f,.9f,.65f),0);
            }

            void Archives(Transform g,int side,int bay,float face)
            {
                M(bay%2==0?Ruins+"Bookcase_Full.fbx":KayModel("shelves"),g,"Archive_Shelves",new Vector3(side*.7f,0,0),new Vector3(1,3.6f,2.4f),face);
                var table=M(KayModel(bay%2==0?"table_small_decorated_A":"table_small_decorated_B"),g,"Relic_Study_Table",
                    new Vector3(-side*.65f,0,-.5f),new Vector3(1.3f,1.35f,1.25f),face);
                float top=Bounds(table).max.y;
                M(KayModel("bottle_A_labeled_green"),g,"Alchemical_Bottle",new Vector3(-side*.8f,top,.0f),new Vector3(.3f,.65f,.3f),0);
                M(KayModel("candle_triple"),g,"Archive_Candelabra",new Vector3(-side*.4f,top,-.7f),new Vector3(.45f,.9f,.45f),0);
                M(KayModel("chair"),g,"Archivist_Chair",new Vector3(-side*.9f,0,1),new Vector3(.75f,1.2f,.75f),face+180);
                M(KayModel("trunk_small_B"),g,"Relic_Document_Trunk",new Vector3(side*.6f,0,1.05f),new Vector3(.8f,.8f,.9f),face);
                M(KayModel("bottle_C_brown"),g,"Sealed_Artifact",new Vector3(side*.6f,.8f,1.05f),new Vector3(.25f,.45f,.25f),0);
            }

            void Overgrown(Transform g,int side,int bay,float face)
            {
                M(Ruins+(bay%2==0?"Column_Round_Short.fbx":"Support_Tall.fbx"),g,"Weathered_Stone_Monument",new Vector3(side*.6f,0,-.2f),new Vector3(1.5f,4.8f,1.5f),face);
                M(Ruins+(bay%2==0?"Bush_Large.fbx":"Bush_2x1.fbx"),g,"Dense_Ancient_Overgrowth",new Vector3(side*.45f,0,.8f),new Vector3(1.6f,1.4f,1.4f),bay*37);
                M(Ruins+"Pot2_Broken.fbx",g,"Broken_Ancient_Pottery",new Vector3(-side*.9f,0,.65f),new Vector3(.9f,1.3f,.9f),40);
                M(KayModel("rubble_large"),g,"Fallen_Masonry",new Vector3(-side*.55f,0,-.85f),new Vector3(1.6f,.8f,1.5f),bay*67);
                M(Ruins+"Grass.fbx",g,"Grass_In_Stone_Cracks",new Vector3(-side*.75f,0,1.3f),new Vector3(.9f,.7f,.7f),15);
                M(Ruins+"Chest.fbx",g,"Forgotten_Relic_Chest",new Vector3(side*.6f,0,-1.15f),new Vector3(.9f,.9f,.8f),face);
                if(bay==2)M(Ruins+"DeadTree_1.fbx",g,"Underground_Root_Silhouette",new Vector3(side*.5f,0,0),new Vector3(2,5,2),face);
            }

            void Stores(Transform g,int side,int bay,float face)
            {
                if(bay%2==0)
                {
                    M(Ruins+"Cart.fbx",g,"Excavation_Cart",Vector3.zero,new Vector3(2.1f,2.2f,2.5f),face);
                    M(KayModel("crates_stacked"),g,"Stacked_Excavation_Crates",new Vector3(side*.7f,0,-1),new Vector3(1.2f,2.3f,1.2f),face);
                }
                else
                {
                    M(KayModel("barrel_large_decorated"),g,"Large_Storage_Barrel",new Vector3(side*.7f,0,0),new Vector3(1.1f,2.1f,1.1f),bay*45);
                    M(KayModel("barrel_small_stack"),g,"Small_Barrel_Stack",new Vector3(-side*.6f,0,-.7f),new Vector3(1.3f,1.7f,1.2f),face);
                    M(KayModel("keg_decorated"),g,"Ancient_Store_Keg",new Vector3(-side*.6f,0,.8f),new Vector3(1,1.3f,1),face);
                }
                M(KayModel("trunk_medium_C"),g,"Excavated_Relic_Trunk",new Vector3(side*.55f,0,1.1f),new Vector3(.95f,1,.9f),face);
                M(KayModel("box_small_decorated"),g,"Marked_Supply_Box",new Vector3(-side*.85f,0,1.1f),new Vector3(.7f,.7f,.7f),face+20);
                M(Ruins+"Brick.fbx",g,"Recovered_Stone_Block",new Vector3(-side*.85f,.7f,1.1f),new Vector3(.4f,.3f,.4f),10);
            }

            void Reliquary(Transform g,int side,int bay,float face)
            {
                M(KayModel("pillar_decorated"),g,"Carved_Relic_Pedestal",new Vector3(side*.55f,0,-.25f),new Vector3(1.4f,2.1f,1.4f),0);
                if(bay%2==0)
                    M(Ruins+(side<0?"Statue_Stag.fbx":"Statue_Fox.fbx"),g,"Reliquary_Guardian",new Vector3(side*.55f,2.1f,-.25f),new Vector3(1.8f,3.4f,1.7f),face);
                else
                    M(KayModel("sword_shield_gold"),g,"Ceremonial_Relic",new Vector3(side*.55f,2.1f,-.25f),new Vector3(1.5f,2.6f,1.2f),face);
                var table=M(KayModel("table_small"),g,"Offering_Table",new Vector3(-side*.7f,0,.9f),new Vector3(1.1f,1.2f,1),face);
                M(KayModel("candle_triple"),g,"Offering_Candelabra",new Vector3(-side*.7f,Bounds(table).max.y,.9f),new Vector3(.65f,1.1f,.65f),0);
                M(KayModel("coin_stack_large"),g,"Relic_Offering_Coins",new Vector3(-side*.8f,0,-.75f),new Vector3(.8f,.65f,.8f),0);
                M(Ruins+"Pot1.fbx",g,"Gold_Hall_Urn",new Vector3(side*.75f,0,1.1f),new Vector3(.8f,1.6f,.8f),bay*37);
            }

            void Banner(int side,int bay)
            {
                // Backing walls have an inner face at |X| = 10.275m. Position by full
                // renderer thickness, never the FBX pivot, leaving a visible 25cm air gap.
                if(variant==2 && bay%2==1)
                {
                    M(Ruins+"Window_Bars_Overgrown.fbx",wallDecor,"Overgrown_Wall_Relief_"+side+"_"+bay,
                        new Vector3(side*9.8f,4.7f,new[]{3.35f,9f,15f,20.65f}[bay]),new Vector3(.3f,2.2f,2),side<0?90:-90);
                    return;
                }
                string banner=variant==0?"banner_patternC_yellow":variant==1?"banner_thin_blue":variant==3?"banner_brown":variant==4?"banner_shield_red":"banner_green";
                var g=M(KayModel(banner),wallDecor,"Hanging_Banner_"+side+"_"+bay,new Vector3(0,4.6f,0),new Vector3(.55f,3.5f,2.1f),side<0?90:-90);
                var b=Bounds(g);float x=side*(10.275f-.25f-b.extents.x);
                CentreAtGround(g,new Vector3(x,4.6f,new[]{3.35f,9f,15f,20.65f}[bay]));
                b=Bounds(g);
                if((side>0?b.max.x:-b.min.x)>10.026f)throw new InvalidOperationException("Banner wall clearance failed.");
                Notes.Add(g.name+" wall clearance >= 0.25m");
                if((variant==1||variant==4)&&bay%2==1)
                    M(KenneyDungeon+"gate-metal-bars.fbx",wallDecor,"Sealed_Side_Relic_Niche_"+side+"_"+bay,
                        new Vector3(side*9.95f,.05f,new[]{3.35f,9f,15f,20.65f}[bay]),new Vector3(.25f,3.6f,2.4f),side<0?90:-90);
            }

            GameObject M(string path,Transform parent,string name,Vector3 ground,Vector3 size,float yaw,bool stretch=false)
            {
                Models.Add(path);var g=Model(path,parent,name,ground,size,yaw,stretch);
                if(path.StartsWith(KenneyDungeon))
                {
                    string materialPath=Materials+"/MAT_AD_Kenney_DungeonAtlas.mat";
                    var atlas=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if(!atlas)
                    {
                        atlas=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="MAT_AD_Kenney_DungeonAtlas",enableInstancing=true};
                        atlas.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(KenneyDungeon+"Textures/colormap.png"));
                        atlas.SetColor("_BaseColor",Color.white);atlas.SetFloat("_Smoothness",.12f);atlas.SetFloat("_Cull",0);
                        AssetDatabase.CreateAsset(atlas,materialPath);
                    }
                    foreach(var r in g.GetComponentsInChildren<Renderer>())r.sharedMaterials=r.sharedMaterials.Select(m=>atlas).ToArray();
                }
                return g;
            }
            static string KayModel(string name) {return Kay+"fbx(unity)/"+name+".fbx";}
            void FitBay(GameObject g,Vector3 centre)
            {
                var b=Bounds(g);float scale=Mathf.Min(1,3.25f/b.size.x,5.7f/b.size.y,3.4f/b.size.z);
                g.transform.localScale*=scale;CentreAtGround(g,centre);b=Bounds(g);
                if(b.min.x<5.5f && b.max.x>-5.5f)throw new InvalidOperationException("Alcove entered the route clearance.");
            }
            public void AddLocalLights()
            {
                for(int i=0;i<lamps.Count;i++)Point("Torch_Amber_"+i,lighting,lamps[i],new Color(1,.58f,.22f),18,10,false);
                Point("Soft_Cool_Hall_Fill",lighting,new Vector3(0,7.8f,12),new Color(.52f,.65f,.82f),10,17,false);
            }
        }
    }
}
