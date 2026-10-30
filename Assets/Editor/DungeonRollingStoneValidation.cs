using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EndlessRollball.Track;

public static class DungeonRollingStoneValidation
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static object Field(object target,string name) => target.GetType().GetField(name,Hidden).GetValue(target);
    private static void Set(object target,string name,object value) => target.GetType().GetField(name,Hidden).SetValue(target,value);
    private static object Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,Hidden).Invoke(target,args);
    private static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}

    [MenuItem("Tools/Endless Rollball/Check Dungeon Rolling Stone Integration")]
    public static void Check()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        const string folder="Assets/EndlessRollballAssets/Obstacles/Dungeon/";
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(folder+"Prefabs/RollingStoneBall.prefab");
        var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>(folder+"Dungeon_ObstacleLibrary.asset");
        var road=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Dungeon_Road_24m.prefab");
        Require(source!=null && library!=null && road!=null,"Missing rolling stone/library/road.");
        var authored=source.GetComponent<DungeonRollingStoneBall>();
        Require(authored!=null && Array.Exists(library.RollingStoneBalls,item=>item==authored),"Rolling stone is not assigned to its dedicated library selection.");
        Require(!Array.Exists(library.DungeonTraps,item=>item!=null && item.Kind==DungeonTrapEncounter.TrapKind.SlidingBlock),"Sliding blocks remain in the Dungeon selection.");
        var preview=EditorSceneManager.NewPreviewScene();
        var fixture=new GameObject("DungeonRollingStoneValidation") {hideFlags=HideFlags.HideAndDontSave};
        SceneManager.MoveGameObjectToScene(fixture,preview);
        var settingsCopy=UnityEngine.Object.Instantiate(source,fixture.transform);
        settingsCopy.SetActive(false);
        var testSource=settingsCopy.GetComponent<DungeonRollingStoneBall>();
        var rollingOnly=ScriptableObject.CreateInstance<ThemeObstacleLibrary>();
        var serialized=new SerializedObject(rollingOnly);var array=serialized.FindProperty("rollingStoneBalls");array.arraySize=1;array.GetArrayElementAtIndex(0).objectReferenceValue=testSource;serialized.ApplyModifiedPropertiesWithoutUndo();
        ObstacleManager manager=null;
        var previousMode=GameSessionSettings.SelectedMode;
        try
        {
            var player=GameObject.CreatePrimitive(PrimitiveType.Sphere);player.transform.SetParent(fixture.transform,false);
            var ball=player.AddComponent<BallMovement>();ball.enabled=false;
            var tracker=fixture.AddComponent<SessionTaskTracker>();
            var controller=fixture.AddComponent<GameplayRunController>();controller.enabled=false;
            typeof(GameplayRunController).GetField("<State>k__BackingField",Hidden).SetValue(controller,SessionFlowState.Running);
            Set(tracker,"ballMovement",ball);Set(tracker,"runController",controller);
            manager=fixture.AddComponent<ObstacleManager>();
            var activeRoot=new GameObject("Active");activeRoot.transform.SetParent(fixture.transform,false);
            var poolRoot=new GameObject("Pool");poolRoot.transform.SetParent(fixture.transform,false);
            Set(manager,"activeRoot",activeRoot.transform);Set(manager,"poolRoot",poolRoot.transform);Set(manager,"ballMovement",ball);Set(manager,"taskTracker",tracker);
            Require(manager.BeginRun(31415),"Manager initialization failed.");
            var trackObject=UnityEngine.Object.Instantiate(road,fixture.transform);trackObject.transform.position=new Vector3(0,0,240);
            var track=trackObject.GetComponent<TrackSegment>();
            Call(tracker,"RegisterTrack",track);
            player.transform.position=new Vector3(0,.5f,132);
            object[] planning={road.GetComponent<TrackSegment>(),10,new DifficultySnapshot(DifficultyMode.Normal,7.5f,3.5f,1,0,0,6),240f,311f,rollingOnly,null};
            Require((bool)typeof(ObstacleManager).GetMethod("TryPlanDungeon",Hidden).Invoke(manager,planning),"Rolling stone was not planned.");
            var plans=(IEnumerable)planning[6].GetType().GetProperty("Encounters").GetValue(planning[6]);
            object plan=null;foreach(var item in plans){plan=item;break;}
            Call(manager,"AttachPlanned",track,plan,null);
            Call(manager,"AttachPlanned",track,plan,null);
            Require(!(bool)typeof(SessionTaskTracker).GetProperty("HasRegistrationErrors",Hidden).GetValue(tracker),"Sphere scoring registration failed.");
            var tasks=(IList)Field(tracker,"opportunities");Require(tasks.Count==1,"Encounter registered more than once.");
            var active=(IDictionary)Field(manager,"active");var pairs=(IList)active[track];
            object pair=pairs[0];var rolling=(DungeonRollingStoneBall)pair.GetType().GetField("RollingStone").GetValue(pair);
            Require(rolling!=null && manager.OwnsActiveCollider(rolling.Hazard),"Manager does not own the active SphereCollider.");
            manager.SetMotionPaused(false);
            Vector3 first=(Vector3)Field(rolling,"previousPosition");
            player.transform.position+=Vector3.forward*.3f;
            Call(rolling,"FixedUpdate");
            Vector3 moved=(Vector3)Field(rolling,"previousPosition");
            Require(moved.z<first.z && Mathf.Abs(moved.x-first.x)>.00001f,"Stone did not approach and weave immediately while the player was distant.");
            manager.SetMotionPaused(true);float elapsed=(float)Field(rolling,"elapsed");
            for(int i=0;i<20;i++)Call(rolling,"FixedUpdate");
            Require((float)Field(rolling,"elapsed")==elapsed && (Vector3)Field(rolling,"previousPosition")==moved,"Paused stone advanced.");
            manager.SetMotionPaused(false);
            player.transform.position=new Vector3(0,.5f,251);
            Call(rolling,"FixedUpdate");
            float reached=(float)Field(rolling,"greatestProgress");
            player.transform.position-=Vector3.forward*30;Call(rolling,"FixedUpdate");
            Require((float)Field(rolling,"greatestProgress")==reached,"Movement rewound after backward progress.");
            player.transform.position=new Vector3(0,.5f,251);
            tracker.BeginSession("rolling-failure");
            Call(tracker,"RecordFailure",SessionOutcome.Collision,rolling.Hazard);
            Call(tracker,"RecordFailure",SessionOutcome.Collision,rolling.Hazard);
            Require(tracker.CurrentScore.SteeringTaskCount==1 && tracker.CurrentScore.SuccessfulSteeringTaskCount==0 && tracker.CurrentScore.RequiredJumpOpportunityCount==0,"Repeated collision was not one navigation failure.");
            tracker.BeginSession("rolling-success");
            object task=tasks[0];float landing=(float)task.GetType().GetField("LandingStartZ").GetValue(task);
            player.transform.position=new Vector3(0,.5f,landing-1);
            Call(tracker,"Enter",task);
            var floors=(IDictionary)Field(tracker,"trackFloors");var floor=(Collider)((IList)floors[track])[0];
            Call(tracker,"ObserveSupportedPass",task,floor);
            Require(tracker.CurrentScore.SuccessfulSteeringTaskCount==0,"Navigation succeeded before the clearance window.");
            player.transform.position=new Vector3(0,.5f,landing+.1f);Call(rolling,"FixedUpdate");
            Call(tracker,"ObserveSupportedPass",task,floor);Call(tracker,"ObserveSupportedPass",task,floor);
            Require(tracker.CurrentScore.SuccessfulSteeringTaskCount==1 && tracker.CurrentScore.SteeringTaskCount==1,"Supported passage did not score once.");
            Require(!rolling.Hazard.enabled,"Passed stone retained its hazardous collider.");
            manager.Release(track);
            Require(!manager.OwnsActiveCollider(rolling.Hazard),"Released stone remained owned.");
            float updatedDistance=testSource.SidewaysDistance+.75f;
            Set(testSource,"sidewaysDistance",updatedDistance);
            Call(tracker,"ResetRoute");Call(tracker,"RegisterTrack",track);
            player.transform.position=new Vector3(0,.5f,132);
            Call(manager,"AttachPlanned",track,plan,null);
            var reusedPairs=(IList)((IDictionary)Field(manager,"active"))[track];
            var reused=(DungeonRollingStoneBall)reusedPairs[0].GetType().GetField("RollingStone").GetValue(reusedPairs[0]);
            Require(ReferenceEquals(reused,rolling) && (float)Field(reused,"elapsed")==0 && reused.Hazard.enabled,"Pool reuse failed to restore the same stone and collider.");
            Require(Mathf.Approximately(reused.SidewaysDistance,updatedDistance),"Pooled stone retained the old prefab Sideways Distance.");
            manager.SetMotionPaused(false);
            float minimumX=float.PositiveInfinity,maximumX=float.NegativeInfinity;
            for(int frame=0;frame<240;frame++)
            {
                Call(reused,"FixedUpdate");
                float x=((Vector3)Field(reused,"previousPosition")).x;
                minimumX=Mathf.Min(minimumX,x);maximumX=Mathf.Max(maximumX,x);
            }
            Require(Mathf.Abs(minimumX+updatedDistance)<.02f && Mathf.Abs(maximumX-updatedDistance)<.02f,"Updated distance did not produce the full requested lateral range.");
            Require(((IList)Field(tracker,"opportunities")).Count==1,"Reuse duplicated scoring registration.");

            int scheduled=0;
            foreach(var mode in new[]{GameMode.Standard,GameMode.FastPaced,GameMode.Endless})
            foreach(var difficulty in new[]{DifficultyMode.Easy,DifficultyMode.Normal,DifficultyMode.Hard,DifficultyMode.Endless})
            for(int seed=0;seed<20;seed++)
            {
                GameSessionSettings.SelectedMode=mode;
                Call(manager,"ResetDungeonPlanning");Field(manager,"planner").GetType().GetMethod("Reset").Invoke(Field(manager,"planner"),null);
                Set(manager,"runSeed",seed);
                float speed=difficulty==DifficultyMode.Easy?6:difficulty==DifficultyMode.Hard?9:7.5f;
                player.transform.position=new Vector3(0,.5f,132);
                planning[2]=new DifficultySnapshot(difficulty,speed,3,1,0,0,6);
                Require((bool)typeof(ObstacleManager).GetMethod("TryPlanDungeon",Hidden).Invoke(manager,planning),"Missing rolling selection for "+mode+"/"+difficulty);
                scheduled++;
            }
            Debug.Log($"Dungeon rolling stone integration PASS: {scheduled} seeded mode/difficulty selections; early approach/weave, pause, monotonic progress, active ownership, SphereCollider scoring, one failure/success, no premature success, passed collider disable, same-instance pooled reset, refreshed prefab settings and full updated lateral range. Editor fixture; Play Mode visibility, collision response and gameplay feel remain unverified.");
        }
        finally
        {
            GameSessionSettings.SelectedMode=previousMode;
            if(manager!=null)Call(manager,"ResetDungeonPlanning");
            UnityEngine.Object.DestroyImmediate(fixture);UnityEngine.Object.DestroyImmediate(rollingOnly);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
