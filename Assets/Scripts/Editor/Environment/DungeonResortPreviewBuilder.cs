using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // Authoring utility only: no gameplay-track, collision or spawning changes.
    internal static partial class DungeonResortPreviewBuilder
    {
        const string ScenePath = "Assets/Scenes/EnvironmentPreview.unity";
        const string RootName = "DungeonPreview_TreasureTunnel_48m";
        const string Source = "Assets/EndlessRollballAssets/Dungeon/";
        const string Ruins = Source + "Modular Ruins Pack/FBX/";
        const string Kay = Source + "KayKit Dungeon Pack/KayKit_Dungeon_Pack_1.1_FREE/Assets/";
        const string Materials = "Assets/EndlessRollballAssets/Materials/Environment/DungeonPreview";
        static readonly Dictionary<Material, Material> Remapped = new Dictionary<Material, Material>();
        static readonly StringBuilder Report = new StringBuilder();

        [MenuItem("Tools/Endless Rollball/Environment/Correct New Dungeon Assembly %#&r")]
        static void CorrectNewAssembly()
        {
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode.");
            var first=scene.GetRootGameObjects().Single(g=>g.name==RootName);
            first.name="DungeonPreview_InitialAssembly_Inactive";first.SetActive(false);
            try
            {
                Build();Object.DestroyImmediate(first);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
            }
            catch {first.name=RootName;first.SetActive(true);throw;}
        }

        [MenuItem("Tools/Endless Rollball/Environment/Inspect Dungeon Imported Orientation %#&y")]
        static void InspectOrientation()
        {
            var output=new StringBuilder();var temp=EditorSceneManager.NewPreviewScene();
            try
            {
                foreach(var path in new[]{Ruins+"Arch_Round_RoundColumn.fbx",Ruins+"Wall.fbx",Ruins+"Floor_Standard.fbx",Ruins+"Column_Round.fbx",Kay+"fbx(unity)/torch_lit.fbx"})
                {
                    var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);var g=(GameObject)PrefabUtility.InstantiatePrefab(source,temp);
                    output.AppendLine(path+" rotation="+g.transform.eulerAngles+" scale="+g.transform.localScale+" bounds="+Bounds(g));
                    foreach(var f in g.GetComponentsInChildren<MeshFilter>())output.AppendLine("  "+f.name+" localrot="+f.transform.localEulerAngles+" meshBounds="+f.sharedMesh.bounds);
                    Object.DestroyImmediate(g);
                }
                Directory.CreateDirectory("Artifacts/DungeonPreview");File.WriteAllText("Artifacts/DungeonPreview/ImportedOrientation.txt",output.ToString());
            }
            finally {EditorSceneManager.ClosePreviewScene(temp);}
            Debug.Log("Dungeon import orientation recorded.");
        }

        [MenuItem("Tools/Endless Rollball/Environment/Build Enclosed Dungeon Preview 48m %#&t")]
        static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open EnvironmentPreview in Edit Mode first.");
            if (scene.GetRootGameObjects().Any(g => g.name == RootName))
                throw new InvalidOperationException("Dungeon preview already exists; your edits have been preserved.");
            var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).FirstOrDefault();
            if (!camera) throw new InvalidOperationException("The existing preview camera is required.");
            string backup = "Artifacts/EnvironmentPreviewBackups/Dungeon_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(backup);
            File.Copy(ScenePath, backup + "/EnvironmentPreview_SavedBefore.unity");
            EditorSceneManager.SaveScene(scene, backup + "/EnvironmentPreview_LiveBefore.unity", true);
            var previous = scene.GetRootGameObjects();
            File.WriteAllText(backup + "/PresentationBefore.txt", string.Join("\n", previous.Select(g => g.name + " active=" + g.activeSelf)) +
                "\nCamera=" + EditorJsonUtility.ToJson(camera,true) + "\nSkybox=" + AssetDatabase.GetAssetPath(RenderSettings.skybox));
            Folder(Materials); Remapped.Clear(); Report.Clear();
            var staging = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, staging);
                var floor = Group("01_Preview_Stone_Route_VisualOnly", root.transform);
                var shell = Group("02_Enclosed_Walls_And_Ceiling", root.transform);
                var arches = Group("03_Columns_And_Arches", root.transform);
                var alcoves = Group("04_Treasure_And_Statue_Alcoves", root.transform);
                var clutter = Group("05_Pots_Rubble_And_Storage", root.transform);
                var nature = Group("06_Moss_And_Overgrowth", root.transform);
                var torches = Group("07_Torches_And_Warm_Lighting", root.transform);
                var distance = Group("08_Fogged_Continuation_ReviewOnly", root.transform);
                var lightRig = Group("09_Preview_Fill_Lighting", root.transform);
                var stone = Solid("Shell_WarmStone", new Color(.28f,.26f,.22f));
                var gold = Solid("Route_MutedGold", new Color(.77f,.55f,.16f));
                // Open entrance/exit: no wall caps or doors cross the running route.
                // Entrance extension keeps the existing camera physically inside the presentation.
                for (int side=-1;side<=1;side+=2)
                {
                    Box("Continuous_Wall_Backing_"+side,shell,new Vector3(side*11.1f,5.6f,18),new Vector3(.5f,11.6f,60),stone);
                    Box("Side_Foundation_"+side,floor,new Vector3(side*8, -.3f,18),new Vector3(7,.5f,60),stone);
                    Box("Route_Edge_"+side,floor,new Vector3(side*4.45f,.012f,24),new Vector3(.065f,.018f,48),gold);
                    for(int bay=-3;bay<12;bay++)
                    {
                        float z=bay*4+2;
                        Model(Ruins+(bay%4==1?"Wall_Overgrown.fbx":"Wall.fbx"),shell,"Stone_Wall_"+side+"_"+bay,
                            new Vector3(side*10.5f,-.05f,z),new Vector3(.75f,5.8f,4),side<0?90:-90,true);
                        Model(Ruins+"Wall.fbx",shell,"Upper_Masonry_"+side+"_"+bay,
                            new Vector3(side*10.5f,5.7f,z),new Vector3(.75f,5.4f,4),side<0?90:-90,true);
                    }
                    for(int i=0;i<9;i++)
                    {
                        float z=i*6;
                        Model(Ruins+(i%2==0?"Column_Round.fbx":"Column_Square.fbx"),arches,"Hall_Column_"+side+"_"+i,
                            new Vector3(side*8.8f,-.05f,z),new Vector3(1.25f,8.8f,1.25f),0,true);
                        var torch=Model(Kay+"fbx(unity)/torch_lit.fbx",torches,"Torch_"+side+"_"+i,
                            new Vector3(side*7.8f,2.2f,z+1.5f),new Vector3(.65f,1.5f,.65f),side<0?90:-90,false);
                        var tb=Bounds(torch);
                        Point("Torch_Amber_"+side+"_"+i,torches,new Vector3(tb.center.x,tb.max.y-.15f,tb.center.z),
                            new Color(1,.58f,.22f),5,9,false);
                    }
                }
                // A removable preview floor, not a replacement for runtime track floors or gaps.
                for(int z=-4;z<16;z++)for(int lane=-1;lane<=1;lane++)
                    Model(Ruins+(z%3==0?"Floor_Diamond.fbx":"Floor_Standard.fbx"),floor,"Route_Tile_"+z+"_"+lane,
                        new Vector3(lane*3,-.22f,z*3+1.5f),new Vector3(3,.22f,3),0,true);
                Box("Ceiling_Backing",shell,new Vector3(0,11.5f,18),new Vector3(22.6f,.6f,60),stone);
                for(int z=-3;z<12;z++)for(int x=-2;x<=2;x++)
                    Model(Kay+"fbx(unity)/ceiling_tile.fbx",shell,"Coffered_Ceiling_"+x+"_"+z,
                        new Vector3(x*4.4f,10.95f,z*4+2),new Vector3(4.4f,.35f,4),0,true);
                for(int i=0;i<5;i++)
                    Model(Ruins+"Arch_Round_RoundColumn.fbx",arches,"Processional_Arch_"+i,
                        new Vector3(0,-.05f,i*12),new Vector3(19,10.7f,1.1f),0,true);

                // Recesses and treasure sit outside the 9m route plus 1m clearance buffer.
                for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++)
                {
                    float z=5+i*12;
                    float x=side*7.3f;
                    if(i%2==0)
                    {
                        Box("Statue_Dais_"+side+"_"+i,alcoves,new Vector3(x,.18f,z),new Vector3(2.3f,.4f,2.3f),stone);
                        Model(Ruins+(side<0?"Statue_Fox.fbx":"Statue_Stag.fbx"),alcoves,"Guardian_Statue_"+side+"_"+i,
                            new Vector3(x,.38f,z),new Vector3(1.8f,3.8f,1.8f),side<0?90:-90,false);
                    }
                    else
                    {
                        Model(Kay+"fbx(unity)/chest_gold.fbx",alcoves,"Treasure_Chest_"+side+"_"+i,
                            new Vector3(x,0,z),new Vector3(1.8f,1.5f,1.4f),side<0?65:-65,false);
                        Model(Kay+"fbx(unity)/coin_stack_large.fbx",alcoves,"Treasure_Coins_"+side+"_"+i,
                            new Vector3(side*6.3f,0,z+1.4f),new Vector3(.8f,.55f,.8f),i*47,false);
                    }
                    Model(Ruins+(i%2==0?"Pot2.fbx":"Pot1_Broken.fbx"),clutter,"Ancient_Urn_"+side+"_"+i,
                        new Vector3(side*6.4f,0,z+3),new Vector3(1,1.4f,1),i*63,false);
                    Model(Kay+"fbx(unity)/rubble_half.fbx",clutter,"Wall_Rubble_"+side+"_"+i,
                        new Vector3(side*9.5f,0,z+3.5f),new Vector3(1.3f,.8f,2.2f),i*71,false);
                    Model(Ruins+"Bush_1x1.fbx",nature,"Moss_Cluster_"+side+"_"+i,
                        new Vector3(side*9.7f,.03f,z+1.5f),new Vector3(1.1f,.55f,1.4f),i*49,false);
                }
                for(int i=0;i<4;i++)
                {
                    int side=i%2==0?-1:1;float z=9+i*10;
                    Model(Kay+"fbx(unity)/barrel_small_stack.fbx",clutter,"Ancient_Storage_"+i,
                        new Vector3(side*9,0,z),new Vector3(1.4f,1.6f,1.4f),i*90,false);
                    Model(Ruins+"Candles_2.fbx",alcoves,"Candle_Offering_"+i,
                        new Vector3(side*6.2f,0,z+1.6f),new Vector3(.8f,.9f,.8f),0,false);
                }
                // Simpler distant corridor fades into charcoal, never a visible route-ending doorway.
                Box("Distant_Floor",distance,new Vector3(0,-.22f,72),new Vector3(22,.4f,48),stone);
                Box("Distant_Ceiling",distance,new Vector3(0,11.5f,72),new Vector3(22,.6f,48),stone);
                foreach(int side in new[]{-1,1})
                    Box("Distant_Wall_"+side,distance,new Vector3(side*11.1f,5.6f,72),new Vector3(.6f,11.6f,48),stone);
                for(int z=60;z<96;z+=12)
                    Model(Ruins+"Arch_Round_RoundColumn.fbx",distance,"Distant_Arch_"+z,
                        new Vector3(0,-.05f,z),new Vector3(19,10.7f,1.1f),0,true);
                for(int z=-4;z<60;z+=12)
                    Point("Cool_Soft_Fill_"+z,lightRig,new Vector3(0,7,z),new Color(.42f,.60f,.80f),3.5f,16,false);
                StreetCitySceneryExtractor.StripNonVisualComponents(root);
                // The scenery remains visual-only; restore only authoring-preview lights removed by the shared stripper.
                // Create them after stripping so no imported physics, animation or gameplay scripts survive.
                foreach(Transform empty in torches.Cast<Transform>().Where(t=>t.name.StartsWith("Torch_Amber_")).ToArray())
                    AttachPoint(empty.gameObject,new Color(1,.58f,.22f),18,10);
                foreach(Transform empty in lightRig)
                    AttachPoint(empty.gameObject,new Color(.52f,.65f,.82f),10,18);
                var fill=Group("Soft_Architectural_Fill",lightRig).gameObject;
                fill.transform.rotation=Quaternion.Euler(48,-25,0);
                var fillLight=fill.AddComponent<Light>();fillLight.type=LightType.Directional;
                fillLight.intensity=.75f;fillLight.color=new Color(1,.88f,.73f);fillLight.shadows=LightShadows.None;

                SceneManager.MoveGameObjectToScene(root,scene);
                foreach(var old in previous)
                    if(old.activeSelf && !old.GetComponent<Camera>() && !old.name.Contains("EventSystem")) old.SetActive(false);
                RenderSettings.skybox=null;RenderSettings.sun=null;
                RenderSettings.ambientMode=AmbientMode.Trilight;
                RenderSettings.ambientSkyColor=new Color(.40f,.44f,.50f);
                RenderSettings.ambientEquatorColor=new Color(.38f,.36f,.32f);
                RenderSettings.ambientGroundColor=new Color(.25f,.23f,.20f);
                RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
                RenderSettings.fogColor=new Color(.045f,.05f,.065f);RenderSettings.fogStartDistance=38;RenderSettings.fogEndDistance=88;
                camera.transform.SetPositionAndRotation(new Vector3(0,4,-8),Quaternion.Euler(10,0,0));
                camera.fieldOfView=62;camera.farClipPlane=120;camera.nearClipPlane=.1f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;
                AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene,ScenePath);
                File.WriteAllText(backup+"/DungeonComposition.txt","48m enclosed dungeon with entrance camera extension and 48m fogged distant continuation.\n"+
                    "Preview floor is visual-only. No gameplay track/prefab/collider changes. No Play Mode or tests run.\n"+Report);
                Selection.activeGameObject=root;
                if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(new Vector3(0,4,17),Quaternion.Euler(9,0,0),22);
                Debug.Log("DUNGEON_PREVIEW_SAVED: "+RootName+" in existing EnvironmentPreview. Earlier roots preserved inactive. Backup: "+backup);
            }
            catch {if(root)Object.DestroyImmediate(root);throw;}
            finally {EditorSceneManager.ClosePreviewScene(staging);}
        }

        static Transform Group(string name,Transform parent) {var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;}
        static void Folder(string path) {if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
        static Bounds Bounds(GameObject g) {var rr=g.GetComponentsInChildren<Renderer>(true);if(rr.Length==0)throw new InvalidOperationException("No geometry: "+g.name);var b=rr[0].bounds;foreach(var r in rr.Skip(1))b.Encapsulate(r.bounds);return b;}
        static Material Solid(string name,Color color)
        {
            string path=Materials+"/MAT_AD_"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat)return mat;
            mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="MAT_AD_"+name,enableInstancing=true};
            mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",.15f);mat.SetFloat("_Cull",0);AssetDatabase.CreateAsset(mat,path);return mat;
        }
        static void Box(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=size;
            Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=material;
        }
        static GameObject Model(string path,Transform parent,string name,Vector3 ground,Vector3 limit,float yaw,bool stretch)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!source)throw new FileNotFoundException("Imported model missing",path);
            var g=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);g.name=name;g.transform.localPosition=Vector3.zero;
            // Quaternius FBXs carry a +/-90-degree X conversion at their imported root.
            // Retain it; replacing it lays arches and statues flat even when their AABBs are resized.
            g.transform.localRotation=Quaternion.Euler(0,yaw,0)*source.transform.localRotation;
            var b=Bounds(g);var ratio=new Vector3(limit.x/Mathf.Max(.0001f,b.size.x),limit.y/Mathf.Max(.0001f,b.size.y),limit.z/Mathf.Max(.0001f,b.size.z));
            if(stretch)
            {
                var right=g.transform.right;var up=g.transform.up;var forward=g.transform.forward;
                var localRatio=new Vector3(
                    Mathf.Abs(right.x)*ratio.x+Mathf.Abs(right.y)*ratio.y+Mathf.Abs(right.z)*ratio.z,
                    Mathf.Abs(up.x)*ratio.x+Mathf.Abs(up.y)*ratio.y+Mathf.Abs(up.z)*ratio.z,
                    Mathf.Abs(forward.x)*ratio.x+Mathf.Abs(forward.y)*ratio.y+Mathf.Abs(forward.z)*ratio.z);
                g.transform.localScale=Vector3.Scale(g.transform.localScale,localRatio);
            }
            else g.transform.localScale*=Mathf.Min(ratio.x,ratio.y,ratio.z);
            b=Bounds(g);g.transform.position+=ground-new Vector3(b.center.x,b.min.y,b.center.z);
            foreach(var renderer in g.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>Convert(m,path)).ToArray();
            Report.AppendLine(name+" | "+path+" | "+Bounds(g).ToString("F2"));return g;
        }
        static Material Convert(Material source,string model)
        {
            if(!source)return Solid("Fallback_Stone",new Color(.54f,.49f,.39f));
            if(Remapped.TryGetValue(source,out var mapped))return mapped;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string guid,out long localId);
            string safe=string.Concat(source.name.Select(c=>char.IsLetterOrDigit(c)?c:'_'));
            string path=Materials+"/MAT_AD_Imported_"+safe+"_"+guid.Substring(0,Mathf.Min(8,guid.Length))+"_"+localId+".mat";
            mapped=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mapped)
            {
                mapped=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=Path.GetFileNameWithoutExtension(path),enableInstancing=true};
                var texture=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.mainTexture;
                if(!texture && model.StartsWith(Kay))texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Kay+"textures/dungeon_texture.png");
                mapped.SetTexture("_BaseMap",texture);
                mapped.SetColor("_BaseColor",source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.color:Color.white);
                mapped.SetFloat("_Smoothness",.12f);mapped.SetFloat("_Metallic",0);mapped.SetFloat("_Cull",0);
                if(source.name.IndexOf("flame",StringComparison.OrdinalIgnoreCase)>=0 || source.name.IndexOf("fire",StringComparison.OrdinalIgnoreCase)>=0)
                {mapped.EnableKeyword("_EMISSION");mapped.SetColor("_EmissionColor",new Color(1.5f,.4f,.05f));}
                AssetDatabase.CreateAsset(mapped,path);
            }
            Remapped[source]=mapped;return mapped;
        }
        static void Point(string name,Transform parent,Vector3 position,Color color,float intensity,float range,bool shadows)
        {var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=position;AttachPoint(g,color,intensity,range);}
        static void AttachPoint(GameObject g,Color color,float intensity,float range)
        {var l=g.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;l.shadows=LightShadows.None;}
    }
}
