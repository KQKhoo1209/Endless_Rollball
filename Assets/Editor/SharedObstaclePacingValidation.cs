using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EndlessRollball.Track;

// Explicit menu only. Each seed yields back to the Editor; no startup/reload validation.
public static class SharedObstaclePacingValidation
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object Get(object obj,string name) => obj.GetType().GetField(name,Hidden|BindingFlags.Public).GetValue(obj);
    private static void Set(object obj,string name,object value) => obj.GetType().GetField(name,Hidden|BindingFlags.Public).SetValue(obj,value);
    private static object Property(object obj,string name) => obj.GetType().GetProperty(name,Hidden|BindingFlags.Public).GetValue(obj);
    private static object Call(object obj,string name,params object[] args) => obj.GetType().GetMethod(name,Hidden|BindingFlags.Public).Invoke(obj,args);
    private static void Require(bool value,string reason) {if(!value) throw new Exception(reason);}
    private static int job, startJob, lastJob;
    private static bool running;
    private static readonly string[] Themes={"Street","Beach","Dungeon"};
    private static readonly List<float> intervals=new List<float>();
    private static StreamWriter output,details;
    private static GameMode? previousMode;
    private static int warnings;
    private static readonly Dictionary<string,int> coverage=new Dictionary<string,int>();

    [MenuItem("Tools/Endless Rollball/Check Shared Obstacle Pacing")]
    public static void Check() => Start(0, "");
    [MenuItem("Tools/Endless Rollball/Check Dungeon Shared Pacing")]
    public static void CheckDungeon() => Start(480, "dungeon-recheck-");
    [MenuItem("Tools/Endless Rollball/Check City Shared Pacing")]
    public static void CheckCity() => Start(0, "city-recheck-", 240);
    private static void Start(int firstJob,string prefix,int endJob=720)
    {
        if(running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory("Artifacts/SharedPacing");
        output=new StreamWriter("Artifacts/SharedPacing/"+prefix+"summary.csv",false);
        output.WriteLine("theme,gameMode,difficulty,seed,decisions,medianSeconds,longestSeconds,mixedRows,coverageShortfalls");
        details=new StreamWriter("Artifacts/SharedPacing/"+prefix+"decisions.csv",false);
        details.WriteLine("theme,gameMode,difficulty,seed,segment,id,family,prefab,layout,decisionZ,clearZ,recoveryEndZ,passageMin,passageMax,exitX,seconds,targetSeconds,groupSize,reason");
        previousMode=GameSessionSettings.SelectedMode;job=startJob=firstJob;lastJob=endJob;warnings=0;running=true;
        EditorApplication.update+=Tick;
        AssemblyReloadEvents.beforeAssemblyReload+=Abort;
        Debug.Log($"Shared pacing checks started: {endJob-firstJob} routes, 20 seeds x 300 segments per configuration. Editor fixtures, not Play Mode.");
    }
    private static void Tick()
    {
        if(!running) return;
        if(EditorApplication.isPlayingOrWillChangePlaymode) {Finish(false);return;}
        try
        {
            int seed=job%20, difficulty=(job/20)%4, mode=(job/80)%3, theme=job/240;
            if(job>=lastJob) {Finish(true);return;}
            GameSessionSettings.SelectedMode=(GameMode)mode;
            Run(Themes[theme],(DifficultyMode)difficulty,seed);
            job++;
            if(job%20==0) {output.Flush();details.Flush();Debug.Log($"Shared pacing checks: {job}/720 seeded routes completed.");}
        }
        catch(Exception ex) {Debug.LogException(ex.InnerException??ex);Finish(false);}
    }
    [MenuItem("Tools/Endless Rollball/Stop Shared Pacing Checks")]
    public static void Stop() {if(running) Finish(false);}
    private static void Abort() {if(running) Finish(false);}
    private static void Finish(bool passed)
    {
        EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=Abort;running=false;
        output?.Dispose();details?.Dispose();output=details=null;
        GameSessionSettings.SelectedMode=previousMode;
        Debug.Log($"Shared pacing validation {(passed?"PASS":"FAILED")}: {job-startJob} seeded routes x 300 segments; {warnings} capacity/median tuning flags. See Artifacts/SharedPacing. Timing targets are reported separately from structural invariants; Play Mode remains required.");
    }
    private static void Run(string theme,DifficultyMode mode,int seed)
    {
        var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>($"Assets/EndlessRollballAssets/Obstacles/{theme}/{theme}_ObstacleLibrary.asset");
        string roadName=theme=="Street"?"TRK_Street_Road_24m":theme=="Beach"?"TRK_Beach_Road_24m":"TRK_Dungeon_Road_24m";
        GameObject roadObject=null;
        foreach(var guid in AssetDatabase.FindAssets(roadName+" t:Prefab"))
        {var obj=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));if(obj.name==roadName){roadObject=obj;break;}}
        // Authored Beach road is named Water in older layouts.
        if(roadObject==null && theme=="Beach")
            foreach(var guid in AssetDatabase.FindAssets("TRK_Water_Road_24m t:Prefab"))
            {roadObject=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));break;}
        Require(library!=null && roadObject!=null,"Missing library/road: "+theme);
        var profile=AssetDatabase.LoadAssetAtPath<DifficultyProfile>($"Assets/EndlessRollballAssets/Resources/Track/Difficulty_{mode}.asset");
        Require(profile!=null,"Missing profile "+mode);
        var scene=EditorSceneManager.NewPreviewScene();
        var fixture=new GameObject("SharedPacingFixture"){hideFlags=HideFlags.HideAndDontSave};
        SceneManager.MoveGameObjectToScene(fixture,scene);
        ObstacleManager manager=null;
        try
        {
            var player=GameObject.CreatePrimitive(PrimitiveType.Sphere);player.transform.SetParent(fixture.transform,false);
            var ball=player.AddComponent<BallMovement>();ball.enabled=false;
            var root=new GameObject("Active");root.transform.SetParent(fixture.transform,false);
            var pool=new GameObject("Pool");pool.transform.SetParent(fixture.transform,false);
            manager=fixture.AddComponent<ObstacleManager>();
            Set(manager,"activeRoot",root.transform);Set(manager,"poolRoot",pool.transform);Set(manager,"ballMovement",ball);
            Set(manager,"captureDecisionDiagnostics",true);
            Require(manager.BeginRun(seed),"BeginRun failed");
            var requestType=typeof(ObstacleManager).GetNestedType("CityPlanningRequest",BindingFlags.NonPublic);
            var planMethod=typeof(ObstacleManager).GetMethod("PlanSharedSegment",Hidden);
            var history=new List<object>();
            var ids=new HashSet<long>();
            intervals.Clear();coverage.Clear();float longest=0;int mixed=0;
            float entry=240;
            for(int segment=0;segment<300;segment++)
            {
                player.transform.position=new Vector3(0,.5f,entry-144);
                var request=Activator.CreateInstance(requestType);
                Set(request,"Road",roadObject.GetComponent<TrackSegment>());Set(request,"Index",segment+10);Set(request,"Zone",0);
                Set(request,"Origin",entry);Set(request,"Limit",entry+191f);Set(request,"Library",library);
                Set(request,"Difficulty",profile.Evaluate(segment*24));
                Set(request,"Traffic",true);Set(request,"Junction",true);Set(request,"Stops",true);Set(request,"Beach",true);
                var plan=planMethod.Invoke(manager,new[]{request});
                var chosen=(TrackSegment)Property(plan,"Track");
                Require(chosen!=null,"Planner returned no road");
                entry+=chosen.Length;
            }
            var records=(IList)Get(manager,"decisionDiagnostics");
            foreach(var record in records)
            {
                float z=(float)Get(record,"DecisionZ"),clear=(float)Get(record,"ClearZ");
                string family=(string)Get(record,"Family"),prefab=(string)Get(record,"Prefab");
                float seconds=(float)Get(record,"Seconds");
                if(seconds>0) {intervals.Add(seconds);longest=Mathf.Max(longest,seconds);}
                Require(ids.Add((long)Get(record,"Identity")),"Duplicate encounter identity");
                Require((float)Get(record,"PassageMin")<=(float)Get(record,"PassageMax"),"No collider-clear passage");
                Require((float)Get(record,"ExitX")>=(float)Get(record,"PassageMin")-.001f && (float)Get(record,"ExitX")<=(float)Get(record,"PassageMax")+.001f,"Exit outside passage");
                Require(clear>z,"Invalid clearance window");
                if((int)Get(record,"GroupSize")>1) mixed++;
                foreach(var f in family.Split(new[]{" + "},StringSplitOptions.None)) coverage[f]=coverage.TryGetValue(f,out int n)?n+1:1;
                // A prefab cannot occur twice inside 24 m; family frequency is capped at two.
                foreach(var f in family.Split(new[]{" + "},StringSplitOptions.None))
                {
                    int count=1;
                    foreach(var prior in history)
                        if(z-(float)Get(prior,"DecisionZ")<24-.001f)
                        {
                            foreach(var old in ((string)Get(prior,"Family")).Split(new[]{" + "},StringSplitOptions.None)) if(old==f) count++;
                        }
                    Require(count<=2,"Family repeated more than twice in 24 m: "+f);
                }
                foreach(var prior in history)
                    if(z-(float)Get(prior,"DecisionZ")<24-.001f)
                        foreach(var p in prefab.Split(new[]{" + "},StringSplitOptions.None))
                            Require(Array.IndexOf(((string)Get(prior,"Prefab")).Split(new[]{" + "},StringSplitOptions.None),p)<0,"Repeated prefab in 24 m: "+p);
                history.Add(record);while(history.Count>0 && z-(float)Get(history[0],"DecisionZ")>=24) history.RemoveAt(0);
                details.WriteLine(string.Join(",",theme,GameSessionSettings.SelectedMode,mode,seed,Get(record,"Segment"),Get(record,"Identity"),family,prefab,Get(record,"Layout"),
                    F(z),F(clear),F((float)Get(record,"RecoveryEndZ")),F((float)Get(record,"PassageMin")),F((float)Get(record,"PassageMax")),F((float)Get(record,"ExitX")),F(seconds),F((float)Get(record,"TargetSeconds")),Get(record,"GroupSize"),Get(record,"Reason")));
            }
            intervals.Sort();float median=intervals.Count==0?0:intervals[intervals.Count/2];
            var families=(IEnumerable)Call(manager,"SharedFamilies",library);int missing=0;
            foreach(var family in families) {string name=(string)Get(family,"Name");if(!coverage.TryGetValue(name,out int n)||n<3) missing++;}
            if(missing>0 || (mode==DifficultyMode.Easy && Mathf.Abs(median-2)>.2f) || (mode==DifficultyMode.Normal && Mathf.Abs(median-1)>.1f)) warnings++;
            output.WriteLine(string.Join(",",theme,GameSessionSettings.SelectedMode,mode,seed,records.Count,F(median),F(longest),mixed,missing));
            var rejections=(IDictionary)Get(manager,"sharedRejections");
            if(seed==0)
            {
                using(var reasons=new StreamWriter($"Artifacts/SharedPacing/{theme}-{GameSessionSettings.SelectedMode}-{mode}-rejections.txt"))
                {foreach(DictionaryEntry pair in rejections) reasons.WriteLine(pair.Key+": "+pair.Value);foreach(var pair in coverage) reasons.WriteLine(pair.Key+": "+pair.Value);}
            }
        }
        finally
        {
            if(manager!=null) {Set(manager,"sharedLibrary",null);manager.EndRun();}
            UnityEngine.Object.DestroyImmediate(fixture);EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    private static string F(float value) => value.ToString("0.###",CultureInfo.InvariantCulture);

    [MenuItem("Tools/Endless Rollball/Capture Legacy Obstacle Selection Baseline")]
    public static void CaptureBaseline()
    {
        if(running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory("Artifacts/SharedPacing");
        var previous=GameSessionSettings.SelectedMode;
        using(var writer=new StreamWriter("Artifacts/SharedPacing/legacy-selection.csv",false))
        {
            writer.WriteLine("theme,difficulty,encounters,maxSameFamilyWithin24m,maxSameVariantWithin24m");
            try
            {
                GameSessionSettings.SelectedMode=GameMode.Standard;
                foreach(string theme in Themes)
                foreach(DifficultyMode mode in Enum.GetValues(typeof(DifficultyMode)))
                {
                    var library=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>($"Assets/EndlessRollballAssets/Obstacles/{theme}/{theme}_ObstacleLibrary.asset"));
                    var serialized=new SerializedObject(library);serialized.FindProperty("useSharedPacing").boolValue=false;serialized.ApplyModifiedPropertiesWithoutUndo();
                    var road=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_{theme}_Road_24m.prefab").GetComponent<TrackSegment>();
                    var profile=AssetDatabase.LoadAssetAtPath<DifficultyProfile>($"Assets/EndlessRollballAssets/Resources/Track/Difficulty_{mode}.asset");
                    var preview=EditorSceneManager.NewPreviewScene();
                    var fixture=new GameObject("LegacySelectionBaseline"){hideFlags=HideFlags.HideAndDontSave};SceneManager.MoveGameObjectToScene(fixture,preview);
                    ObstacleManager manager=null;
                    try
                    {
                        var player=GameObject.CreatePrimitive(PrimitiveType.Sphere);player.transform.SetParent(fixture.transform,false);
                        var ball=player.AddComponent<BallMovement>();ball.enabled=false;
                        var active=new GameObject("Active");active.transform.SetParent(fixture.transform,false);
                        var pool=new GameObject("Pool");pool.transform.SetParent(fixture.transform,false);
                        manager=fixture.AddComponent<ObstacleManager>();Set(manager,"activeRoot",active.transform);Set(manager,"poolRoot",pool.transform);Set(manager,"ballMovement",ball);
                        Require(manager.BeginRun(71),"Legacy baseline initialization failed");
                        var recent=new List<Tuple<float,string,string>>();int total=0,maxFamily=0,maxVariant=0;
                        for(int segment=0;segment<300;segment++)
                        {
                            float origin=240+24*segment;player.transform.position=new Vector3(0,.5f,origin-144);
                            var plan=Call(manager,"PlanSegment",road,null,segment+10,false,profile.Evaluate(segment*24),origin,origin+191,library,true,true,true,true);
                            var encounters=(IEnumerable)Property(plan,"Encounters");
                            if(encounters==null) continue;
                            foreach(var encounter in encounters)
                            {
                                string family=(string)Property(encounter,"Family");
                                var placements=(IEnumerable)Property(encounter,"Placements");
                                string variant="Gap";foreach(var placement in placements){variant=((GameObject)Property(placement,"Prefab")).name;break;}
                                var windows=(Array)Property(encounter,"Windows");if(windows.Length==0)continue;
                                float z=(float)Get(windows.GetValue(0),"HazardStartZ");int f=1,v=1;
                                recent.RemoveAll(item=>z-item.Item1>=24);
                                foreach(var item in recent){if(item.Item2==family)f++;if(item.Item3==variant)v++;}
                                maxFamily=Mathf.Max(maxFamily,f);maxVariant=Mathf.Max(maxVariant,v);recent.Add(Tuple.Create(z,family,variant));total++;
                            }
                            var specialWindow=Property(plan,"VehicleWindow")??Property(plan,"JunctionWindow")??Property(plan,"RoadworksWindow");
                            if(specialWindow!=null)
                            {
                                var vehicle=Property(plan,"CityVehicle") as CityVehicleEncounter;
                                var beach=Property(plan,"BeachBarrier") as BeachBarrierEncounter;
                                var works=Property(plan,"Roadworks") as RoadworksTrackEncounter;
                                string family=vehicle!=null?vehicle.FamilyId:beach!=null?beach.Family:works!=null?works.FamilyId:"Street.Junction";
                                var sourceVehicle=Property(plan,"VehiclePrefab") as GameObject;
                                string variant=sourceVehicle!=null?sourceVehicle.name:((TrackSegment)Property(plan,"Track")).name;
                                float z=(float)Get(specialWindow,"HazardStartZ");int f=1,v=1;
                                recent.RemoveAll(item=>z-item.Item1>=24);
                                foreach(var item in recent){if(item.Item2==family)f++;if(item.Item3==variant)v++;}
                                maxFamily=Mathf.Max(maxFamily,f);maxVariant=Mathf.Max(maxVariant,v);recent.Add(Tuple.Create(z,family,variant));total++;
                            }
                        }
                        writer.WriteLine(string.Join(",",theme,mode,total,maxFamily,maxVariant));
                    }
                    finally {if(manager!=null) {Call(manager,"ResetDungeonPlanning");manager.EndRun();}UnityEngine.Object.DestroyImmediate(fixture);EditorSceneManager.ClosePreviewScene(preview);UnityEngine.Object.DestroyImmediate(library);}
                }
                Debug.Log("Legacy selection baseline captured in Artifacts/SharedPacing/legacy-selection.csv. Direct legacy selection fixture, not a replay of the reported six-copy scene or the generator's historical traffic lookahead.");
            }
            finally {GameSessionSettings.SelectedMode=previous;}
        }
    }

    [MenuItem("Tools/Endless Rollball/Check Shared Pacing Ownership And Recovery")]
    public static void CheckIntegration()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene=EditorSceneManager.NewPreviewScene();
        var fixture=new GameObject("SharedPacingOwnershipFixture"){hideFlags=HideFlags.HideAndDontSave};
        SceneManager.MoveGameObjectToScene(fixture,scene);
        ObstacleManager manager=null;
        try
        {
            var player=GameObject.CreatePrimitive(PrimitiveType.Sphere);player.transform.SetParent(fixture.transform,false);
            var ball=player.AddComponent<BallMovement>();ball.enabled=false;
            var root=new GameObject("Active");root.transform.SetParent(fixture.transform,false);
            var pool=new GameObject("Pool");pool.transform.SetParent(fixture.transform,false);
            manager=fixture.AddComponent<ObstacleManager>();
            var tracker=fixture.AddComponent<SessionTaskTracker>();Set(tracker,"ballMovement",ball);
            Set(manager,"activeRoot",root.transform);Set(manager,"poolRoot",pool.transform);Set(manager,"ballMovement",ball);Set(manager,"taskTracker",tracker);
            var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>("Assets/EndlessRollballAssets/Obstacles/Street/Street_ObstacleLibrary.asset");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Street_Road_24m.prefab");
            Require(manager.BeginRun(71),"BeginRun failed");
            player.transform.position=new Vector3(0,.5f,96);
            var requestType=typeof(ObstacleManager).GetNestedType("CityPlanningRequest",BindingFlags.NonPublic);
            var request=Activator.CreateInstance(requestType);
            Set(request,"Road",source.GetComponent<TrackSegment>());Set(request,"Index",10);Set(request,"Zone",0);
            Set(request,"Origin",240f);Set(request,"Limit",431f);Set(request,"Library",library);
            Set(request,"Difficulty",new DifficultySnapshot(DifficultyMode.Normal,7.5f,3.5f,1,0,0,6));
            var plan=Call(manager,"PlanSharedSegment",request);
            var track=UnityEngine.Object.Instantiate(source,fixture.transform).GetComponent<TrackSegment>();track.transform.position=Vector3.forward*240;
            Call(tracker,"RegisterTrack",track);
            Call(manager,"AttachSegment",track,plan,null,null);
            var tasks=(IList)Get(tracker,"opportunities");int taskCount=tasks.Count;
            var active=(IDictionary)Get(manager,"active");int payloadCount=((IList)active[track]).Count;
            Require(taskCount>0 && payloadCount>0,"No ordinary shared payloads registered");
            Call(manager,"AttachSegment",track,plan,null,null);
            Require(tasks.Count==taskCount && ((IList)active[track]).Count==payloadCount,"Duplicate attachment created payloads/tasks");
            Require(!(bool)Property(tracker,"HasRegistrationErrors"),"Scoring registration error");
            manager.SetMotionPaused(true);manager.SetMotionPaused(false);
            Physics.SyncTransforms();
            object[] recovery={240f,1000f,null,true,10f};
            Require((bool)typeof(SessionTaskTracker).GetMethod("TryGetRecoveryPoint",Hidden).Invoke(tracker,recovery),"Dense encounters prevented supported recovery");
            var point=(Vector3)recovery[2];
            Call(tracker,"PrepareRecoveryArrival",point.z,point.z+10f);
            Call(manager,"ProtectRecoveryArrival",point.z,point.z+10f);
            manager.Release(track);Call(tracker,"ReleaseTrack",track);
            Require(!active.Contains(track),"Released track retained encounter ownership");
            Call(tracker,"ResetRoute");Call(tracker,"RegisterTrack",track);
            Call(manager,"AttachSegment",track,plan,null,null);
            Require(((IList)Get(tracker,"opportunities")).Count==taskCount,"Pooled reuse changed scoring count");
            Require(((IList)active[track]).Count==payloadCount,"Pooled reuse changed payload count");
            foreach(var trap in AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>("Assets/EndlessRollballAssets/Obstacles/Dungeon/Dungeon_ObstacleLibrary.asset").DungeonTraps)
            {
                if(trap.Kind!=DungeonTrapEncounter.TrapKind.Fire) continue;
                bool side=trap.name.EndsWith("Left") || trap.name.EndsWith("Right");
                Require(trap.RequiredJump!=side,"Side/full-width fire scoring classification incorrect");
                var instance=UnityEngine.Object.Instantiate(trap,fixture.transform);
                instance.Prepare(ball,13,false);var position=instance.Hazard.transform.position;
                for(int i=0;i<20;i++) Call(instance,"FixedUpdate");
                Require(instance.Hazard.transform.position==position,"Navigation fire unexpectedly moved");
                instance.SetRunning(false);Require(!(bool)Get(instance,"running"),"Fire pause failed");
                UnityEngine.Object.DestroyImmediate(instance.gameObject);
            }
            var stoneSource=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EndlessRollballAssets/Obstacles/Dungeon/Prefabs/RollingStoneBall.prefab");
            var stone=UnityEngine.Object.Instantiate(stoneSource,fixture.transform).GetComponent<DungeonRollingStoneBall>();
            stone.transform.position=new Vector3(0,0,300);player.transform.position=new Vector3(-2.3f,.5f,300);
            stone.Prepare(ball,9,0,false,null);stone.SetPlannedPassage(-2.3f);Call(stone,"FixedUpdate");
            Require(((Vector3)Get(stone,"previousPosition")).x>.99f,"Rolling stone did not preserve its planned passage");
            stone.gameObject.SetActive(false);stone.gameObject.SetActive(true);stone.Prepare(ball,9,0,true,null);
            Require((float)Get(stone,"plannedPassSide")==0,"Pooled stone retained a previous planned passage");
            UnityEngine.Object.DestroyImmediate(stone.gameObject);
            Set(manager,"sharedLibrary",null);
            Require(manager.BeginRun(71),"Retry reset failed");
            Require(((IList)Get(manager,"sharedHistory")).Count==0 && ((IDictionary)Get(manager,"sharedLookahead")).Count==0,"Retry retained scheduling state");
            foreach(string theme in Themes)
            {
                var themeLibrary=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>($"Assets/EndlessRollballAssets/Obstacles/{theme}/{theme}_ObstacleLibrary.asset");
                var themeRoad=AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_{theme}_Road_24m.prefab").GetComponent<TrackSegment>();
                string firstLayout=null;
                for(int playback=0;playback<2;playback++)
                {
                    Set(manager,"sharedLibrary",null);manager.BeginRun(123);Set(manager,"captureDecisionDiagnostics",true);
                    float entry=480;
                    for(int segment=0;segment<8;segment++)
                    {
                        player.transform.position=new Vector3(0,.5f,entry-(playback==0?144:3000));
                        var r=Activator.CreateInstance(requestType);
                        Set(r,"Road",themeRoad);Set(r,"Index",segment+20);Set(r,"Zone",0);Set(r,"Origin",entry);Set(r,"Limit",entry+191);
                        Set(r,"Library",themeLibrary);Set(r,"Difficulty",new DifficultySnapshot(DifficultyMode.Normal,7.5f,3.5f,1,0,0,6));
                        Set(r,"Traffic",true);Set(r,"Junction",true);Set(r,"Stops",true);Set(r,"Beach",true);
                        var selected=Call(manager,"PlanSharedSegment",r);entry+=((TrackSegment)Property(selected,"Track")).Length;
                    }
                    string layout="";
                    foreach(var d in (IList)Get(manager,"decisionDiagnostics")) layout+=Get(d,"Family")+":"+Get(d,"Prefab")+":"+Get(d,"DecisionZ")+";";
                    if(playback==0) firstLayout=layout;else Require(layout==firstLayout,"Seeded layout depended on live preparation distance: "+theme);
                }
            }
            Debug.Log("Shared pacing integration PASS: attachment is idempotent, one task per ordinary encounter, dense supported recovery, pool release/reuse, retry reset, side-fire navigation and full-width jump classification, stationary fire and pause, rolling safe-side reset, and deterministic layouts at different preparation distances. Editor fixture; Play Mode remains required.");
        }
        finally
        {
            if(manager!=null) {Set(manager,"sharedLibrary",null);manager.EndRun();}
            UnityEngine.Object.DestroyImmediate(fixture);EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
