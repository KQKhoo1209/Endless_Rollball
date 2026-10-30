using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using EndlessRollball.Track;
public static class DungeonObstacleValidation
{
    [MenuItem("Tools/Endless Rollball/Check Dungeon Obstacles")]
    public static void Check()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var library=AssetDatabase.LoadAssetAtPath<ThemeObstacleLibrary>("Assets/EndlessRollballAssets/Obstacles/Dungeon/Dungeon_ObstacleLibrary.asset");
        if(library==null) { EditorApplication.delayCall+=Check; return; }
        if(!Array.Exists(library.DungeonTraps,trap=>trap!=null && trap.Kind==DungeonTrapEncounter.TrapKind.PillarJump))
        { EditorApplication.delayCall+=Check; return; }
        if(!Array.Exists(library.DungeonTraps,trap=>trap!=null && trap.Kind==DungeonTrapEncounter.TrapKind.PillarCollapse && trap.FallsOnApproach))
        { EditorApplication.delayCall+=Check; return; }
        var road=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EndlessRollballAssets/Environment Prefab/Roads/TRK_Dungeon_Road_24m.prefab").GetComponent<TrackSegment>();
        var fixture=new GameObject("DungeonValidation") {hideFlags=HideFlags.HideAndDontSave};
        var previousMode=GameSessionSettings.SelectedMode;
        try
        {
            var ball=fixture.AddComponent<BallMovement>();ball.enabled=false;
            var manager=fixture.AddComponent<ObstacleManager>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(ObstacleManager).GetField("ballMovement",flags).SetValue(manager,ball);
            var method=typeof(ObstacleManager).GetMethod("TryPlanDungeon",flags);
            var reset=typeof(ObstacleManager).GetMethod("ResetDungeonPlanning",flags);
            var planner=typeof(ObstacleManager).GetField("planner",flags).GetValue(manager);
            int runs=0;
            foreach(var gameMode in new[]{GameMode.Standard,GameMode.FastPaced,GameMode.Endless})
            foreach(var mode in new[]{DifficultyMode.Easy,DifficultyMode.Normal,DifficultyMode.Hard,DifficultyMode.Endless})
            for(int seed=0;seed<20;seed++)
            {
                GameSessionSettings.SelectedMode=gameMode;
                reset.Invoke(manager,null);planner.GetType().GetMethod("Reset").Invoke(planner,null);
                typeof(ObstacleManager).GetField("runSeed",flags).SetValue(manager,seed);
                var counts=new System.Collections.Generic.Dictionary<string,int>();
                DifficultyProfile profile=null;
                foreach(string guid in AssetDatabase.FindAssets("t:DifficultyProfile"))
                {var candidate=AssetDatabase.LoadAssetAtPath<DifficultyProfile>(AssetDatabase.GUIDToAssetPath(guid));if(candidate.Mode==mode) {profile=candidate;break;}}
                for(int step=0;step<300;step++)
                {
                    float origin=(step+10)*24;fixture.transform.position=new Vector3(0,.5f,origin-100);
                    float speed=mode==DifficultyMode.Hard?9:mode==DifficultyMode.Normal?7.5f:mode==DifficultyMode.Endless?7:6;
                    float range=mode==DifficultyMode.Hard?4:mode==DifficultyMode.Easy?3:3.5f;
                    object[] args={road,step+10,profile!=null?profile.Evaluate(step*24):new DifficultySnapshot(mode,speed,range,1,0,step/299f),origin,origin+71,library,null};
                    if(!(bool)method.Invoke(manager,args)) continue;
                    var plans=(System.Collections.IEnumerable)args[6].GetType().GetProperty("Encounters").GetValue(args[6]);
                    foreach(var plan in plans)
                    {
                        string family=(string)plan.GetType().GetProperty("Family",flags|BindingFlags.Public).GetValue(plan);
                        counts[family]=counts.TryGetValue(family,out int n)?n+1:1;
                    }
                }
                if(counts.Count!=5) throw new Exception($"Missing Dungeon family: {mode} seed {seed}");
                foreach(var pair in counts) if(pair.Value<3) throw new Exception("Dungeon minimum coverage failed: "+pair.Key);
                runs++;
            }
            foreach(var source in library.DungeonTraps)
            {
                if(source.Hazard==null || !source.Hazard.GetComponent<Rigidbody>().isKinematic) throw new Exception("Invalid Dungeon hazard body.");
                if(source.Kind==DungeonTrapEncounter.TrapKind.Fire && !source.Hazard.isTrigger) throw new Exception("Fire hazard must be a trigger.");
                var instance=UnityEngine.Object.Instantiate(source);
                try
                {
                    instance.Prepare(ball,17,true);
                    var tick=typeof(DungeonTrapEncounter).GetMethod("FixedUpdate",flags);
                    for(int i=0;i<200;i++) tick.Invoke(instance,null);
                    if((float)typeof(DungeonTrapEncounter).GetField("elapsed",flags).GetValue(instance)!=0) throw new Exception("Paused Dungeon trap advanced.");
                    if(instance.FallsOnApproach)
                    {
                        Vector3 previous=fixture.transform.position;
                        fixture.transform.position=instance.transform.position-Vector3.forward*200;
                        instance.Prepare(ball,17,false);
                        for(int i=0;i<200;i++) tick.Invoke(instance,null);
                        if((bool)typeof(DungeonTrapEncounter).GetField("started",flags).GetValue(instance)) throw new Exception("Pillar fell while player was distant.");
                        float gate=(float)typeof(DungeonTrapEncounter).GetField("triggerZ",flags).GetValue(instance);
                        fixture.transform.position=new Vector3(0,.5f,gate+.1f);
                        tick.Invoke(instance,null);
                        if(!(bool)typeof(DungeonTrapEncounter).GetField("started",flags).GetValue(instance)) throw new Exception("Pillar did not trigger on approach.");
                        fixture.transform.position=previous;
                    }
                    typeof(DungeonTrapEncounter).GetField("elapsed",flags).SetValue(instance,9f);
                    instance.gameObject.SetActive(false);instance.gameObject.SetActive(true);
                    instance.Prepare(ball,17,true);
                    if((float)typeof(DungeonTrapEncounter).GetField("elapsed",flags).GetValue(instance)!=0 || !instance.Hazard.enabled) throw new Exception("Dungeon pooled reset failed.");
                }
                finally { UnityEngine.Object.DestroyImmediate(instance.gameObject); }
            }
            reset.Invoke(manager,null);
            Debug.Log($"Dungeon obstacle validation PASS: {runs} seeded runs x 300 compatible segments; configured families >=3; fire trigger, pause and reset contracts valid. Use Check Dungeon Rolling Stone Integration for rolling-stone ownership and scoring. Fixture excludes actual transitions, short visits, scenery overlap and Play Mode.");
        }
        finally { GameSessionSettings.SelectedMode=previousMode; UnityEngine.Object.DestroyImmediate(fixture); }
    }
}


