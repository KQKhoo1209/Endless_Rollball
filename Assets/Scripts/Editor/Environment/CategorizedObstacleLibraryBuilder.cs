using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EndlessRollball.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace EndlessRollball.Editor
{
    // Explicit create-only request. Never regenerates on an ordinary domain reload.
    internal static class CategorizedObstacleLibraryBuilder
    {
        const string Root = "Assets/EndlessRollballAssets/Obstacles/Shared";
        const string Kenney = "Assets/EndlessRollballAssets/Obstacles/kenney_survival-kit";
        const string Kay = "Assets/EndlessRollballAssets/Obstacles/KayKit_ResourceBits_1.0_FREE";
        const string Vehicles = "Assets/EndlessRollballAssets/Street/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels";
        const string Request = "Artifacts/ObstacleLibraryV2/Create.request";
        const string VehicleRequest = "Artifacts/ObstacleLibraryV2/ResizeVehicles.request";
        const string Report = "Artifacts/ObstacleLibraryV2/AuthoringReport.txt";
        const string ManifestPath = Root + "/CategorizedAssetManifest.json";
        static double deadline;
        static string activeRequest;
        static readonly string[] Categories = { "FixedPosition", "NavigateLeftRight", "JumpableOnly" };
        static readonly string[][] Names =
        {
            new[] { "CratePyramid", "CrateWall", "BarrelStack", "MixedCargo", "StoneColumn", "RockMound", "LumberTower", "ParkedCompactCar", "ParkedSUV", "DeliveryPickup" },
            new[] { "SlidingCrates", "SlidingBarrels", "SlidingStone", "SlidingTimber", "SlidingRocks", "SlidingFence", "CrossingCompactCar", "CrossingSUV", "CrossingPickup", "CrossingTruck" },
            new[] { "LogRow", "TimberBundleRow", "StoneBrickRow", "LowCrateRow", "FallenBarrelRow", "FenceRailRow", "RubbleBand", "FortifiedFenceRow", "TextileBundleRow", "IronBundleRow" }
        };

        [Serializable] sealed class Manifest
        {
            public string scope = "30 standalone review prefabs; no automatic placement, scoring, collision-loss rules or scene changes.";
            public string placement = "Unit scale, +Z approach, Y=0 floor. Fixed obstacles can occupy one lane. Moving and JumpableOnly roots must be centred at road X=0. Route inner walls are X=+-4.5m.";
            public string modifications = "New wrappers, simple colliders, fitted source instances. Shared meshes and original materials unchanged. Jump rows use small X fitting to close tile seams; other payloads are uniformly scaled.";
            public string validation = "Editor authoring checks only. Jump clearance, motion, pooling, collision response and gameplay acceptance remain user review.";
            public Source[] sources;
            public Entry[] obstacles;
        }
        [Serializable] sealed class Source
        {
            public string author, source, license, evidence;
        }
        [Serializable] sealed class Entry
        {
            public string name, category, destination, useNotes;
            public string[] eligibleThemes, modelSources, materials;
            public Vector3 visualSize, colliderSize;
            public int colliderCount, triangles, materialCount;
            public float halfTravel, speed, startPhase, sweptWidth;
        }

        [InitializeOnLoadMethod]
        static void Schedule()
        {
            activeRequest = File.Exists(VehicleRequest) ? VehicleRequest : Request;
            string token = activeRequest == VehicleRequest ? "resize-four-crossing-vehicles-v2" : "create-thirty-categorized-obstacles-v2";
            if (Application.isBatchMode || !File.Exists(activeRequest) ||
                DateTime.UtcNow - File.GetLastWriteTimeUtc(activeRequest) > TimeSpan.FromMinutes(30) ||
                File.ReadAllText(activeRequest).Trim() != token) return;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += RunRequest;
        }

        static void RunRequest()
        {
            if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= RunRequest;
            if (!File.Exists(activeRequest)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                File.WriteAllText(activeRequest + ".blocked", "Exit Play Mode/Prefab Mode first. Request was not consumed.");
                return;
            }
            try { if (activeRequest == VehicleRequest) ResizeCrossingVehicles(); else Build(); }
            catch (Exception error)
            {
                File.WriteAllText(activeRequest + ".failed", error.ToString());
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Endless Rollball/Obstacles/Create Categorized Library (30)")]
        static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Play Mode/Prefab Mode first.");
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 10; i++)
                    if (File.Exists(PathFor(c, i))) throw new IOException("Existing prefab preserved: " + PathFor(c, i));
            if (File.Exists(ManifestPath)) throw new IOException("Existing categorized manifest preserved.");
            var kenney = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_OBS_Shared_Kenney.mat");
            var kay = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_OBS_Shared_KayKit.mat");
            var barrel = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MAT_OBS_Shared_BarrelWood.mat");
            if (!kenney || !kay || !barrel) throw new InvalidOperationException("Original shared materials must exist first.");
            foreach (string source in AllSources())
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(source)) throw new FileNotFoundException("Missing source: " + source);
            foreach (string category in Categories) SharedObstaclePrefabBuilder.EnsureFolder(Root + "/Prefabs/" + category);
            if (File.Exists(Request)) File.Move(Request, Request + ".consumed_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));

            Scene scene = EditorSceneManager.NewPreviewScene();
            var entries = new List<Entry>();
            var report = new StringBuilder("CATEGORIZED_OBSTACLES_SAVED\n10 FixedPosition + 10 NavigateLeftRight + 10 JumpableOnly. Original six preserved.\n");
            try
            {
                for (int category = 0; category < 3; category++)
                for (int index = 0; index < 10; index++)
                {
                    var root = new GameObject(Path.GetFileNameWithoutExtension(PathFor(category, index)));
                    SceneManager.MoveGameObjectToScene(root, scene);
                    Transform owner = root.transform;
                    if (category == 1) owner = Child(root.transform, "Mover");
                    Transform visual = Child(owner, "Visual");
                    Transform collision = Child(owner, "Collision");
                    var used = new HashSet<string>();
                    Action<string, string, Material, Vector3, Vector3, Quaternion> part = (label, source, mat, size, pos, rotation) =>
                        SharedObstaclePrefabBuilder.AddPart(visual, collision, label, source, mat, size, pos, false, rotation, used, scene);
                    if (category == 2) BuildJumpRow(index, visual, collision, kenney, kay, barrel, used, scene);
                    else if (category == 0) BuildFixed(index, part, kenney, kay, barrel);
                    else BuildMoving(index, part, kenney, kay, barrel);

                    Bounds bounds = SharedObstaclePrefabBuilder.RendererBounds(root);
                    float halfTravel = 0f, speed = 0f, phase = 0f;
                    if (category == 1)
                    {
                        halfTravel = Mathf.Min(2.7f, 4.35f - bounds.extents.x);
                        speed = 1.2f + index % 4 * 0.25f;
                        phase = index % 2 == 0 ? 0.25f : 0.75f;
                        var body = owner.gameObject.AddComponent<Rigidbody>();
                        body.isKinematic = true;
                        body.useGravity = false;
                        body.interpolation = RigidbodyInterpolation.Interpolate;
                        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                        body.constraints = RigidbodyConstraints.FreezeRotation;
                        var motion = owner.gameObject.AddComponent<SideToSideObstacleMotion>();
                        var serialized = new SerializedObject(motion);
                        serialized.FindProperty("halfTravel").floatValue = halfTravel;
                        serialized.FindProperty("speed").floatValue = speed;
                        serialized.FindProperty("startPhase").floatValue = phase;
                        serialized.FindProperty("localHomePosition").vector3Value = Vector3.zero;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                    Entry entry = Audit(root, category, index, bounds, used, halfTravel, speed, phase);
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, entry.destination, out bool success);
                    if (!success) throw new IOException("Could not save " + entry.destination);
                    AssetDatabase.SetLabels(saved, new[] { "EndlessRollball", "ObstacleLibraryV2", entry.category }.Concat(entry.eligibleThemes).ToArray());
                    entries.Add(entry);
                    report.AppendLine(entry.name + " | visual=" + bounds.size.ToString("F3") + " | collision=" + entry.colliderSize.ToString("F3") +
                        " | shapes=" + entry.colliderCount + " | tris=" + entry.triangles + " | halfTravel=" + halfTravel.ToString("F3") + " | themes=" + string.Join(",", entry.eligibleThemes));
                    Object.DestroyImmediate(root);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            var manifest = new Manifest
            {
                sources = new[]
                {
                    new Source { author="Kenney", source="https://kenney.nl/assets/survival-kit", license="CC0", evidence=Kenney+"/License.txt" },
                    new Source { author="Kay Lousberg", source="https://kaylousberg.itch.io/resource-bits", license="CC0", evidence=Kay+"/License.txt" },
                    new Source { author="Existing SimplePoly City imported pack; author not independently verified", source="https://assetstore.unity.com/packages/3d/environments/simplepoly-city-low-poly-assets-58899", license="Existing imported Unity Asset Store pack terms; NOT CC0. No new licence certification or redistribution permission asserted.", evidence=Vehicles }
                },
                obstacles = entries.ToArray()
            };
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
            AssetDatabase.ImportAsset(ManifestPath);
            report.AppendLine("Moving roots: centre X=0, unit scale; body sweeps inside X=+-4.35m. Kinematic FixedUpdate MovePosition; deterministic enable/disable reset.");
            report.AppendLine("JumpableOnly: continuous visible 9m rows, one 9m non-trigger collider, 0.72m high. Requires manual jump/landing acceptance, not a scored jump task yet.");
            report.AppendLine("No source meshes/materials, original six prefabs, scenes, roads, track selection, scoring or difficulty edited. No Play Mode, automated tests or profiling.");
            File.WriteAllText(Report, report.ToString());
            SharedObstaclePrefabBuilder.QueuePreviews(entries.Select(e => e.destination), "Artifacts/ObstacleLibraryV2/Previews");
            Debug.Log("CATEGORIZED_OBSTACLES_SAVED: " + Report);
        }

        static void ResizeCrossingVehicles()
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            if (manifest.obstacles.Length != 30) throw new InvalidOperationException("Expected 30 library entries.");
            string backup = "Artifacts/ObstacleLibraryV2/BeforeVehicleResize_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            Directory.CreateDirectory(backup);
            string rockPath = PathFor(0,5);
            File.Copy(rockPath,backup+"/"+Path.GetFileName(rockPath));
            File.Copy(rockPath+".meta",backup+"/"+Path.GetFileName(rockPath)+".meta");
            for (int i=6; i<10; i++)
            {
                File.Copy(PathFor(1,i),backup+"/"+Path.GetFileName(PathFor(1,i)));
                File.Copy(PathFor(1,i)+".meta",backup+"/"+Path.GetFileName(PathFor(1,i))+".meta");
            }
            File.Copy(ManifestPath,backup+"/CategorizedAssetManifest.json");
            File.Move(VehicleRequest,VehicleRequest+".consumed_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            for (int i=6; i<10; i++)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(PathFor(1,i));
                try
                {
                    Transform mover = root.transform.Find("Mover");
                    Transform visual = mover.Find("Visual"), collision = mover.Find("Collision");
                    while (visual.childCount>0) Object.DestroyImmediate(visual.GetChild(0).gameObject);
                    while (collision.childCount>0) Object.DestroyImmediate(collision.GetChild(0).gameObject);
                    var used = new HashSet<string>();
                    BuildMoving(i,(label,source,mat,size,pos,rotation)=>SharedObstaclePrefabBuilder.AddPart(
                        visual,collision,label,source,mat,size,pos,false,rotation,used,root.scene),null,null,null);
                    Bounds bounds=SharedObstaclePrefabBuilder.RendererBounds(root);
                    float travel=Mathf.Min(2.7f,4.35f-bounds.extents.x);
                    var serialized=new SerializedObject(mover.GetComponent<SideToSideObstacleMotion>());
                    serialized.FindProperty("halfTravel").floatValue=travel;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Entry entry=Audit(root,1,i,bounds,used,travel,1.2f+i%4*.25f,i%2==0?.25f:.75f);
                    PrefabUtility.SaveAsPrefabAsset(root,entry.destination,out bool saved);
                    if (!saved) throw new IOException("Could not resize "+entry.destination);
                    int entryIndex=Array.FindIndex(manifest.obstacles,e=>e.destination==entry.destination);
                    manifest.obstacles[entryIndex]=entry;
                    File.AppendAllText(Report,"Vehicle scale refinement: "+entry.name+" | visual="+bounds.size.ToString("F3")+" | halfTravel="+travel.ToString("F3")+" | sweptWidth="+entry.sweptWidth.ToString("F3")+"\n");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            GameObject rockRoot=PrefabUtility.LoadPrefabContents(rockPath);
            try
            {
                Transform visual=rockRoot.transform.Find("Visual"), collision=rockRoot.transform.Find("Collision");
                while(visual.childCount>0) Object.DestroyImmediate(visual.GetChild(0).gameObject);
                while(collision.childCount>0) Object.DestroyImmediate(collision.GetChild(0).gameObject);
                var used=new HashSet<string>();
                Material material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/MAT_OBS_Shared_Kenney.mat");
                BuildFixed(5,(label,source,mat,size,pos,rotation)=>SharedObstaclePrefabBuilder.AddPart(
                    visual,collision,label,source,mat,size,pos,false,rotation,used,rockRoot.scene),material,null,null);
                Bounds bounds=SharedObstaclePrefabBuilder.RendererBounds(rockRoot);
                Entry entry=Audit(rockRoot,0,5,bounds,used,0,0,0);
                PrefabUtility.SaveAsPrefabAsset(rockRoot,rockPath,out bool saved);
                if (!saved) throw new IOException("Could not refine rock support.");
                manifest.obstacles[Array.FindIndex(manifest.obstacles,e=>e.destination==rockPath)]=entry;
                File.AppendAllText(Report,"Rock support refinement: "+entry.name+" | visual="+bounds.size.ToString("F3")+"\n");
            }
            finally { PrefabUtility.UnloadPrefabContents(rockRoot); }
            File.WriteAllText(ManifestPath,JsonUtility.ToJson(manifest,true));
            AssetDatabase.ImportAsset(ManifestPath);
            SharedObstaclePrefabBuilder.QueuePreviews(Enumerable.Range(6,4).Select(i=>PathFor(1,i)).Concat(new[]{rockPath}),"Artifacts/ObstacleLibraryV2/Previews");
            File.AppendAllText(Report,"CROSSING_VEHICLES_RESIZED. Current manifest supersedes initial crossing-vehicle measurements above. Backup: "+backup+"\n");
            Debug.Log("CROSSING_VEHICLES_RESIZED");
        }

        static void BuildFixed(int i, Action<string, string, Material, Vector3, Vector3, Quaternion> p, Material k, Material y, Material b)
        {
            Quaternion q = Quaternion.identity;
            switch (i)
            {
                case 0:
                    p("Crate_L", K("box"), k, Vector3.one * 1.15f, new Vector3(-.59f, 0, 0), q);
                    p("Crate_R", K("box"), k, Vector3.one * 1.15f, new Vector3(.59f, 0, 0), q);
                    p("Crate_Top", K("box"), k, Vector3.one * 1.15f, new Vector3(0, 1.15f, 0), Quaternion.Euler(0, 90, 0)); break;
                case 1:
                    for (int row = 0; row < 2; row++) for (int col = 0; col < 2; col++)
                        p("Crate_"+row+"_"+col, K("box"), k, Vector3.one*1.05f, new Vector3((col-.5f)*1.1f, row*1.05f, 0), q);
                    break;
                case 2:
                    p("Barrel_L", K("barrel"), b, new Vector3(1, 1.2f, 1), new Vector3(-.43f, 0, 0), q);
                    p("Barrel_R", K("barrel"), b, new Vector3(1, 1.2f, 1), new Vector3(.43f, 0, 0), q);
                    p("Barrel_Top", K("barrel"), b, new Vector3(1, 1.2f, 1), new Vector3(0, 1.2f, 0), q); break;
                case 3:
                    p("CargoCrate", K("box"), k, Vector3.one * 1.35f, new Vector3(-.48f, 0, 0), q);
                    p("CargoBarrel", K("barrel"), b, new Vector3(.9f, 1.35f, .9f), new Vector3(.69f, 0, 0), q);
                    p("UpperCrate", K("box"), k, Vector3.one * .9f, new Vector3(-.48f, 1.35f, 0), q); break;
                case 4:
                    for (int row = 0; row < 2; row++) p("Stone_"+row, Y("Stone_Bricks_Stack_Large"), y, new Vector3(2.4f, 1.2f, 1.6f), new Vector3(0, row*1.2f, 0), Quaternion.Euler(0,row*180,0));
                    break;
                case 5:
                    p("Rock_L", K("rock-a"), k, new Vector3(1.3f, 1.4f, 1.4f), new Vector3(-.6f,0,0), Quaternion.Euler(0,25,0));
                    p("Rock_R", K("rock-b"), k, new Vector3(1.3f, 1.5f, 1.4f), new Vector3(.6f,0,0), Quaternion.Euler(0,-20,0));
                    p("Rock_Cap", K("rock-a"), k, new Vector3(1.15f, 1.1f, 1.2f), new Vector3(0,.50f,0), Quaternion.Euler(0,70,0)); break;
                case 6:
                    for (int row = 0; row < 3; row++) p("Lumber_"+row, Y("Wood_Planks_Stack_Medium"), y, new Vector3(2.35f,.7f,1.9f), new Vector3(0,row*.7f,0), Quaternion.Euler(0,row*180,0));
                    break;
                default:
                    p("Vehicle", V(i == 7 ? "Car" : i == 8 ? "SUV" : "Pick up Truck", "01"), null, new Vector3(2.3f,2.6f,4.7f), Vector3.zero, q); break;
            }
        }

        static void BuildMoving(int i, Action<string, string, Material, Vector3, Vector3, Quaternion> p, Material k, Material y, Material b)
        {
            Quaternion q = Quaternion.identity;
            switch (i)
            {
                case 0:
                    p("Crate_Base", K("box"), k, Vector3.one*1.35f, Vector3.zero, q);
                    p("Crate_Top", K("box"), k, Vector3.one*.9f, new Vector3(0,1.35f,0), Quaternion.Euler(0,90,0)); break;
                case 1:
                    p("Barrel_L", K("barrel"), b, new Vector3(1,1.5f,1), new Vector3(-.5f,0,0), q);
                    p("Barrel_R", K("barrel"), b, new Vector3(1,1.5f,1), new Vector3(.5f,0,0), q); break;
                case 2: p("Stone", Y("Stone_Bricks_Stack_Large"), y, new Vector3(2.3f,1.8f,1.8f), Vector3.zero,q); break;
                case 3: p("Timber", Y("Wood_Log_Stack"), y, new Vector3(2.4f,1.5f,1.6f), Vector3.zero,Quaternion.Euler(0,90,0)); break;
                case 4:
                    p("Rock_L", K("rock-b"), k, new Vector3(1.3f,1.3f,1.3f), new Vector3(-.55f,0,0),q);
                    p("Rock_R", K("rock-a"), k, new Vector3(1.3f,1.3f,1.3f), new Vector3(.55f,0,0),Quaternion.Euler(0,45,0)); break;
                case 5: p("Fence", K("fence-fortified"), k,new Vector3(2.5f,1.8f,1.1f),Vector3.zero,q); break;
                default:
                    string vehicle = i == 6 ? "Car" : i == 7 ? "SUV" : i == 8 ? "Pick up Truck" : "Truck";
                    p("Vehicle", V(vehicle,"02"), null, new Vector3(4.5f,2.3f,2.25f), Vector3.zero, Quaternion.Euler(0,90,0)); break;
            }
        }

        static void BuildJumpRow(int i, Transform visual, Transform collision, Material k, Material y, Material b, HashSet<string> used, Scene scene)
        {
            string[] sources = { Y("Wood_Log_A"), Y("Wood_Planks_Stack_Medium"), Y("Stone_Brick"), K("box"), K("barrel"), K("fence"), Y("Stone_Chunks_Small"), K("fence-fortified"), Y("Textiles_Stack_Small"), Y("Iron_Bars_Stack_Small") };
            Material material = i == 4 ? b : i == 3 || i == 5 || i == 7 ? k : y;
            Quaternion rotation = i == 0 ? Quaternion.Euler(0,90,0) : i == 4 ? Quaternion.Euler(0,0,90) : Quaternion.identity;
            const float height = .72f;
            Bounds first = SharedObstaclePrefabBuilder.AddPart(visual, collision, "Row_00", sources[i], material,
                new Vector3(100,height,100), Vector3.zero, false, rotation, used, scene);
            int count = Mathf.Max(1, Mathf.RoundToInt(9f/first.size.x));
            if (count > 40 || first.size.z > 2.7f) throw new InvalidOperationException("Unsuitable low-row proportions: " + sources[i]);
            float tileWidth = 9f/count;
            for (int tile = 0; tile < count; tile++)
            {
                Transform holder;
                if (tile == 0) holder = visual.GetChild(0);
                else
                {
                    SharedObstaclePrefabBuilder.AddPart(visual, collision, "Row_"+tile.ToString("D2"), sources[i], material,
                        new Vector3(100,height,100), Vector3.zero, false, rotation, used, scene);
                    holder = visual.GetChild(visual.childCount-1);
                }
                // Fit in road/world X after the model's rotation, not its local X.
                Transform fit = Child(visual,"TileFit_"+tile.ToString("D2"));
                holder.SetParent(fit,true);
                Vector3 scale = fit.localScale;
                scale.x *= tileWidth/first.size.x;
                fit.localScale = scale;
                Bounds fitted = SharedObstaclePrefabBuilder.RendererBounds(fit.gameObject);
                fit.localPosition += new Vector3(-4.5f+(tile+.5f)*tileWidth-fitted.center.x, -fitted.min.y, -fitted.center.z);
            }
            // Replace individual tile shapes with one continuous visible-row envelope.
            while (collision.childCount > 0) Object.DestroyImmediate(collision.GetChild(0).gameObject);
            Bounds row = SharedObstaclePrefabBuilder.RendererBounds(visual.gameObject);
            var shape = Child(collision,"COL_FullWidthRow").gameObject.AddComponent<BoxCollider>();
            shape.center = new Vector3(0,height*.5f,0);
            shape.size = new Vector3(9,height,row.size.z);
        }

        static Entry Audit(GameObject root, int category, int index, Bounds bounds, HashSet<string> used, float travel, float speed, float phase)
        {
            BoxCollider[] shapes = root.GetComponentsInChildren<BoxCollider>(true);
            if (shapes.Length == 0 || root.GetComponentsInChildren<Collider>(true).Length != shapes.Length || shapes.Any(c => c.isTrigger || !c.enabled))
                throw new InvalidOperationException("Unexpected collision hierarchy: " + root.name);
            Bounds collision = new Bounds(shapes[0].transform.TransformPoint(shapes[0].center), shapes[0].size);
            foreach (BoxCollider shape in shapes.Skip(1)) collision.Encapsulate(new Bounds(shape.transform.TransformPoint(shape.center),shape.size));
            if (Mathf.Abs(bounds.min.y) > .01f || Mathf.Abs(bounds.center.x) > .3f || bounds.size.y <= .01f)
                throw new InvalidOperationException("Invalid ground pivot or visual bounds: " + root.name);
            if (category == 2 && (Mathf.Abs(bounds.size.x-9f)>.01f || Mathf.Abs(collision.size.x-9f)>.01f || Mathf.Abs(bounds.size.y-.72f)>.01f))
                throw new InvalidOperationException("Full-route jump row mismatch: " + root.name);
            if (category == 0 && bounds.size.x > 2.85f || category == 1 && bounds.size.x > 4.51f)
                throw new InvalidOperationException("Payload too wide: " + root.name);
            Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
            SideToSideObstacleMotion[] motions = root.GetComponentsInChildren<SideToSideObstacleMotion>(true);
            if (bodies.Length != (category == 1 ? 1 : 0) || motions.Length != (category == 1 ? 1 : 0) ||
                (category == 1 && (!bodies[0].isKinematic || bodies[0].useGravity || travel <= 0f || bounds.extents.x+travel > 4.4f)))
                throw new InvalidOperationException("Invalid motion setup: " + root.name);
            if (root.GetComponentsInChildren<MonoBehaviour>(true).Length != motions.Length || root.GetComponentsInChildren<Light>(true).Length != 0)
                throw new InvalidOperationException("Unexpected imported behaviour/light: " + root.name);
            Material[] materials = root.GetComponentsInChildren<Renderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
            if (materials.Any(m=>!m || !m.shader || !m.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal)))
                throw new InvalidOperationException("Missing/non-URP material: " + root.name);
            bool vehicle = category == 0 && index >= 7 || category == 1 && index >= 6;
            return new Entry
            {
                name=root.name, category=Categories[category], destination=PathFor(category,index), visualSize=bounds.size, colliderSize=collision.size,
                colliderCount=shapes.Length, halfTravel=travel, speed=speed, startPhase=phase, sweptWidth=bounds.size.x+2*travel,
                eligibleThemes=vehicle ? new[] { "Street", "Beach" } : new[] { "Street", "Beach", "Dungeon" },
                modelSources=used.OrderBy(s=>s).ToArray(), materials=materials.Select(AssetDatabase.GetAssetPath).ToArray(), materialCount=materials.Length,
                triangles=root.GetComponentsInChildren<MeshFilter>(true).Sum(f => Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(s=>(int)f.sharedMesh.GetIndexCount(s)/3)),
                useNotes=category == 2 ? "Road-centred full-width low barrier. No accepted-jump evidence, landing trigger or score integration yet." :
                    category == 1 ? "Road-centred kinematic lateral mover. Theme eligibility is metadata only until a future spawner enforces it." : "Stationary payload; choose lane placement later. Not jump-proof or a scored task by itself."
            };
        }

        static Transform Child(Transform parent, string name) { var t=new GameObject(name).transform; t.SetParent(parent,false); return t; }
        static string K(string n) => Kenney+"/Models/FBX format/"+n+".fbx";
        static string Y(string n) => Kay+"/Assets/fbx(unity)/"+n+".fbx";
        static string V(string n,string color) => Vehicles+"/Vehicle_"+n+"_color"+color+".prefab";
        static string PathFor(int c,int i) => Root+"/Prefabs/"+Categories[c]+"/OBS_"+(c==0?"FIX":c==1?"NAV":"JMP")+"_"+(i+1).ToString("D2")+"_"+Names[c][i]+".prefab";
        static IEnumerable<string> AllSources() => new[] { "box","box-large","barrel","rock-a","rock-b","fence","fence-fortified" }.Select(K)
            .Concat(new[] { "Stone_Bricks_Stack_Large","Wood_Planks_Stack_Medium","Wood_Log_Stack","Wood_Log_A","Stone_Brick","Stone_Chunks_Small","Textiles_Stack_Small","Iron_Bars_Stack_Small" }.Select(Y))
            .Concat(new[] { V("Car","01"),V("SUV","01"),V("Pick up Truck","01"),V("Car","02"),V("SUV","02"),V("Pick up Truck","02"),V("Truck","02") });
    }
}
