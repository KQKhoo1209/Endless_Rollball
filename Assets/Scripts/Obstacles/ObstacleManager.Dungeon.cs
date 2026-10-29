using System;
using System.Collections.Generic;
using UnityEngine;
namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        private ThemeObstacleLibrary dungeonLibrary;
        private readonly Dictionary<string,int> dungeonCounts = new();
        private void ResetDungeonPlanning() { dungeonLibrary=null; dungeonCounts.Clear(); }
        private void FinishDungeonVisit()
        {
            if(dungeonLibrary==null) return;
            foreach(string family in new[]{"Dungeon.CollapsedPillar","Dungeon.Fire","Dungeon.Rubble","Dungeon.SlidingBlock","Dungeon.PillarJump"})
                if(!dungeonCounts.TryGetValue(family,out int count) || count<3)
                    Debug.LogWarning($"Dungeon visit has insufficient safe capacity for {family}: {count}/3. Run length and safety spacing were preserved.",this);
            ResetDungeonPlanning();
        }
        private bool TryPlanDungeon(TrackSegment road,int index,DifficultySnapshot difficulty,float origin,float limit,ThemeObstacleLibrary library,out SegmentPlan result)
        {
            result=default;
            if(dungeonLibrary!=library) { FinishDungeonVisit(); dungeonLibrary=library; }
            var candidates=new List<GameObject>();
            foreach(var f in library.FixedFamilies) if(f!=null && f.FamilyId=="Dungeon.CollapsedPillar") candidates.AddRange(f.Variants);
            foreach(var trap in library.DungeonTraps) if(trap!=null) candidates.Add(trap.gameObject);
            var random=new System.Random(unchecked(runSeed^index*7919));
            // Seeded tie breaking, then balance successful placements by family.
            for(int i=candidates.Count-1;i>0;i--) {int j=random.Next(i+1);var t=candidates[i];candidates[i]=candidates[j];candidates[j]=t;}
            candidates.RemoveAll(item=>item==null);
            candidates.Sort((a,b)=>Count(a).CompareTo(Count(b)));
            foreach(var source in candidates)
            {
                if(source==null) continue;
                var trap=source.GetComponent<DungeonTrapEncounter>();
                string family=trap!=null?trap.Family:"Dungeon.CollapsedPillar";
                if(family==planner.LastFamily) continue;
                bool jump=trap!=null && trap.RequiredJump;
                Bounds occupied;
                if(trap!=null) occupied=trap.SettledBounds;
                else
                {
                    var shape=source.GetComponent<BoxCollider>();
                    if(shape==null) continue;
                    occupied=new Bounds(shape.center,shape.size);
                }
                if(source.transform.localScale!=Vector3.one || occupied.min.z < -1.5f || occupied.max.z>1.5f) continue;
                float maximum=difficulty.ForwardSpeed+difficulty.SpeedAdjustmentRange;
                float minimum=Mathf.Max(.1f,difficulty.ForwardSpeed-difficulty.SpeedAdjustmentRange);
                float gravity=Mathf.Max(.1f,-Physics.gravity.y), impulse=ballMovement.JumpSpeed;
                if(jump && (occupied.size.x<difficulty.RoadWidth || !trap.CanClearJump(minimum,maximum,impulse,Radius,Time.fixedDeltaTime))) continue;
                var safety=new DifficultySnapshot(difficulty.Mode,maximum,0,1,0,difficulty.Progress01,difficulty.RoadWidth,difficulty.RecoverySeconds,difficulty.ConsecutiveLimit);
                float start=origin+12+occupied.min.z;
                float end=jump?origin+12+occupied.max.z+maximum*2*impulse/gravity+Radius+.5f:origin+15;
                float passage=trap!=null?trap.PassageX:(source.name.Contains("Left")?1.5f:-1.5f);
                if(!jump)
                {
                    float edge=difficulty.RoadWidth*.5f-Radius-.15f;
                    float low=passage>0?occupied.max.x+Radius+.15f:-edge;
                    float high=passage>0?edge:occupied.min.x-Radius-.15f;
                    if(high<low) continue;
                    passage=(low+high)*.5f;
                }
                if(end>limit || !planner.CanReach(index,start,end,passage,safety,LateralSpeed,Acceleration)) continue;
                // Validate at maximum speed rather than the nominal planner speed.
                float approachSeconds=EncounterPlanner.SteeringTime(-passage,passage,LateralSpeed,Acceleration);
                if(start-ballMovement.transform.position.z<maximum*Mathf.Max(approachSeconds,1f)) continue;
                if(trap!=null && (!jump || trap.FallsOnApproach) && start-maximum*(trap.ApproachLeadSeconds+2*Time.fixedDeltaTime)-Radius <= ballMovement.transform.position.z) continue;
                long id=(long)Math.Round((origin+12)*1000);
                var window=new TaskWindow {LocalIndex=0,EntryZ=origin,HazardStartZ=start,LandingStartZ=origin+12+occupied.max.z+Radius+.1f,DeadlineZ=end,RecoveryEndZ=end};
                var placement=new Placement(source,new Vector3(0,0,12),jump,speed:maximum);
                var plan=new EncounterPlan(this,runVersion,index,road,new[]{placement},false,end,passage,6,id,family,new[]{window});
                planner.Commit(index,end,passage,family);
                dungeonCounts[family]=Count(source)+1;
                result=new SegmentPlan(road,new List<EncounterPlan>{plan});
                return true;
            }
            return false;
        }
        private int Count(GameObject source)
        { string family=source.GetComponent<DungeonTrapEncounter>()?.Family??"Dungeon.CollapsedPillar";return dungeonCounts.TryGetValue(family,out int n)?n:0; }
    }
}
