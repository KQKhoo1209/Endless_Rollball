using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        private sealed class DungeonCandidate
        {
            public GameObject Source;
            public DungeonTrapEncounter Trap;
            public DungeonRollingStoneBall Rolling;
            public string Family;
        }
        private ThemeObstacleLibrary dungeonLibrary;
        private readonly Dictionary<string,int> dungeonCounts = new();
        private readonly List<DungeonCandidate> dungeonCandidates = new(16);
        private readonly List<string> dungeonFamilies = new(5);
        private void ResetDungeonPlanning()
        {
            dungeonLibrary=null;dungeonCounts.Clear();dungeonCandidates.Clear();dungeonFamilies.Clear();
        }
        private void FinishDungeonVisit()
        {
            if(dungeonLibrary==null) return;
            foreach(string family in dungeonFamilies)
                if(!dungeonCounts.TryGetValue(family,out int count) || count<3)
                    Debug.LogWarning($"Dungeon visit has insufficient safe capacity for {family}: {count}/3. Run length and encounter spacing were preserved.",this);
            ResetDungeonPlanning();
        }
        private void BuildDungeonCandidates(ThemeObstacleLibrary library)
        {
            FinishDungeonVisit();dungeonLibrary=library;
            foreach(var family in library.FixedFamilies)
                if(family!=null && family.FamilyId=="Dungeon.CollapsedPillar")
                    foreach(var source in family.Variants) AddDungeonCandidate(source);
            foreach(var trap in library.DungeonTraps)
                if(trap!=null) AddDungeonCandidate(trap.gameObject);
            foreach(var rolling in library.RollingStoneBalls)
                if(rolling!=null) AddDungeonCandidate(rolling.gameObject);
        }
        private void AddDungeonCandidate(GameObject source)
        {
            if(source==null) return;
            foreach(var existing in dungeonCandidates) if(existing.Source==source) return;
            var trap=source.GetComponent<DungeonTrapEncounter>();
            var rolling=source.GetComponent<DungeonRollingStoneBall>();
            string family=rolling!=null ? DungeonRollingStoneBall.FamilyId : trap!=null ? trap.Family : "Dungeon.CollapsedPillar";
            dungeonCandidates.Add(new DungeonCandidate {Source=source,Trap=trap,Rolling=rolling,Family=family});
            if(!dungeonFamilies.Contains(family)) dungeonFamilies.Add(family);
        }
        private int DungeonCount(string family) => dungeonCounts.TryGetValue(family,out int count)?count:0;
        private bool TryPlanDungeon(TrackSegment road,int index,DifficultySnapshot difficulty,float origin,float limit,ThemeObstacleLibrary library,out SegmentPlan result)
        {
            result=default;
            if(dungeonLibrary!=library) BuildDungeonCandidates(library);
            var random=new System.Random(unchecked(runSeed^index*7919));
            for(int i=dungeonCandidates.Count-1;i>0;i--)
            {
                int j=random.Next(i+1);
                var candidate=dungeonCandidates[i];dungeonCandidates[i]=dungeonCandidates[j];dungeonCandidates[j]=candidate;
            }
            // Stable insertion sort retains seeded tie order without delegate/list allocations.
            for(int i=1;i<dungeonCandidates.Count;i++)
            {
                var candidate=dungeonCandidates[i];int j=i-1;
                while(j>=0 && DungeonCount(dungeonCandidates[j].Family)>DungeonCount(candidate.Family))
                {dungeonCandidates[j+1]=dungeonCandidates[j];j--;}
                dungeonCandidates[j+1]=candidate;
            }
            float maximum=difficulty.ForwardSpeed+difficulty.SpeedAdjustmentRange;
            var safety=new DifficultySnapshot(difficulty.Mode,maximum,0,1,0,difficulty.Progress01,difficulty.RoadWidth,difficulty.RecoverySeconds,difficulty.ConsecutiveLimit);
            for(int pass=0;pass<2;pass++)
            foreach(var candidate in dungeonCandidates)
            {
                bool repeat=candidate.Family==planner.LastFamily;
                if((pass==0 && repeat) || (pass==1 && !repeat)) continue;
                if(candidate.Rolling!=null)
                {
                    if(TryPlanRollingStone(candidate,road,index,difficulty,safety,origin,limit,random,out result)) return true;
                    continue;
                }
                var trap=candidate.Trap;
                bool jump=trap!=null && trap.RequiredJump;
                Bounds occupied;
                if(trap!=null)
                {
                    if(trap.Hazard==null) continue;
                    occupied=trap.SettledBounds;
                }
                else
                {
                    var shape=candidate.Source.GetComponent<BoxCollider>();
                    if(shape==null) continue;
                    occupied=new Bounds(shape.center,shape.size);
                }
                float gravity=Mathf.Max(.1f,-Physics.gravity.y),impulse=ballMovement.JumpSpeed;
                // Visual and jump dimensions remain manually tuned in Play Mode.
                float start=origin+12+occupied.min.z;
                float end=jump?origin+12+occupied.max.z+maximum*2*impulse/gravity+Radius+.5f:origin+15;
                float passage=trap!=null?trap.PassageX:(candidate.Source.name.Contains("Left")?1.5f:-1.5f);
                if(!jump)
                {
                    float edge=difficulty.RoadWidth*.5f-Radius-.15f;
                    float low=passage>0?occupied.max.x+Radius+.15f:-edge;
                    float high=passage>0?edge:occupied.min.x-Radius-.15f;
                    if(high<low) continue;
                    passage=(low+high)*.5f;
                }
                if(end>limit || !planner.CanReach(index,start,end,passage,safety,LateralSpeed,Acceleration)) continue;
                float approachSeconds=EncounterPlanner.SteeringTime(-passage,passage,LateralSpeed,Acceleration);
                if(start-ballMovement.transform.position.z<maximum*Mathf.Max(approachSeconds,1f)) continue;
                if(trap!=null && (!jump || trap.FallsOnApproach) && start-maximum*(trap.ApproachLeadSeconds+2*Time.fixedDeltaTime)-Radius<=ballMovement.transform.position.z) continue;
                long id=(long)Math.Round((origin+12)*1000);
                var window=new TaskWindow {LocalIndex=0,EntryZ=origin,HazardStartZ=start,LandingStartZ=origin+12+occupied.max.z+Radius+.1f,DeadlineZ=end,RecoveryEndZ=end};
                var placement=new Placement(candidate.Source,new Vector3(0,0,12),jump,speed:maximum);
                var plan=new EncounterPlan(this,runVersion,index,road,new[]{placement},false,end,passage,difficulty.RoadWidth,id,candidate.Family,new[]{window});
                planner.Commit(index,end,passage,candidate.Family);
                dungeonCounts[candidate.Family]=DungeonCount(candidate.Family)+1;
                result=new SegmentPlan(road,new List<EncounterPlan>{plan});return true;
            }
            return false;
        }
        private bool TryPlanRollingStone(DungeonCandidate candidate,TrackSegment road,int index,DifficultySnapshot difficulty,DifficultySnapshot safety,float origin,float limit,System.Random random,out SegmentPlan result)
        {
            result=default;
            var rolling=candidate.Rolling;
            if(!rolling.enabled || rolling.Hazard==null || !rolling.Hazard.enabled || rolling.Hazard.isTrigger) return false;
            float anchor=origin+12f;
            float overlap=rolling.AuthoredRadius+Radius+.15f;
            float start=anchor-overlap;
            float landing=anchor+rolling.ClearanceProgress(Radius);
            float recoveryEnd=landing+2f;
            float firstPosition=anchor+rolling.PositionAtProgress(ballMovement.transform.position.z-anchor,difficulty.ForwardSpeed);
            float end=Mathf.Max(recoveryEnd,firstPosition+rolling.AuthoredRadius+1f);
            float passage=planner.PreferredOppositeSide*(difficulty.RoadWidth*.5f-Radius-.15f);
            // Reserve the actual oncoming trajectory on ordinary road, not a repeating approach schedule.
            if(end>limit || !planner.CanReach(index,start,end,passage,safety,LateralSpeed,Acceleration)) return false;
            if(start-ballMovement.transform.position.z<safety.ForwardSpeed) return false;
            long id=(long)Math.Round(anchor*1000);
            var window=new TaskWindow {LocalIndex=0,EntryZ=start-12f,HazardStartZ=start,LandingStartZ=landing,DeadlineZ=landing+1f,RecoveryEndZ=recoveryEnd};
            float phase=random.Next(2)==0?0f:Mathf.PI;
            var placement=new Placement(candidate.Source,new Vector3(0,0,12),false,phase:phase,speed:difficulty.ForwardSpeed);
            var plan=new EncounterPlan(this,runVersion,index,road,new[]{placement},false,end,passage,difficulty.RoadWidth,id,candidate.Family,new[]{window});
            planner.Commit(index,end,passage,candidate.Family);
            dungeonCounts[candidate.Family]=DungeonCount(candidate.Family)+1;
            result=new SegmentPlan(road,new List<EncounterPlan>{plan});return true;
        }
    }
}
