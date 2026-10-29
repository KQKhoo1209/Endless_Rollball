using System;
using System.Collections.Generic;
using UnityEngine;
namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        internal sealed class BeachPlanningRequest
        {
            internal TrackSegment Road;
            internal int Index, Zone;
            internal bool Allowed;
            internal DifficultySnapshot Difficulty;
            internal float Origin, Limit;
            internal ThemeObstacleLibrary Library;
        }
        private SegmentPlan pendingBeachPlan;
        private ThemeObstacleLibrary beachCoverageLibrary;
        private int beachCoverageZone = -1, woodenPlans, explosivePlans;
        private void ResetBeachPlanning()
        { pendingBeachPlan=default; beachCoverageLibrary=null; beachCoverageZone=-1; woodenPlans=explosivePlans=0; }
        internal bool PlanBeachWindow(List<BeachPlanningRequest> requests, out SegmentPlan result)
        {
            result=default;
            if(requests.Count==0) return false;
            var first=requests[0];
            if(beachCoverageLibrary!=first.Library || beachCoverageZone!=first.Zone)
            { ResetBeachPlanning(); beachCoverageLibrary=first.Library; beachCoverageZone=first.Zone; }
            if(pendingBeachPlan.BeachBarrier!=null)
            {
                if(first.Index<pendingBeachPlan.Index) return true;
                if(first.Index==pendingBeachPlan.Index && first.Allowed)
                { result=pendingBeachPlan;pendingBeachPlan=default;return true; }
                pendingBeachPlan=default;
            }
            if(!ready || first.Library==null || first.Library.BeachBarrierTracks.Length==0 || !first.Allowed) return false;
            bool missing=woodenPlans<3 || explosivePlans<3;
            var random=new System.Random(unchecked(runSeed^first.Index*7919));
            if(!missing && random.NextDouble()>=.3) return false;
            bool preferred=explosivePlans<woodenPlans;
            // Try the underrepresented family first, including compatible future slots.
            // Clear road is committed only after a validated future encounter exists.
            for(int family=0;family<2;family++) foreach(var request in requests)
            {
                if(!request.Allowed || request.Zone!=first.Zone || request.Library!=first.Library) break;
                if(!TryPlanBeachBarrier(request.Road,request.Index,request.Difficulty,request.Origin,request.Limit,
                    request.Library,out var accepted,true,family==0?preferred:!preferred)) continue;
                if(accepted.BeachBarrier.Explosive) explosivePlans++; else woodenPlans++;
                Debug.Log($"Beach barrier planned: {accepted.BeachBarrier.Family}, segment {request.Index}, zone {request.Zone}, wooden={woodenPlans}, explosive={explosivePlans}.",this);
                if(request.Index==first.Index) result=accepted; else pendingBeachPlan=accepted;
                return true;
            }
            if(missing) Debug.Log($"Beach barrier deferred: zone {first.Zone}, segment {first.Index}; no safe slot in {requests.Count}-segment lookahead (wooden={woodenPlans}, explosive={explosivePlans}).",this);
            return false;
        }
        private readonly Dictionary<TrackSegment, BeachBarrierEncounter> activeBeachBarriers = new();
        private bool TryPlanBeachBarrier(TrackSegment road, int index, DifficultySnapshot difficulty,
            float origin, float limit, ThemeObstacleLibrary library, out SegmentPlan result, bool forceSelection = false, bool? preferredExplosive = null)
        {
            result=default;
            if(library==null || library.BeachBarrierTracks.Length==0 || ballMovement==null || origin<96f) return false;
            long id=(long)Math.Round((origin+12f)*1000d);
            var random=new System.Random(unchecked(runSeed^(int)id^(int)(id>>32)));
            if(!forceSelection && random.NextDouble()>=.4) return false;
            int offset=random.Next(library.BeachBarrierTracks.Length);
            float maximum=difficulty.ForwardSpeed+difficulty.SpeedAdjustmentRange;
            float minimum=Mathf.Max(.1f,difficulty.ForwardSpeed-difficulty.SpeedAdjustmentRange);
            float gravity=Mathf.Max(.1f,-Physics.gravity.y), jump=ballMovement.JumpSpeed;
            // Conservative allowance for both lips, timing error and one physics step.
            if(minimum*2f*jump/gravity < 3.2f+minimum*Time.fixedDeltaTime+.3f) return false;
            for(int i=0;i<library.BeachBarrierTracks.Length;i++)
            {
                var source=library.BeachBarrierTracks[(offset+i)%library.BeachBarrierTracks.Length];
                if(source==null || source.Family==planner.LastFamily || (preferredExplosive.HasValue && source.Explosive!=preferredExplosive.Value)) continue;
                var track=source.GetComponent<TrackSegment>();
                if(track==null || !track.IsContractValid(out _) || !track.Supports(difficulty.Mode)) continue;
                float clearHeight=.6f+.08f;
                if(!source.Explosive && (jump*jump<=2f*gravity*clearHeight ||
                    2f*Mathf.Sqrt(jump*jump-2f*gravity*clearHeight)/gravity*minimum*1.25f < .6f+2f*Radius+maximum*1.25f*Time.fixedDeltaTime+.3f)) continue;
                float approach=source.Explosive ? 10.4f : 2f;
                float end=origin+Mathf.Max(25f,14f+maximum*2f*jump/gravity+Radius);
                float playerLocal=ballMovement.transform.position.z-origin;
                if(source.Explosive && BeachBarrierEncounter.ExplosionTriggerLocalZ(maximum,jump,Radius)-playerLocal < maximum*1.2f) continue;
                if(end>limit || !planner.CanReach(index,origin+approach,end,0f,difficulty,LateralSpeed,Acceleration)) continue;
                var window=new TaskWindow { LocalIndex=1, EntryZ=origin-12f, HazardStartZ=origin+(source.Explosive?10.4f:21.7f),
                    LandingStartZ=origin+(source.Explosive?13.6f+Radius+.15f:22.3f+Radius+.15f), DeadlineZ=end, RecoveryEndZ=end };
                result=new SegmentPlan(track,new List<EncounterPlan>(),index:index,encounterId:id,vehicleWindow:window,vehiclePosition:new Vector3(maximum,0,0),beachBarrier:source);
                planner.Commit(index,end,0f,source.Family);
                return true;
            }
            return false;
        }
        internal bool OwnsBeachBarrier(BeachBarrierEncounter barrier) => activeBeachBarriers.ContainsValue(barrier);
        private void AttachBeachBarrier(TrackSegment track, SegmentPlan plan)
        {
            if(activeBeachBarriers.ContainsKey(track)) return;
            var barrier=track.GetComponent<BeachBarrierEncounter>();
            if(barrier==null) return;
            activeBeachBarriers.Add(track,barrier);
            barrier.Prepare(ballMovement, plan.VehiclePosition.x,motionPaused,taskTracker);
            if(barrier.Explosive) taskTracker?.RegisterGap(track,plan.Index,1,plan.EncounterId,plan.VehicleWindow);
            else taskTracker?.RegisterPayload(track,plan.Index,barrier.Hazard.gameObject,true,null,1,new[]{barrier.Hazard},plan.EncounterId,plan.VehicleWindow,true);
        }
    }
}
