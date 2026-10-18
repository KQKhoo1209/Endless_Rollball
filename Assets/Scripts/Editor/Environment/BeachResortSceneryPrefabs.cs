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
        const string BeachPrefabFolder = "Assets/EndlessRollballAssets/Environment Prefab/BeachResort";

        [MenuItem("Tools/Endless Rollball/Environment/Create Five Beach Resort Scenery Prefabs %#&g")]
        static void CreateBeachSceneryPrefabs()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode first.");
            var source = scene.GetRootGameObjects().Single(g => g.name == RootName);
            if (!source.transform.Find("Foreground_Beach") || !source.transform.Find("Background_Resort"))
                throw new InvalidOperationException("The approved foreground/background beach layout is required.");
            var labels = new[] { "VillaArrival", "GardenVillage", "IceCreamBeach", "MarinaCove", "SandcastleRetreat" };
            var names = labels.Select((s,i) => "ENV_BeachResort_Scenery_" + (char)('A'+i) + "_" + s + "_24m").ToArray();
            foreach (var name in names)
                if (File.Exists(BeachPrefabFolder + "/" + name + ".prefab"))
                    throw new InvalidOperationException("Existing beach prefabs preserved. Move or rename them before creating another set.");

            var templates = source.GetComponentsInChildren<Transform>(true)
                .Where(t => t.gameObject.activeInHierarchy)
                .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First().gameObject);
            EnsureFolder(BeachPrefabFolder);
            Remaps.Clear();
            var staging = EditorSceneManager.NewPreviewScene();
            var roots = new List<GameObject>();
            var report = new StringBuilder("Five beach-resort scenery modules, derived from the approved live 48m preview.\n" +
                "Entrance pivot (0,0,0); +Z forward; exact 24m ground seams. Scenery stays outside X +/-5.5m.\n" +
                "No centre road, centre ocean, gameplay components, colliders, cameras or lights.\n" +
                "Use alongside the authoritative gameplay track. The global ocean and skybox remain scene-level presentation.\n" +
                "Original preview and gameplay scenes are not saved or modified. No Play Mode, gameplay tests or profiling run.\n\n");
            try
            {
                for (int variant = 0; variant < names.Length; variant++)
                {
                    var root = new GameObject(names[variant]);
                    SceneManager.MoveGameObjectToScene(root, staging);
                    roots.Add(root);
                    var composer = new BeachModuleComposer(root, templates, variant);
                    composer.Compose();
                    StreetCitySceneryExtractor.StripNonVisualComponents(root);
                    report.AppendLine(names[variant]);
                    foreach (var line in composer.Placements) report.AppendLine("  " + line);
                    report.AppendLine("  Renderers: " + root.GetComponentsInChildren<Renderer>().Length);
                }
                // Compose all five before saving; an arrangement failure cannot replace an approved asset.
                foreach (var root in roots)
                {
                    string path = BeachPrefabFolder + "/" + root.name + ".prefab";
                    if (!PrefabUtility.SaveAsPrefabAsset(root, path)) throw new IOException("Could not save " + path);
                }
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Artifacts/EnvironmentExtraction");
                File.WriteAllText("Artifacts/EnvironmentExtraction/BeachResortPrefabs_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt", report.ToString());
                Selection.objects = names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(BeachPrefabFolder + "/" + n + ".prefab")).ToArray();
                EditorGUIUtility.PingObject(Selection.objects[0]);
                Debug.Log("BEACH_FIVE_PREFABS_SAVED: " + BeachPrefabFolder + ". Five distinct 24m visual-only modules; approved preview preserved. No gameplay testing run.");
            }
            finally { EditorSceneManager.ClosePreviewScene(staging); }
        }

        sealed class BeachModuleComposer
        {
            readonly Transform root, terrain, buildings, palms, lounges, vendors, castles, dressing, marina;
            readonly Dictionary<string, GameObject> templates;
            readonly List<Bounds> occupied = new List<Bounds>();
            readonly int variant;
            public readonly List<string> Placements = new List<string>();

            public BeachModuleComposer(GameObject owner, Dictionary<string, GameObject> sources, int index)
            {
                root = owner.transform; templates = sources; variant = index;
                terrain = Group("01_Sand_And_Shore_24m", root);
                var front = Group("02_Foreground_Beach", root);
                var back = Group("03_Background_Resort", root);
                buildings = Group("01_Villas_And_Garden_Cabins", back);
                castles = Group("02_Sandcastle_And_Rock_Landmarks", back);
                palms = Group("01_Palm_Gardens", front);
                lounges = Group("02_Sunshades_And_Loungers", front);
                vendors = Group("03_IceCream_And_Pavilions", front);
                dressing = Group("04_Coastal_Dressing", front);
                marina = Group("04_Marina_And_Boats", root);
            }

            public void Compose()
            {
                Ground();
                Architecture();
                // Four coherent gardens (2-3 palms and four coastal grass tufts per group).
                // The middle of each frontage is reserved for the vendor/pavilion court.
                Copy("Palm_Garden_0" + (variant % 4 + 1), palms, "Palm_Garden_Left_Entrance", -9.5f, 4, true, 0, .95f, 3);
                Copy("Palm_Garden_0" + ((variant + 2) % 4 + 1), palms, "Palm_Garden_Left_Exit", -9.5f, 20, true, 180, .95f, 3);
                Copy("Palm_Garden_0" + (5 + variant % 3), palms, "Palm_Garden_Right_Entrance", 9.2f, 4, true, 0, .8f, 3);
                Copy("Palm_Garden_0" + (5 + (variant+1) % 3), palms, "Palm_Garden_Right_Exit", 9.2f, 20, true, 180, .8f, 3);

                Copy("IceCream_Truck", vendors, "IceCream_Truck", variant == 2 ? 9 : -9, 12, true, 0, 1, 4);
                Copy(variant % 2 == 0 ? "Beach_Cafe" : "Seaside_Shade", vendors, "Beach_Pavilion", variant == 2 ? -12.5f : 12.5f, 12, true, 0, 1, 4);
                for (int i=0; i<4; i++)
                {
                    float x = i<2 ? -14 : 14;
                    float z = i%2==0 ? 5.5f : 18.5f;
                    Copy("Beach_Sunshade_" + ((i+variant)%5), lounges, "Sunshade_Court_" + (i+1).ToString("00"), x,z,true, i<2 ? 20 : -20,1,5);
                }
                for (int i=0; i<4; i++)
                    Model(Pack+"SandCastle/SandCastle.fbx", dressing, "Small_Sandcastle_"+(i+1).ToString("00"),
                        i%2==0 ? -6.8f : 6.8f, 3+i*5.7f, new Vector3(1.3f,1,1.3f), i*47+variant*19,true,4);

                // Small, individually placed shoreline details rather than a repeated full FBX scene.
                for (int side=-1; side<=1; side+=2)
                    for (int i=0; i<6; i++)
                        Model(Pirate+"grass-plant.fbx",dressing,"Coastal_Grass_"+side+"_"+i,
                            side*5.8f, 1.5f+i*4.1f, new Vector3(.3f,.3f,.3f), i*57+variant*29,true,2);
                for (int i=0; i<4; i++)
                {
                    var planter=Group("Barrel_Planter_"+i,dressing);
                    var b=Model(Pirate+"barrel.fbx",planter,"Planter_Barrel", i<2?-6.7f:6.7f, i%2==0?8:16,
                        new Vector3(.65f,.85f,.65f),i*35,true,3);
                    var bb=BoundsOf(b);
                    Place(Pirate+"grass-patch.fbx",planter,new Vector3(bb.center.x,bb.max.y-.06f,bb.center.z),i*31,new Vector3(.75f,.38f,.75f),"Planter_Greenery");
                }
                // Side-only lagoon; no visual surface spans the authoritative gameplay road/gap.
                if (variant==3) Marina();
                else
                {
                    Model(Boats+(variant==1?"boat-row-large.fbx":"boat-row-small.fbx"),marina,"Shore_Rowboat",28,7+variant*2,
                        new Vector3(2,.8f,3.5f),25+variant*15,false,2,true);
                    Model(Boats+"boat-sail-a.fbx",marina,"Lagoon_Sailboat",38,16,
                        new Vector3(3.2f,5,5.5f),-25+variant*10,false,2,true);
                }
                for(int i=0;i<4;i++)
                    Model(Boats+"buoy.fbx",marina,"Lagoon_Buoy_"+i,33.5f+(i%2)*.7f,2.5f+i*6,
                        new Vector3(.5f,.7f,.5f),variant*25,false,1,true);

                // Creation-time spatial contract only; not a gameplay test suite.
                foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    var b=renderer.bounds;
                    if(b.min.z<-.01f || b.max.z>24.01f || (b.min.x<5.49f && b.max.x>-5.49f))
                        throw new InvalidOperationException("24m scenery boundary exceeded: "+renderer.name+" "+b);
                }
            }

            void Ground()
            {
                foreach(string name in new[]{"Village_Sand_Base","Beach_Sand_Base","Beach_Shallow_Water","Wet_Sand_Shore"})
                {
                    var g=Object.Instantiate(Template(name),terrain,true);g.name=name+"_24m";
                    var scale=g.transform.localScale;scale.z*=24/BoundsOf(g).size.z;g.transform.localScale=scale;
                    var b=BoundsOf(g);g.transform.position+=Vector3.forward*(12-b.center.z);
                }
                Box("Side_Lagoon_24m",terrain,new Vector3(42,-1.45f,12),new Vector3(33,.2f,24),
                    templates["Ocean"].GetComponent<Renderer>().sharedMaterial);
                for(int i=0;i<6;i++)
                {
                    var g=Object.Instantiate(Template("Shore_Foam_00"),terrain,true);g.name="Shore_Foam_"+i.ToString("00");
                    g.transform.position=new Vector3(26.2f+Mathf.Sin(i+variant)*.2f,-1.15f,2+i*4);
                }
            }

            void Architecture()
            {
                if(variant==4)
                {
                    Model(Pack+"SandCastle/SandCastle.fbx",castles,"Grand_Sandcastle",-25,12,
                        new Vector3(10.5f,7.4f,10.5f),0,false,2);
                    Copy("Garden_Cabin_02",buildings,"Garden_Cabin_Left_Entrance",-20.5f,3,false,0,.9f,2);
                    Copy("Garden_Cabin",buildings,"Garden_Cabin_Left_Exit",-20.5f,21,false,0,.9f,2);
                }
                else
                {
                    if(variant==2)
                        Model(Pack+"Villa/Prefabs/ENV_BeachResort_Villa_B.prefab",buildings,"Resort_Villa_B",-24,6,
                            new Vector3(10,6.3f,9),90,false,2);
                    else
                        Copy(variant==1?"Garden_Cabin_02":"Resort_Villa_A",buildings,
                            "Village_Residence_Left_Entrance",variant==1?-21:-24,6,false,0,1,2);
                    Copy("Garden_Cabin",buildings,"Garden_Cabin_Left_Exit",-21,18,false,0,1,2);
                }
                Copy("Garden_Cabin_04",buildings,"Garden_Cabin_Right_Exit",20.5f,18,false,0,1,2);
                if(variant!=3)
                    Copy("Beach_Cabin",buildings,"Beach_Cabin_Right_Entrance",20.5f,5.5f,false,0,1,2);
                else
                    Model(Pirate+"tower-complete-small.fbx",buildings,"Marina_Lookout",20.5f,5.5f,
                        new Vector3(3.5f,5.5f,3.5f),-90,false,2);

                // A compact secondary rock silhouette and grass apron behind the busy foreground.
                Model(Pirate+"rocks-sand-"+(char)('a'+variant%3)+".fbx",castles,"Coastal_Rock_Landmark",23,11.5f,
                    new Vector3(3.8f,2.6f,3.6f),variant*39,false,3);
                for(int side=-1;side<=1;side+=2)
                    for(int i=0;i<3;i++)
                        Model(Pirate+"grass-patch.fbx",buildings,"Cabin_Garden_"+side+"_"+i,side*17.65f,3+i*8.5f,
                            new Vector3(.6f,.3f,.6f),i*37,false,3);
            }

            void Marina()
            {
                // One continuous dock made from touching copies of the existing imported platform.
                for(int i=0;i<4;i++)
                    Place(Pirate+"structure-platform-dock.fbx",marina,new Vector3(25+i*2.4f,-1.25f,11),90,
                        new Vector3(2.4f,1.25f,2.4f),"Jetty_"+i.ToString("00"));
                Model(Boats+"boat-speed-a.fbx",marina,"Resort_Motorboat",34,7,new Vector3(2.5f,1.5f,5),-20,false,1,true);
                Model(Boats+"boat-row-small.fbx",marina,"Moored_Rowboat",28,14,new Vector3(1.6f,.7f,3.2f),90,false,1,true);
                Model(Boats+"boat-sail-a.fbx",marina,"Marina_Sailboat",41,17,new Vector3(4,6,7),-25,false,1,true);
            }

            GameObject Template(string name)
            {
                if(!templates.TryGetValue(name,out var g))throw new InvalidOperationException("Approved preview item missing: "+name);
                return g;
            }

            GameObject Copy(string source,Transform parent,string name,float x,float z,bool front,float turn,float scale,int radius)
            {
                var g=Object.Instantiate(Template(source),parent,true);g.name=name;g.SetActive(true);
                g.transform.rotation=Quaternion.Euler(0,turn,0)*g.transform.rotation;
                g.transform.localScale*=scale;
                Arrange(g,new Vector3(x,x<0?-.2f:-.3f,z),front,radius,false);
                return g;
            }

            GameObject Model(string path,Transform parent,string name,float x,float z,Vector3 size,float yaw,bool front,int radius,bool water=false)
            {
                var g=Place(path,parent,new Vector3(x,water?-1.25f:x<0?-.2f:-.3f,z),yaw,size,name);
                Arrange(g,new Vector3(x,water?-1.25f:x<0?-.2f:-.3f,z),front,radius,water);
                return g;
            }

            void Arrange(GameObject g,Vector3 desired,bool front,int radius,bool water)
            {
                var points=new List<Vector3>{desired};
                for(int x=-radius*2;x<=radius*2;x++)for(int z=-radius*2;z<=radius*2;z++)
                    points.Add(desired+new Vector3(x*.5f,0,z*.5f));
                // A dense variant can fill the immediate requested pocket. Search the rest of
                // the same side/depth band before rejecting it, never the road or another building.
                float low=water?26:front?(desired.x<0?-16.5f:5.5f):(desired.x<0?-32.5f:17.2f);
                float high=water?57:front?(desired.x<0?-5.5f:16.5f):(desired.x<0?-17.2f:25.5f);
                for(float x=low+.5f;x<high;x+=.5f)for(float z=.5f;z<24;z+=.5f)
                    points.Add(new Vector3(x,desired.y,z));
                foreach(var point in points.OrderBy(p=>(p-desired).sqrMagnitude))
                {
                    MoveBoundsTo(g,point);var b=BoundsOf(g);
                    if(b.min.z<.3f || b.max.z>23.7f)continue;
                    if(front)
                    {
                        if(desired.x<0 && (b.min.x< -16.5f || b.max.x> -5.5f))continue;
                        if(desired.x>0 && (b.min.x<5.5f || b.max.x>16.5f))continue;
                    }
                    else if(water) {if(b.min.x<26 || b.max.x>57)continue;}
                    else
                    {
                        if(desired.x<0 && (b.min.x< -32.5f || b.max.x> -17.2f))continue;
                        if(desired.x>0 && (b.min.x<17.2f || b.max.x>25.5f))continue;
                    }
                    if(occupied.Any(o=>b.min.x<o.max.x+.2f && b.max.x>o.min.x-.2f && b.min.z<o.max.z+.2f && b.max.z>o.min.z-.2f))continue;
                    occupied.Add(b);Placements.Add(g.name+" | "+b.ToString("F2"));return;
                }
                throw new InvalidOperationException(root.name+": no clear placement for "+g.name+". Existing preview remains unchanged.");
            }
        }
    }
}
