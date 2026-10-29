using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;
[InitializeOnLoad]
public static class BeachBarrierSpawnRegression
{
    static BeachBarrierSpawnRegression() { EditorApplication.delayCall+=Check; }
    [MenuItem("Tools/Endless Rollball/Check Beach Barrier Spawn Regression")]
    public static void Check()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>("Assets/EndlessRollballAssets/Obstacles/Beach/Beach_ObstacleLibrary.asset");
        var road=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Beach_Road_24m.prefab").GetComponent<TrackSegment>();
        var fixture=new GameObject("BeachSpawnRegression") {hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var ball=fixture.AddComponent<BallMovement>(); ball.enabled=false;fixture.transform.position=new Vector3(0,.5f,100);
            var manager=fixture.AddComponent<ObstacleManager>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(ObstacleManager).GetField("ballMovement",flags).SetValue(manager,ball);
            object planner=typeof(ObstacleManager).GetField("planner",flags).GetValue(manager);
            var method=typeof(ObstacleManager).GetMethod("TryPlanBeachBarrier",flags);
            int immediate=0, ahead=0;
            for(int seed=0;seed<100;seed++) foreach(bool future in new[]{false,true})
            {
                planner.GetType().GetMethod("Reset").Invoke(planner,null);
                planner.GetType().GetMethod("Commit").Invoke(planner,new object[]{9,236f,0f,"Beach.WoodenFence"});
                typeof(ObstacleManager).GetField("runSeed",flags).SetValue(manager,seed);
                float origin=future?264f:240f;
                object[] args={road,future?11:10,new DifficultySnapshot(DifficultyMode.Easy,6,3,1,0,0),origin,origin+72,library,null,false,null};
                bool accepted=(bool)method.Invoke(manager,args);
                if(accepted) {if(future)ahead++;else immediate++;}
            }
            Debug.Log($"Beach spawn regression: crowded current slot {immediate}/100; next compatible slot {ahead}/100. Existing recovery protection must remain; lookahead must make the next slot eligible.");
            Debug.Assert(ahead>0,"No Beach barriers can be planned even with compatible lookahead.");
            var requestType=typeof(ObstacleManager).GetNestedType("BeachPlanningRequest",BindingFlags.NonPublic);
            var listType=typeof(System.Collections.Generic.List<>).MakeGenericType(requestType);
            var window=typeof(ObstacleManager).GetMethod("PlanBeachWindow",flags|BindingFlags.Public);
            for(int speed=3;speed<=17;speed++)
            {
                float trigger=BeachBarrierEncounter.ExplosionTriggerLocalZ(speed,6f,.5f);
                float takeoff=12f-.5f-speed*6f/(-Physics.gravity.y);
                if((takeoff-trigger)/speed<2f) throw new Exception("Explosion warning too short.");
                if(-100f>=trigger) throw new Exception("Barrel explodes at far spawn distance.");
            }
            if(BeachBarrierEncounter.RollingAngle(1f,.3f)>=0f) throw new Exception("Wooden roller rotation is reversed.");
            Debug.Log("Beach barrier timing PASS: 15 speed cases retain two-second takeoff warning; distant spawn does not meet detonation gate; rotation matches negative-Z rolling.");
            int runs=0;
            foreach(var mode in new[]{DifficultyMode.Easy,DifficultyMode.Normal,DifficultyMode.Hard,DifficultyMode.Endless})
            for(int seed=0;seed<20;seed++)
            {
                typeof(ObstacleManager).GetMethod("ResetBeachPlanning",flags).Invoke(manager,null);
                planner.GetType().GetMethod("Reset").Invoke(planner,null);
                typeof(ObstacleManager).GetField("ready",flags).SetValue(manager,true);
                typeof(ObstacleManager).GetField("runSeed",flags).SetValue(manager,seed);
                int wood=0, fuel=0;
                float speed=mode==DifficultyMode.Hard?9:mode==DifficultyMode.Normal?7.5f:mode==DifficultyMode.Endless?7:6;
                float range=mode==DifficultyMode.Hard?4:mode==DifficultyMode.Easy?3:3.5f;
                for(int step=0;step<30;step++)
                {
                    int index=10+step;float origin=index*24;fixture.transform.position=new Vector3(0,.5f,origin-100);
                    var list=(System.Collections.IList)Activator.CreateInstance(listType);
                    for(int offset=0;offset<3 && step+offset<30;offset++)
                    {
                        var request=Activator.CreateInstance(requestType,true);
                        void Set(string field,object value)=>requestType.GetField(field,flags|BindingFlags.Public).SetValue(request,value);
                        Set("Road",road);Set("Index",index+offset);Set("Zone",0);Set("Allowed",true);Set("Origin",origin+offset*24);
                        Set("Limit",origin+offset*24+71);Set("Library",library);Set("Difficulty",new DifficultySnapshot(mode,speed,range,1,0,0));list.Add(request);
                    }
                    object[] args={list,null};bool handled=(bool)window.Invoke(manager,args);
                    if(handled && args[1]!=null)
                    {
                        object barrier=args[1].GetType().GetProperty("BeachBarrier").GetValue(args[1]);
                        if(barrier is BeachBarrierEncounter encounter) {if(encounter.Explosive)fuel++;else wood++;}
                    }
                    // Competing ordinary obstacles fill a slot only when the scheduler did not reserve it.
                    if(!handled) planner.GetType().GetMethod("Commit").Invoke(planner,new object[]{index,origin+20,0f,"Beach.WoodenFence"});
                }
                if(wood<3 || fuel<3) throw new Exception($"Beach coverage regression: {mode} seed {seed}, wood={wood}, fuel={fuel}");
                runs++;
            }
            Debug.Log($"Beach lookahead regression PASS: {runs} runs x 30 segments; both barrier families >=3 with competing fences. This fixture excludes transitions and does not verify Play Mode.");
        }
        finally {UnityEngine.Object.DestroyImmediate(fixture);}
    }
}
