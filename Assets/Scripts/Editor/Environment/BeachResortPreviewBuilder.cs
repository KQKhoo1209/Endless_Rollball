using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // Editor-only composition. This does not participate in gameplay track generation.
    internal static partial class BeachResortPreviewBuilder
    {
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string RootName = "WaterPreview_BeachResort_48m";
        const string Pack = "Assets/EndlessRollballAssets/Waterpark Building/";
        const string Materials = "Assets/EndlessRollballAssets/Materials/Environment/BeachResortPreview";
        const string Pirate = Pack + "kenney_pirate-kit/Models/";
        const string Boats = Pack + "kenney_watercraft-pack/Models/";
        static readonly Dictionary<Material, Material> Remaps = new Dictionary<Material, Material>();

        [MenuItem("Tools/Endless Rollball/Environment/Build Beach Resort Preview 48m %#&w")]
        static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode first.");
            if (scene.GetRootGameObjects().Any(g => g.name == RootName))
                throw new InvalidOperationException("Beach preview already exists; existing edits preserved.");
            var backup = "Artifacts/EnvironmentPreviewBackups/BeachResort_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview.unity");
            // Includes unsaved edits without replacing the user's saved scene.
            EditorSceneManager.SaveScene(scene, backup + "/EnvironmentPreview_LiveBefore.unity", true);
            var previous = scene.GetRootGameObjects();
            var state = new System.Text.StringBuilder("Previous presentation state:\n");
            foreach (var g in previous)
            {
                state.AppendLine(g.name + " active=" + g.activeSelf + " pos=" + g.transform.position + " rot=" + g.transform.eulerAngles);
                if (g.GetComponent<Camera>()) state.AppendLine(EditorJsonUtility.ToJson(g.GetComponent<Camera>(), true));
                if (g.GetComponent<Light>()) state.AppendLine(EditorJsonUtility.ToJson(g.GetComponent<Light>(), true));
            }
            state.AppendLine("Skybox=" + AssetDatabase.GetAssetPath(RenderSettings.skybox));
            File.WriteAllText(backup + "/PresentationBefore.txt", state.ToString());
            EnsureFolder(Materials);
            Remaps.Clear();
            GameObject root = null;
            var hidden = new List<GameObject>();
            try
            {
                root = new GameObject(RootName);
                var terrain = Group("01_Beach_And_Sea", root.transform);
                var boardwalk = Group("02_Wooden_Promenade", root.transform);
                var village = Group("03_Resort_Village", root.transform);
                var vendors = Group("04_IceCream_And_Beach_Pavilions", root.transform);
                var nature = Group("05_Palms_And_Coastal_Nature", root.transform);
                var marina = Group("06_Jetty_And_Boats", root.transform);
                var details = Group("07_Beach_Dressing", root.transform);
                var lighting = Group("08_Preview_Lighting", root.transform);
                var sand = Mat("Sand_Warm", "#EAD39B", .08f);
                var sandWet = Mat("Sand_Wet", "#C8BB8F", .3f);
                var water = Mat("Sea_Turquoise", "#159FBC", .72f);
                var shallows = Mat("Sea_Shallow", "#44C7CF", .65f);
                var foam = Mat("Sea_Foam", "#D1F3E7", .15f);
                var wood = Mat("Deck_WarmWood", "#A9683E", .2f);
                var trim = Mat("Deck_Trim", "#784A31", .22f);
                var yellow = Mat("Route_Gold", "#F4C542", .25f);
                var cream = Mat("Pavilion_Cream", "#F2EBD8", .15f);
                var coral = Mat("Pavilion_Coral", "#E97962", .2f);
                var teal = Mat("Pavilion_Teal", "#27A9AF", .2f);

                Box("Ocean", terrain, new Vector3(0,-1.45f,46), new Vector3(200,.2f,220),water);
                Box("Village_Sand_Base", terrain,new Vector3(-19,-.75f,24),new Vector3(27,1.1f,78),sand);
                Box("Beach_Sand_Base", terrain,new Vector3(15.5f,-.85f,24),new Vector3(20,1.1f,78),sand);
                Box("Beach_Shallow_Water",terrain,new Vector3(28,-1.24f,24),new Vector3(7,.12f,78),shallows);
                Box("Wet_Sand_Shore",terrain,new Vector3(25,-.93f,24),new Vector3(2,.08f,78),sandWet);
                // Scalloped quiet surf accents; nothing is placed across the playable route.
                for(int i=0;i<18;i++)
                {
                    float z=-10+i*4;
                    var surf=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    surf.name="Shore_Foam_"+i.ToString("00"); surf.transform.SetParent(terrain,false);
                    surf.transform.localPosition=new Vector3(26.2f+Mathf.Sin(i)*.3f,-1.15f,z);
                    surf.transform.localScale=new Vector3(.45f,.04f,2.5f);
                    Object.DestroyImmediate(surf.GetComponent<Collider>());
                    surf.GetComponent<Renderer>().sharedMaterial=foam;
                }
                Box("Deck_Visual_Foundation_48m",boardwalk,new Vector3(0,-.35f,24),new Vector3(9,.5f,48),wood);
                for(int row=0;row<24;row++) for(int lane=0;lane<3;lane++)
                {
                    var ground=new Vector3((lane-1)*3,-.1f,row*2+1);
                    var planks=Place(Pirate+"platform-planks.fbx",boardwalk,ground,0,new Vector3(3,.16f,2),"Boardwalk_Planks_"+row.ToString("00")+"_"+lane);
                    var b=BoundsOf(planks);
                    planks.transform.localScale=Vector3.Scale(planks.transform.localScale,new Vector3(3/b.size.x,.16f/b.size.y,2/b.size.z));
                    b=BoundsOf(planks);planks.transform.position+=ground-new Vector3(b.center.x,b.min.y,b.center.z);
                }
                foreach(int side in new[]{-1,1})
                {
                    Box("Wood_Edge_"+side,boardwalk,new Vector3(side*4.57f,-.12f,24),new Vector3(.14f,.26f,48),trim);
                    Box("Yellow_Guidance_"+side,boardwalk,new Vector3(side*4.32f,.065f,24),new Vector3(.09f,.018f,48),yellow);
                    for(int i=0;i<9;i++) Box("Deck_Pile_"+side+"_"+i,boardwalk,new Vector3(side*4.45f,-.8f,i*6),new Vector3(.24f,1.4f,.24f),trim);
                }

                Place(Pack+"Villa/Prefabs/ENV_BeachResort_Villa_A.prefab",village,new Vector3(-12.5f,-.2f,11),90,new Vector3(10,6,9),"Resort_Villa_A");
                Place(Pack+"Villa/Prefabs/ENV_BeachResort_Villa_B.prefab",village,new Vector3(-12.5f,-.2f,35),90,new Vector3(10,6.3f,9),"Resort_Villa_B");
                Place(Pack+"WoodenHouse/cabin1.fbx",village,new Vector3(-21,-.2f,23),90,new Vector3(8,5.2f,8),"Garden_Cabin");
                Place(Pack+"WoodenHouse/cabin1.fbx",village,new Vector3(13,-.3f,40),-90,new Vector3(7,4.6f,7),"Beach_Cabin");
                foreach(float z in new[]{11f,35f})
                    Box("Villa_Approach_"+z,boardwalk,new Vector3(-6.3f,-.12f,z),new Vector3(3.6f,.2f,2),wood);

                // Retain the imported truck's finished material; add no obstacle behaviour.
                var truck=Place(Pack+"IceCreamTruck/IceTruck.fbx",vendors,new Vector3(-8.1f,-.1f,23),0,new Vector3(4.8f,3.6f,5.5f),"IceCream_Truck");
                Box("IceCream_Stop_Deck",vendors,new Vector3(-8,-.2f,23),new Vector3(6.2f,.2f,7),wood);
                Pavilion(vendors, new Vector3(9,-.3f,8),"Beach_Cafe",coral,cream,wood);
                Pavilion(vendors, new Vector3(10,-.3f,26),"Seaside_Shade",teal,cream,wood);
                Sign(vendors,new Vector3(-5.9f,1.35f,20),"ICE CREAM",teal,cream,180);
                Sign(vendors,new Vector3(-6.7f,1.4f,4),"PALM COVE",coral,cream,180);

                for(int side=-1;side<=1;side+=2) for(int i=0;i<7;i++)
                {
                    float z=2+i*7.2f+(side>0?2:0);
                    float x=side*(8.4f+(i%3)*.55f);
                    Place(Pirate+(i%2==0?"palm-detailed-bend.fbx":"palm-detailed-straight.fbx"),nature,new Vector3(x,side<0?-.2f:-.3f,z),i*71+side*30,new Vector3(5.4f,6.2f+(i%3)*.6f,5.4f),"Promenade_Palm_"+side+"_"+i);
                }
                for(int i=0;i<9;i++)
                    Place(Pirate+(i%2==0?"palm-bend.fbx":"palm-straight.fbx"),nature,new Vector3(i%2==0?-25:20,-.3f,1+i*6),i*51,new Vector3(5,7,5),"Beach_Grove_"+i);
                for(int side=-1;side<=1;side+=2) for(int i=0;i<16;i++)
                {
                    float z=1+i*3; float x=side*(6.5f+(i%3)*.8f);
                    Place(Pirate+"grass-plant.fbx",nature,new Vector3(x,-.2f,z),i*33,new Vector3(1.2f,.8f,1.2f),"Coastal_Grass_"+side+"_"+i);
                }
                for(int i=0;i<9;i++)
                    Place(Pirate+"rocks-sand-"+(char)('a'+i%3)+".fbx",nature,new Vector3(i%2==0?22:-24,-.3f,-3+i*7),i*41,new Vector3(3,1.3f,2.5f),"Sandstone_Cluster_"+i);

                for(int i=0;i<5;i++)
                    Place(Pirate+"structure-platform-dock.fbx",marina,new Vector3(17+i*3.2f,-1.25f,17),90,new Vector3(3.2f,1.25f,3),"Jetty_"+i);
                Place(Boats+"boat-sail-a.fbx",marina,new Vector3(35,-1.28f,21),-25,new Vector3(4,6,7),"Lagoon_Sailboat");
                Place(Boats+"boat-row-small.fbx",marina,new Vector3(26,-1.1f,12),30,new Vector3(1.6f,.7f,3.2f),"Beach_Rowboat");
                Place(Boats+"boat-speed-a.fbx",marina,new Vector3(33,-1.2f,32),-15,new Vector3(2.5f,1.5f,5),"Resort_Motorboat");
                for(int i=0;i<6;i++) Place(Boats+"buoy.fbx",marina,new Vector3(32,-1.3f,2+i*7),0,new Vector3(.6f,.8f,.6f),"Coastal_Buoy_"+i);
                Place(Pack+"SandCastle/SandCastle.fbx",details,new Vector3(17,-.3f,33),-20,new Vector3(2,1.6f,2),"Beach_Sandcastle");
                for(int i=0;i<5;i++)
                {
                    Place(Pirate+"barrel.fbx",details,new Vector3(-6.3f,-.15f,7+i*9),i*32,new Vector3(.65f,.85f,.65f),"Resort_Barrel_Planter_"+i);
                    Place(Pirate+"grass-patch.fbx",nature,new Vector3(-6.3f,.68f,7+i*9),i*32,new Vector3(.8f,.45f,.8f),"Planter_Greenery_"+i);
                }
                Place(Pirate+"tower-complete-small.fbx",details,new Vector3(18,-.3f,4),-90,new Vector3(3.5f,5.5f,3.5f),"Beach_Lookout_Hut");
                // Simple original sunshades provide colour without requiring another asset pack.
                for(int i=0;i<5;i++) Umbrella(details,new Vector3(15+(i%2)*5,-.3f,5+i*9),i%2==0?coral:teal,cream,wood,i);

                foreach(var old in previous)
                    if(old.activeSelf && !old.GetComponent<Camera>() && !old.GetComponent<Light>() &&
                       !old.name.Contains("EventSystem")) { hidden.Add(old); old.SetActive(false); }
                var sunObject=new GameObject("Beach_Preview_Sun"); sunObject.transform.SetParent(lighting,false);
                sunObject.transform.rotation=Quaternion.Euler(48,-32,0);
                var sun=sunObject.AddComponent<Light>(); sun.type=LightType.Directional; sun.intensity=1.8f;
                sun.color=new Color(1,.95f,.84f); sun.shadows=LightShadows.Soft;
                foreach(var old in previous) if(old.GetComponent<Light>() && old.activeSelf) { hidden.Add(old);old.SetActive(false); }
                RenderSettings.sun=sun;
                RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/EndlessRollballAssets/Skybox/MAT_ENV_WP_Skybox_v02.mat");
                RenderSettings.ambientMode=AmbientMode.Trilight;
                RenderSettings.ambientSkyColor=new Color(.65f,.82f,.95f);
                RenderSettings.ambientEquatorColor=new Color(.65f,.75f,.72f);
                RenderSettings.ambientGroundColor=new Color(.43f,.40f,.31f);
                RenderSettings.fog=true; RenderSettings.fogMode=FogMode.Linear;
                RenderSettings.fogColor=new Color(.60f,.86f,.89f);RenderSettings.fogStartDistance=60;RenderSettings.fogEndDistance=160;
                var camera=previous.Select(g=>g.GetComponent<Camera>()).FirstOrDefault(c=>c);
                if(camera)
                {
                    camera.transform.position=new Vector3(0,4,-8);
                    camera.transform.rotation=Quaternion.Euler(12,0,0);
                    camera.fieldOfView=62;camera.nearClipPlane=.1f;camera.farClipPlane=300;
                    camera.clearFlags=CameraClearFlags.Skybox;
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene,ScenePath);
                File.WriteAllText(backup+"/Created.txt","Created "+RootName+" in "+ScenePath+"\nImported models only from "+Pack+"\nNew simple preview surfaces and materials; no gameplay or tests.\nPrevious scene roots preserved inactive: "+string.Join(", ",hidden.Select(g=>g.name)));
                Selection.activeGameObject=root;
                if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(new Vector3(0,2,20),Quaternion.Euler(26,-25,0),43);
                Debug.Log("BEACH_RESORT_PREVIEW_SAVED: 48m beach boardwalk, villas, cabins, ice cream truck, palms, beach dressing and marina. Backup: "+backup);
            }
            catch
            {
                foreach(var old in hidden) if(old) old.SetActive(true);
                if(root) Object.DestroyImmediate(root);
                throw;
            }
        }

        static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path)) return;
            var parent=Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static Transform Group(string name,Transform parent)
        {
            var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;
        }
        static Material Mat(string name,string hex,float smoothness)
        {
            string path=Materials+"/MAT_WP_"+name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path); if(existing)return existing;
            ColorUtility.TryParseHtmlString(hex,out var color);
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="MAT_WP_"+name,enableInstancing=true};
            mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",smoothness);mat.SetFloat("_Metallic",0);
            AssetDatabase.CreateAsset(mat,path);return mat;
        }
        static void Box(string name,Transform parent,Vector3 pos,Vector3 size,Material mat)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);
            g.transform.localPosition=pos;g.transform.localScale=size;
            Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=mat;
        }
        static Bounds BoundsOf(GameObject g)
        {
            var rr=g.GetComponentsInChildren<Renderer>(true);if(rr.Length==0)throw new InvalidOperationException("No geometry: "+g.name);
            var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);return b;
        }
        static GameObject Place(string path,Transform parent,Vector3 ground,float yaw,Vector3 limit,string name)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!source)throw new FileNotFoundException("Required imported model missing",path);
            var g=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);g.name=name;
            g.transform.localPosition=Vector3.zero;g.transform.localRotation=Quaternion.Euler(0,yaw,0);
            var b=BoundsOf(g);
            float scale=Mathf.Min(limit.x/Mathf.Max(.0001f,b.size.x),limit.y/Mathf.Max(.0001f,b.size.y),limit.z/Mathf.Max(.0001f,b.size.z));
            g.transform.localScale*=scale;b=BoundsOf(g);
            g.transform.position+=ground-new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var collider in g.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            foreach(var renderer in g.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>ConvertMaterial(m,path)).ToArray();
            }
            return g;
        }
        static Material ConvertMaterial(Material source,string asset)
        {
            if(!source)return Mat("Fallback_Cream","#E4D8BB",.15f);
            if(source.shader && source.shader.name.StartsWith("Universal Render Pipeline/"))return source;
            if(Remaps.TryGetValue(source,out var mapped))return mapped;
            string guid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            string safe=string.Concat(source.name.Select(c=>char.IsLetterOrDigit(c)?c:'_'));
            var path=Materials+"/MAT_WP_Imported_"+safe+"_"+guid.Substring(0,Mathf.Min(8,guid.Length))+".mat";
            mapped=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mapped)
            {
                mapped=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=Path.GetFileNameWithoutExtension(path),enableInstancing=true};
                var texture=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.mainTexture;
                if(!texture && asset.Contains("kenney_"))
                    texture=AssetDatabase.LoadAssetAtPath<Texture2D>(asset.Substring(0,asset.IndexOf("/Models/",StringComparison.Ordinal))+"/Models/Textures/colormap.png");
                mapped.SetTexture("_BaseMap",texture);
                mapped.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.color:Color.white);
                mapped.SetFloat("_Smoothness",.2f);mapped.SetFloat("_Metallic",0);mapped.SetFloat("_Cull",0);
                AssetDatabase.CreateAsset(mapped,path);
            }
            Remaps[source]=mapped;return mapped;
        }
        static void Pavilion(Transform parent,Vector3 p,string name,Material roof,Material cream,Material wood)
        {
            var group=Group(name,parent);
            Box(name+"_Deck",group,p+new Vector3(0,.1f,0),new Vector3(4,.2f,4),wood);
            foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Box("Shade_Post_"+x+"_"+z,group,p+new Vector3(x*1.6f,1.5f,z*1.6f),new Vector3(.14f,2.8f,.14f),cream);
            Place(Pirate+"structure-roof.fbx",group,p+new Vector3(0,2.8f,0),0,new Vector3(4.5f,1.3f,4.5f),"Coastal_Shade_Roof");
            Box("Counter",group,p+new Vector3(0,.65f,.9f),new Vector3(3,1, .65f),roof);
        }
        static void Sign(Transform parent,Vector3 p,string label,Material board,Material letters,float yaw)
        {
            var group=Group("Sign_"+label.Replace(' ','_'),parent);group.localPosition=p;group.localRotation=Quaternion.Euler(0,yaw,0);
            Box("Sign_Board",group,Vector3.zero,new Vector3(1.5f,.5f,.1f),board);
            var text=new GameObject("Label");text.transform.SetParent(group,false);text.transform.localPosition=new Vector3(0,0,-.061f);
            var mesh=text.AddComponent<TextMesh>();mesh.text=label;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;
            mesh.characterSize=.13f;mesh.fontSize=48;mesh.color=letters.GetColor("_BaseColor");
        }
        static void Umbrella(Transform parent,Vector3 p,Material fabric,Material cream,Material wood,int index)
        {
            var group=Group("Beach_Sunshade_"+index,parent);
            Box("Umbrella_Pole",group,p+new Vector3(0,1.3f,0),new Vector3(.08f,2.6f,.08f),wood);
            var canopy=GameObject.CreatePrimitive(PrimitiveType.Sphere);canopy.name="Rounded_Sunshade";canopy.transform.SetParent(group,false);
            canopy.transform.localPosition=p+new Vector3(0,2.65f,0);canopy.transform.localScale=new Vector3(3,.65f,3);
            Object.DestroyImmediate(canopy.GetComponent<Collider>());canopy.GetComponent<Renderer>().sharedMaterial=fabric;
            for(int i=0;i<2;i++)Box("Beach_Lounger_"+i,group,p+new Vector3(i*.95f-.45f,.18f,1),new Vector3(.65f,.18f,1.8f),cream);
        }
    }
}
