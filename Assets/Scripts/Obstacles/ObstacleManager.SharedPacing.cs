using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessRollball.Track
{
    public sealed partial class ObstacleManager
    {
        private enum SharedKind { Fixed, Trap, Stone, Combined, Roadworks, Junction, Vehicle, Beach, Gap }
        private sealed class SharedCandidate
        {
            internal GameObject Source;
            internal SharedKind Kind;
            internal string Family;
            internal Bounds Bounds;
            internal DungeonTrapEncounter Trap;
            internal DungeonRollingStoneBall Stone;
            internal PalmObstacle Palm;
            internal bool Jump;
            internal float DecisionOffset, LastDecisionOffset=24;
        }
        private sealed class SharedFamily
        {
            internal string Name;
            internal readonly List<SharedCandidate> Variants = new List<SharedCandidate>();
        }
        internal sealed class DecisionDiagnostic
        {
            public int Segment, GroupSize, FixedSide;
            public long Identity;
            public string Family, Prefab, Layout, Reason;
            public float DecisionZ, ClearZ, RecoveryEndZ, PassageMin, PassageMax, ExitX, Seconds, TargetSeconds;
            public bool Jump;
        }
        private struct SharedHistory
        {
            internal string Family;
            internal GameObject Variant;
            internal float Decision;
        }
        private struct SharedReservation
        {
            internal Bounds Hazard;
            internal float RoadMin, RoadMax;
            internal bool OrdinaryRoad, Exclusive;
            internal float ZSlope, ZIntercept, HalfDepth, ActiveMin, ActiveMax;
        }
        private sealed class SharedChoice
        {
            internal SegmentPlan Plan;
            internal SharedCandidate Candidate;
            internal GameObject Variant;
            internal Bounds Hazard;
            internal float Decision, Clear, Low, High, X, RoadMin, RoadMax;
            internal bool Jump, Exclusive, NeedsRoad;
            internal int FixedSide;
            internal float ZSlope, ZIntercept, HalfDepth, ActiveMin, ActiveMax;
        }
        [Header("Shared encounter diagnostics (development only)")]
        [SerializeField] private bool captureDecisionDiagnostics;
        internal readonly List<DecisionDiagnostic> decisionDiagnostics = new List<DecisionDiagnostic>();
        internal readonly Dictionary<string, int> sharedRejections = new Dictionary<string, int>();
        private readonly Dictionary<int, SegmentPlan> sharedLookahead = new Dictionary<int, SegmentPlan>();
        private readonly Dictionary<ThemeObstacleLibrary, List<SharedFamily>> sharedLibraries = new Dictionary<ThemeObstacleLibrary, List<SharedFamily>>();
        private readonly Dictionary<string, int> sharedCounts = new Dictionary<string, int>();
        private readonly List<SharedHistory> sharedHistory = new List<SharedHistory>(32);
        private readonly List<SharedReservation> sharedReservations = new List<SharedReservation>(32);
        private readonly List<SharedFamily> sharedFamilyOrder = new List<SharedFamily>(12);
        private readonly Dictionary<SharedCandidate,SharedChoice> sharedSpecialCache = new Dictionary<SharedCandidate,SharedChoice>();
        private readonly List<SharedCandidate> sharedVariantOrder = new List<SharedCandidate>(24);
        private ThemeObstacleLibrary sharedLibrary;
        private int sharedZone = -1, sharedSinceVarietyBreath;
        private int sharedNavigationDecisions, sharedJumpDecisions;
        private float sharedVisitStart;
        private string sharedDelayReason = "segment boundary/geometry";
        private float nextSharedDecision = float.NegativeInfinity, sharedClear = float.NegativeInfinity;
        private float sharedExit, sharedLastDecision = float.NegativeInfinity, sharedLastJumpEnd = float.NegativeInfinity;
        private GameObject sharedCandidate;
        private bool sharedAuthoredProbe;
        private float sharedRowPreviousExit, sharedRowPreviousClear, sharedPlanningPlayerZ;
        private float PlanningPlayerZ => sharedCandidate!=null?sharedPlanningPlayerZ:ballMovement.transform.position.z;

        internal bool TryTakeScheduledPlan(int index, ThemeObstacleLibrary library, out SegmentPlan plan) =>
            library != null && library.SharedPacing != null ? TryTakeSharedPlan(index, out plan) : TryTakeCityPlan(index, out plan);
        private bool TryTakeSharedPlan(int index, out SegmentPlan plan)
        {
            if (!sharedLookahead.TryGetValue(index, out plan)) return false;
            sharedLookahead.Remove(index); return true;
        }
        private void FinishSharedVisit()
        {
            if (sharedLibrary == null || sharedLibrary.SharedPacing == null) return;
            int minimum = sharedLibrary.SharedPacing.minimumFamilyAppearances;
            foreach (var family in SharedFamilies(sharedLibrary))
                if (SharedCount(family.Name) < minimum)
                    Debug.LogWarning($"{sharedLibrary.name} visit {sharedZone}: {family.Name} appeared {SharedCount(family.Name)}/{minimum}; available geometry/visit capacity was insufficient.", this);
        }
        private void ResetSharedPacing()
        {
            FinishSharedVisit();
            ResetFixedSidePattern();
            sharedLibrary=null; sharedZone=-1; sharedCandidate=null;sharedSinceVarietyBreath=0;
            sharedNavigationDecisions=sharedJumpDecisions=0;
            sharedLibraries.Clear(); sharedLookahead.Clear(); sharedCounts.Clear(); sharedHistory.Clear(); sharedReservations.Clear();
            sharedFamilyOrder.Clear(); sharedVariantOrder.Clear(); sharedSpecialCache.Clear(); decisionDiagnostics.Clear(); sharedRejections.Clear();
            nextSharedDecision=sharedClear=sharedLastDecision=sharedLastJumpEnd=float.NegativeInfinity; sharedExit=0;
        }
        internal void PlanSharedWindow(List<CityPlanningRequest> requests)
        {
            float entry = requests.Count == 0 ? 0 : requests[0].Origin;
            foreach (var request in requests)
            {
                float delta=entry-request.Origin;
                request.Origin=entry; request.Limit+=delta;
                SegmentPlan plan=PlanSharedSegment(request);
                sharedLookahead[request.Index]=plan;
                TrackSegment selected=plan.Track!=null?plan.Track:request.Road;
                entry=new TrackRouteSpan(request.Index,entry,selected.EntryLocal.z,selected.ExitLocal.z).ExitZ;
                request.RecordEntry?.Invoke(request.Index,request.Origin,entry);
            }
        }
        private int SharedCount(string family) => sharedCounts.TryGetValue(family, out int count)?count:0;
        private bool PreferSharedNavigation(ThemeObstacleLibrary library)
        {
            float share=library.SharedPacing.navigationShare;
            return sharedNavigationDecisions < Mathf.CeilToInt(
                (sharedNavigationDecisions+sharedJumpDecisions+1)*share-.0001f);
        }
        private void SharedReject(string reason)
        {
            sharedDelayReason=reason;
            if(captureDecisionDiagnostics) sharedRejections[reason]=sharedRejections.TryGetValue(reason,out int n)?n+1:1;
        }
        private List<SharedFamily> SharedFamilies(ThemeObstacleLibrary library)
        {
            if(sharedLibraries.TryGetValue(library,out var families)) return families;
            families=new List<SharedFamily>(12);
            var unique=new HashSet<GameObject>();
            void Add(GameObject source,string family,SharedKind kind,bool jump=false)
            {
                if(source==null || !unique.Add(source)) return;
                var trap=source.GetComponent<DungeonTrapEncounter>();
                var stone=source.GetComponent<DungeonRollingStoneBall>();
                if(trap!=null) {kind=SharedKind.Trap;family=trap.Family;jump=trap.RequiredJump;}
                if(stone!=null) {kind=SharedKind.Stone;family=DungeonRollingStoneBall.FamilyId;}
                if(string.IsNullOrWhiteSpace(family)) return;
                Bounds bounds=default;
                if(kind==SharedKind.Fixed && !TryGetFootprint(source,out bounds)) return;
                if(trap!=null && trap.Hazard!=null) bounds=trap.SettledBounds;
                SharedFamily bucket=families.Find(f=>f.Name==family);
                if(bucket==null) {bucket=new SharedFamily{Name=family};families.Add(bucket);}
                var item=new SharedCandidate{Source=source,Family=family,Kind=kind,Jump=jump,
                    Bounds=bounds,Trap=trap,Stone=stone,Palm=source.GetComponent<PalmObstacle>()};
                if(kind==SharedKind.Combined && TryMeasureCombinedGroup(source.transform,source.GetComponent<CombinedObstacleEncounter>().JumpHazards,out Bounds jumpBounds) &&
                    TryMeasureCombinedGroup(source.transform,source.GetComponent<CombinedObstacleEncounter>().NavigationHazards,out Bounds navigationBounds))
                    item.DecisionOffset=item.LastDecisionOffset=12+Mathf.Min(jumpBounds.min.z,navigationBounds.min.z);
                if(kind==SharedKind.Roadworks) item.DecisionOffset=item.LastDecisionOffset=source.GetComponent<RoadworksTrackEncounter>().HazardStartZ;
                if(kind==SharedKind.Beach) item.DecisionOffset=item.LastDecisionOffset=source.GetComponent<BeachBarrierEncounter>().Explosive?10.4f:21.7f;
                if(kind==SharedKind.Stone) item.DecisionOffset=item.LastDecisionOffset=12-stone.AuthoredRadius-Radius-.15f;
                bucket.Variants.Add(item);
            }
            foreach(var family in library.FixedFamilies) if(family!=null)
                foreach(var source in family.Variants) Add(source,family.FamilyId,SharedKind.Fixed);
            foreach(var family in library.JumpFamilies) if(family!=null)
                foreach(var source in family.Variants) Add(source,family.FamilyId,SharedKind.Fixed,true);
            foreach(var trap in library.DungeonTraps) if(trap!=null) Add(trap.gameObject,trap.Family,SharedKind.Trap);
            foreach(var stone in library.RollingStoneBalls) if(stone!=null) Add(stone.gameObject,DungeonRollingStoneBall.FamilyId,SharedKind.Stone);
            foreach(var combined in library.CombinedEncounters) if(combined!=null) Add(combined.gameObject,combined.FamilyId,SharedKind.Combined);
            foreach(var roadworks in library.RoadworksTracks) if(roadworks!=null) Add(roadworks.gameObject,roadworks.FamilyId,SharedKind.Roadworks);
            foreach(var junction in library.JunctionTracks) if(junction!=null) Add(junction.gameObject,JunctionFamily,SharedKind.Junction);
            foreach(var stop in library.RoadsideStops) if(stop!=null) Add(stop.gameObject,stop.FamilyId,SharedKind.Vehicle);
            foreach(var traffic in library.LongitudinalTrafficTracks) if(traffic!=null) Add(traffic.gameObject,traffic.FamilyId,SharedKind.Vehicle);
            foreach(var beach in library.BeachBarrierTracks) if(beach!=null) Add(beach.gameObject,beach.Family,SharedKind.Beach);
            sharedLibraries.Add(library,families); return families;
        }
        private static void SharedShuffle<T>(List<T> list,System.Random random)
        {
            for(int i=list.Count-1;i>0;i--) {int j=random.Next(i+1);T value=list[i];list[i]=list[j];list[j]=value;}
        }
        private void SharedOrderVariants(SharedFamily family,System.Random random)
        {
            sharedVariantOrder.Clear();sharedVariantOrder.AddRange(family.Variants);SharedShuffle(sharedVariantOrder,random);
            if(family.Name!="Street.LongitudinalTraffic") return;
            bool preferred=!lastLongitudinalOncoming.GetValueOrDefault(false);
            for(int i=1;i<sharedVariantOrder.Count;i++)
            {
                var item=sharedVariantOrder[i];
                var traffic=item.Source.GetComponent<LongitudinalTrafficEncounter>();
                if(traffic==null || traffic.Oncoming!=preferred) continue;
                int j=i-1;
                while(j>=0 && sharedVariantOrder[j].Source.GetComponent<LongitudinalTrafficEncounter>().Oncoming!=preferred)
                {sharedVariantOrder[j+1]=sharedVariantOrder[j];j--;}
                sharedVariantOrder[j+1]=item;
            }
        }
        private bool SharedVariety(string family,GameObject variant,float decision,bool relaxed)
        {
            int in24=0, age=0;
            for(int i=sharedHistory.Count-1;i>=0;i--)
            {
                SharedHistory history=sharedHistory[i];
                if(decision-history.Decision<24f-.001f)
                {
                    if(history.Family==family && ++in24>=2) return false;
                    if(history.Variant==variant) return false;
                }
                if(!relaxed && ((age<2 && history.Family==family) || (age<4 && history.Variant==variant))) return false;
                age++;
            }
            return true;
        }
        private Vector2 SharedInterval(DifficultySnapshot difficulty,ThemeObstacleLibrary.SharedPacingSettings settings)
        {
            switch(difficulty.Mode)
            {
                case DifficultyMode.Easy:return settings.easySeconds;
                case DifficultyMode.Normal:return settings.normalSeconds;
                case DifficultyMode.Hard:return settings.hardSeconds;
                default:return Vector2.Lerp(settings.endlessMinimumSeconds,settings.endlessMaximumSeconds,difficulty.Progress01);
            }
        }
        private float SharedMargin(DifficultySnapshot d)
        {
            Vector3 extra=sharedLibrary.SharedPacing.passageExtraWidth;
            return .5f*(d.Mode==DifficultyMode.Easy?extra.x:d.Mode==DifficultyMode.Normal?extra.y:
                d.Mode==DifficultyMode.Hard?extra.z:Mathf.Lerp(extra.x,extra.z,d.Progress01));
        }
        private SharedChoice BuildSharedChoice(SharedCandidate candidate,CityPlanningRequest r,float cursor,System.Random random,int side,int rowSide=0)
        {
            float maxSpeed=r.Difficulty.ForwardSpeed+r.Difficulty.SpeedAdjustmentRange;
            var safety=new DifficultySnapshot(r.Difficulty.Mode,maxSpeed,0,1,r.Difficulty.MovingObstacleSpeed,
                r.Difficulty.Progress01,r.Difficulty.RoadWidth,r.Difficulty.RecoverySeconds,r.Difficulty.ConsecutiveLimit,r.Difficulty.CombinationChance);
            long identity=(long)Math.Round(cursor*1000d);
            if(candidate.Kind==SharedKind.Fixed || candidate.Kind==SharedKind.Trap)
            {
                Bounds bounds=candidate.Bounds;
                bool jump=candidate.Jump;
                float half=r.Difficulty.RoadWidth*.5f, margin=SharedMargin(r.Difficulty);
                int fixedSide=0;
                bool left=false;
                if(!jump)
                {
                    int wantedSide=rowSide!=0?rowSide:NextFixedSide;
                    if(candidate.Kind==SharedKind.Fixed)
                    {
                        fixedSide=wantedSide;
                        left=fixedSide<0;
                        // Palms remain rooted on their authored beach side.
                        if(candidate.Palm!=null && candidate.Palm.BlocksLeft!=left) return null;
                    }
                    else
                    {
                        // Side fire, rubble and fallen pillars use authored mirrored variants.
                        fixedSide=BlockingSide(bounds.center.x);
                        if(fixedSide!=0 && fixedSide!=wantedSide) return null;
                        left=fixedSide<0;
                    }
                }
                float yaw=candidate.Family=="Street.ParkedVehicles" && !left?180:0;
                bounds=RotatedFootprint(bounds,yaw);
                float x=0;
                if(candidate.Kind==SharedKind.Fixed && !jump)
                    x=candidate.Palm!=null && candidate.Palm.RootedOnBeach?candidate.Palm.RoadsideX(half):
                        left?-half+RoadCorridor.BoundarySkin-bounds.min.x:half-RoadCorridor.BoundarySkin-bounds.max.x;
                float localZ=cursor-r.Origin-bounds.min.z;
                bounds.center+=new Vector3(x,0,r.Origin+localZ);
                if(localZ<0 || localZ>24 || bounds.min.z<r.Origin+.5f || bounds.max.z>r.Limit || bounds.max.z+Radius+.1f>r.Origin+24f) return null;
                float low=-half+Radius+margin+RoadCorridor.BoundarySkin,high=-low;
                if(!jump)
                {
                    float leftHigh=Mathf.Min(high,bounds.min.x-Radius-margin);
                    float rightLow=Mathf.Max(low,bounds.max.x+Radius+margin);
                    bool useLeft=(side==0?sharedExit<=0:sharedExit>0);
                    if(leftHigh<low) useLeft=false;
                    if(rightLow>high) useLeft=true;
                    if(useLeft) high=leftHigh;else low=rightLow;
                    if(high<low) return null;
                }
                float target=Mathf.Clamp(sharedExit,low,high);
                if(jump) target=Mathf.Clamp(0,low,high);
                float clear=bounds.max.z+Radius+.1f;
                if(jump) clear=Mathf.Max(clear,cursor-Radius+maxSpeed*2f*ballMovement.JumpSpeed/Mathf.Max(.1f,-Physics.gravity.y)+.25f);
                if(candidate.Trap!=null && candidate.Trap.Kind!=DungeonTrapEncounter.TrapKind.Fire &&
                    (!jump || candidate.Trap.FallsOnApproach) &&
                    bounds.min.z-(r.Origin-r.SpawnAhead) < maxSpeed*(candidate.Trap.ApproachLeadSeconds+2*Time.fixedDeltaTime)+Radius)
                {SharedReject("trap warning lead");return null;}
                var window=new TaskWindow{LocalIndex=jump?1:0,EntryZ=cursor-12,HazardStartZ=bounds.min.z,
                    LandingStartZ=bounds.max.z+Radius+.1f,DeadlineZ=Mathf.Max(clear+.5f,bounds.max.z+Radius+.6f),
                    RecoveryEndZ=clear+Mathf.Max(1f,maxSpeed*r.Difficulty.RecoverySeconds)};
                var placement=new Placement(candidate.Source,new Vector3(x,0,localZ),jump,speed:maxSpeed,taskIndex:jump?1:0,yawDegrees:yaw);
                var encounter=new EncounterPlan(this,runVersion,r.Index,r.Road,new[]{placement},false,window.RecoveryEndZ,target,r.Difficulty.RoadWidth,
                    identity,candidate.Family,new[]{window});
                return new SharedChoice{Candidate=candidate,Variant=candidate.Source,Plan=new SegmentPlan(r.Road,new List<EncounterPlan>{encounter}),
                    Hazard=bounds,Decision=bounds.min.z,Clear=clear,Low=low,High=high,X=target,Jump=jump,
                    Exclusive=jump,RoadMin=cursor-Radius,RoadMax=clear,NeedsRoad=jump,FixedSide=fixedSide};
            }
            if(side!=0) return null;
            if(sharedSpecialCache.TryGetValue(candidate,out var cached))
                return cached!=null && cached.Decision>=cursor-.01f && (sharedAuthoredProbe || cached.Decision<=cursor+.51f)?cached:null;
            if(!sharedAuthoredProbe && (cursor<r.Origin+candidate.DecisionOffset-.51f || cursor>r.Origin+candidate.LastDecisionOffset+.01f)) return null;
            SegmentPlan result=default;
            var previous=planner.Copy();bool? previousDirection=lastLongitudinalOncoming;
            bool previousActive=cityPacingActive;
            sharedCandidate=candidate.Source;sharedPlanningPlayerZ=r.Origin-r.SpawnAhead;planner.SharedCandidateValidation=true;cityPacingActive=true;
            try
            {
                switch(candidate.Kind)
                {
                    case SharedKind.Combined:
                        if(TryPlanCombined(r.Library,r.Road,r.Index,identity,safety,r.Origin,r.Limit,random,out var combined,true))
                            result=new SegmentPlan(r.Road,new List<EncounterPlan>{combined});
                        break;
                    case SharedKind.Roadworks:TryPlanRoadworks(r.Library,r.Index,identity,safety,r.Origin,r.Limit,random,out result,true);break;
                    case SharedKind.Junction:
                        if(r.Junction) TryPlanJunction(r.Library,r.Index,identity,r.Difficulty,r.Origin,r.Limit,random,out result,true);break;
                    case SharedKind.Vehicle:
                        var vehicle=candidate.Source.GetComponent<CityVehicleEncounter>();
                        if(vehicle.IsMoving?r.Traffic:r.Stops)
                            TryPlanCityVehicle(r.Library,r.Road,r.Index,identity,r.Difficulty,r.Origin,r.Limit,random,!vehicle.IsMoving,vehicle.IsMoving,out result,true);
                        break;
                    case SharedKind.Beach:
                        if(r.Beach) TryPlanBeachBarrier(r.Road,r.Index,r.Difficulty,r.Origin,r.Limit,r.Library,out result,true);break;
                    case SharedKind.Stone:
                        TryPlanRollingStone(new DungeonCandidate{Source=candidate.Source,Rolling=candidate.Stone,Family=candidate.Family},
                            r.Road,r.Index,r.Difficulty,safety,r.Origin,r.Limit,random,out result);break;
                }
            }
            finally {planner.Restore(previous);lastLongitudinalOncoming=previousDirection;cityPacingActive=previousActive;sharedCandidate=null;}
            if(result.Track==null) {sharedSpecialCache[candidate]=null;SharedReject("authored encounter unavailable/geometry");return null;}
            TaskWindow window2=result.VehicleWindow??result.JunctionWindow??result.RoadworksWindow;
            if(window2==null && result.Encounters.Count>0) window2=result.Encounters[0].Windows[0];
            if(window2==null) return null;
            result=ReidentifySharedPlan(result,(long)Math.Round(window2.HazardStartZ*1000d));
            bool requiredJump=candidate.Kind==SharedKind.Beach || candidate.Kind==SharedKind.Combined;
            bool moving=result.CityVehicle!=null && result.CityVehicle.IsMoving;
            Bounds hazard=new Bounds(new Vector3(0,1,r.Origin+12),new Vector3(r.Difficulty.RoadWidth,20,24));
            float low2=-r.Difficulty.Corridor.CentreLimit(Radius+SharedMargin(r.Difficulty)),high2=-low2;
            float exit=0;
            bool exclusive=true,needsRoad=true;
            if(result.CityVehicle!=null && TryGetFootprint(result.VehiclePrefab,out Bounds vehicleBounds))
            {
                hazard=RotatedFootprint(vehicleBounds,result.CityVehicle.YawDegrees);
                hazard.center+=result.VehiclePosition+Vector3.forward*r.Origin;
                exit=(hazard.center.x>=0?-1:1)*r.Difficulty.RoadWidth*.25f;
                if(exit>0) low2=Mathf.Max(low2,hazard.max.x+Radius+SharedMargin(r.Difficulty));
                else high2=Mathf.Min(high2,hazard.min.x-Radius-SharedMargin(r.Difficulty));
                exclusive=false;needsRoad=moving;
                if(moving)
                {
                    float slope=((LongitudinalTrafficEncounter)result.CityVehicle).Oncoming?-.5f:.5f;
                    float intercept=r.Origin+result.CityVehicle.VehicleZ(24f-r.Origin)+RotatedFootprint(vehicleBounds,result.CityVehicle.YawDegrees).center.z;
                    float halfDepth=hazard.extents.z;
                    float first=intercept+slope*(r.Origin-r.SpawnAhead);
                    float last=intercept+slope*(Mathf.Max(r.Origin+24,result.EndZ)+r.RecycleBehind);
                    var min=hazard.min;var max=hazard.max;
                    min.z=Mathf.Min(first,last)-halfDepth;max.z=Mathf.Max(first,last)+halfDepth;hazard.SetMinMax(min,max);
                }
            }
            else if(result.Roadworks!=null) {exit=result.Roadworks.SafePassageX;low2=high2=exit;}
            else if(result.Junction!=null)
            {
                exit=JunctionTrafficController.PlannedSafePassage(result.TrafficSeed);low2=high2=exit;
                hazard=new Bounds(new Vector3(0,1,(window2.HazardStartZ+window2.LandingStartZ)*.5f),
                    new Vector3(r.Difficulty.RoadWidth,20,window2.LandingStartZ-window2.HazardStartZ));
            }
            else if(candidate.Kind==SharedKind.Stone)
            {
                float radius=candidate.Stone.AuthoredRadius;
                float initial=r.Origin+12+candidate.Stone.PositionAtProgress(-r.SpawnAhead-12,r.Difficulty.ForwardSpeed);
                hazard=new Bounds(new Vector3(0,radius,(window2.HazardStartZ+initial+radius)*.5f),
                    new Vector3(r.Difficulty.RoadWidth,2*radius,initial+radius-window2.HazardStartZ));
                exit=planner.PreferredOppositeSide*r.Difficulty.Corridor.CentreLimit(Radius+SharedMargin(r.Difficulty));
                // The authored five-metre stone uses the explicit tight-passage minimum;
                // keep its scale and weave amplitude, rather than silently resizing the asset.
                const float stonePadding=.1f;
                float edge=r.Difficulty.Corridor.CentreLimit(Radius+stonePadding);
                if(exit<0) {low2=-edge;high2=Mathf.Min(edge,candidate.Stone.SidewaysDistance-radius-Radius-stonePadding);}
                else {low2=Mathf.Max(-edge,-candidate.Stone.SidewaysDistance+radius+Radius+stonePadding);high2=edge;}
                if(low2>high2) {SharedReject("rolling stone passage dimensions");return null;}
                exit=Mathf.Clamp(exit,low2,high2);
            }
            else if(candidate.Kind==SharedKind.Combined) {exit=result.Encounters[0].ExitX;low2=high2=exit;}
            float clearance=window2.LandingStartZ;
            if(requiredJump) clearance=Mathf.Max(clearance,window2.HazardStartZ-Radius+maxSpeed*2f*ballMovement.JumpSpeed/Mathf.Max(.1f,-Physics.gravity.y)+.25f);
            if(candidate.Kind==SharedKind.Stone || candidate.Kind==SharedKind.Beach) clearance=result.EndZ;
            if(low2>high2) return null;
            float movementSlope=moving?(((LongitudinalTrafficEncounter)result.CityVehicle).Oncoming?-.5f:.5f):0;
            float movementIntercept=moving?r.Origin+result.CityVehicle.VehicleZ(24-r.Origin)+RotatedFootprint(GeometryBounds(result.VehiclePrefab),result.CityVehicle.YawDegrees).center.z:hazard.center.z;
            float movementDepth=moving?RotatedFootprint(GeometryBounds(result.VehiclePrefab),result.CityVehicle.YawDegrees).extents.z:hazard.extents.z;
            var built=new SharedChoice{Candidate=candidate,Variant=result.VehiclePrefab!=null?result.VehiclePrefab:candidate.Source,Plan=result,
                Decision=window2.HazardStartZ,Clear=clearance,Hazard=hazard,Low=low2,High=high2,X=Mathf.Clamp(exit,low2,high2),
                Jump=requiredJump,Exclusive=exclusive,NeedsRoad=needsRoad,
                RoadMin=moving?hazard.min.z:Mathf.Min(r.Origin,window2.HazardStartZ),RoadMax=moving?hazard.max.z:(result.Junction!=null?clearance:Mathf.Max(result.EndZ,clearance)),
                ZSlope=movementSlope,ZIntercept=movementIntercept,HalfDepth=movementDepth,
                ActiveMin=r.Origin-r.SpawnAhead,ActiveMax=Mathf.Max(r.Origin+24,result.EndZ)+r.RecycleBehind,
                FixedSide=result.Roadworks!=null ? -BlockingSide(result.Roadworks.SafePassageX) :
                    result.CityVehicle is RoadsideStopEncounter stop ? (stop.RightSide?1:-1) : 0};
            sharedSpecialCache[candidate]=built;
            return built.Decision>=cursor-.01f && (sharedAuthoredProbe || built.Decision<=cursor+.51f)?built:null;
        }
        private SegmentPlan ReidentifySharedPlan(SegmentPlan plan,long identity)
        {
            var encounters=new List<EncounterPlan>();
            if(plan.Encounters!=null) foreach(var e in plan.Encounters)
                encounters.Add(new EncounterPlan(this,e.Version,e.Index,e.Track,e.Placements,e.IsGap,e.EndZ,e.ExitX,e.Width,identity,e.Family,e.Windows,e.GapTaskIndex));
            return new SegmentPlan(plan.Track,encounters,plan.Roadworks,plan.Index,identity,plan.RoadworksWindow,
                plan.Junction,plan.JunctionWindow,plan.TrafficSeed,plan.CityVehicle,plan.VehiclePrefab,plan.VehiclePosition,plan.VehicleWindow,plan.BeachBarrier);
        }
        private SharedChoice BuildSharedGap(CityPlanningRequest r,float cursor)
        {
            float start=r.Origin+GapTakeoffZ(r.Gap),landing=r.Origin+GapLandingZ(r.Gap)+Radius+.15f;
            if(start<cursor-.01f || (!sharedAuthoredProbe && start>cursor+.51f)) return null;
            float maxSpeed=r.Difficulty.ForwardSpeed+r.Difficulty.SpeedAdjustmentRange;
            float clear=Mathf.Max(landing,start-Radius+maxSpeed*2*ballMovement.JumpSpeed/Mathf.Max(.1f,-Physics.gravity.y)+.25f);
            var window=new TaskWindow{LocalIndex=1,EntryZ=start-12,HazardStartZ=start,LandingStartZ=landing,DeadlineZ=clear+.5f,RecoveryEndZ=clear+1};
            long id=(long)Math.Round(start*1000d);
            var plan=new EncounterPlan(this,runVersion,r.Index,r.Gap,Array.Empty<Placement>(),true,window.RecoveryEndZ,0,r.Difficulty.RoadWidth,id,"Gap",new[]{window});
            return new SharedChoice{Candidate=new SharedCandidate{Family="Gap",Source=r.Gap.gameObject,Kind=SharedKind.Gap},Variant=r.Gap.gameObject,
                Plan=new SegmentPlan(r.Gap,new List<EncounterPlan>{plan}),Decision=start,Clear=clear,Low=0,High=0,X=0,Jump=true,Exclusive=true,NeedsRoad=false,
                RoadMin=r.Origin,RoadMax=clear,Hazard=new Bounds(new Vector3(0,1,(r.Origin+clear)*.5f),new Vector3(r.Difficulty.RoadWidth,20,clear-r.Origin))};
        }
        private Bounds GeometryBounds(GameObject source) { TryGetFootprint(source,out Bounds bounds); return bounds; }
        private static bool SharedTimeOverlap(SharedChoice choice,SharedReservation prior,CityPlanningRequest request)
        {
            if(choice.ZSlope==0 && prior.ZSlope==0) return true;
            float from=Mathf.Max(request.Origin-request.SpawnAhead,prior.ActiveMin);
            float to=Mathf.Min(Mathf.Max(request.Origin+24,choice.Plan.EndZ)+request.RecycleBehind,prior.ActiveMax);
            if(to<from) return false;
            float slope=choice.ZSlope-prior.ZSlope;
            float intercept=(choice.ZSlope==0?choice.Hazard.center.z:choice.ZIntercept)-prior.ZIntercept;
            float half=(choice.HalfDepth>0?choice.HalfDepth:choice.Hazard.extents.z)+prior.HalfDepth+.1f+
                (Mathf.Abs(choice.ZSlope)+Mathf.Abs(prior.ZSlope))*(request.Difficulty.ForwardSpeed+request.Difficulty.SpeedAdjustmentRange)*Time.fixedDeltaTime*2f;
            float first=intercept+slope*from,last=intercept+slope*to;
            return Mathf.Min(first,last)<=half && Mathf.Max(first,last)>=-half;
        }
        private bool ValidateSharedChoice(SharedChoice choice,CityPlanningRequest r)
        {
            // Authored replacements are probed before the ordinary cursor. They must
            // obey the same interval, including when selected in the next segment.
            if(choice.Decision<nextSharedDecision-.01f)
            {SharedReject("difficulty decision interval");return false;}
            if(choice.FixedSide!=0 && choice.FixedSide!=NextFixedSide)
            {SharedReject("fixed-side pattern requires other layout");return false;}
            float speed=r.Difficulty.ForwardSpeed+r.Difficulty.SpeedAdjustmentRange;
            if(choice.NeedsRoad && choice.RoadMin<sharedVisitStart-.01f) {SharedReject("moving approach/theme boundary");return false;}
            if(choice.Clear>r.Limit || choice.RoadMax>r.Limit) {SharedReject("transition/finish reservation");return false;}
            if(choice.Jump && choice.Decision-Radius<sharedLastJumpEnd) {SharedReject("jump flight/landing");return false;}
            float earliest=choice.Decision-Radius-speed*Time.fixedDeltaTime;
            float time=float.IsNegativeInfinity(sharedClear)?float.PositiveInfinity:(earliest-sharedClear)/Mathf.Max(.1f,speed);
            float steering=EncounterPlanner.SteeringTime(sharedExit,choice.X,LateralSpeed,Acceleration);
            if(time<0 || (Mathf.Abs(sharedExit-choice.X)>.05f && time<steering))
            {SharedReject("steering/previous hazard clearance");return false;}
            foreach(var prior in sharedReservations)
            {
                Bounds expanded=choice.Hazard;expanded.Expand(new Vector3(.1f,0,.1f));
                if(expanded.Intersects(prior.Hazard) && SharedTimeOverlap(choice,prior,r)) {SharedReject("hazard/moving trajectory overlap");return false;}
                bool overlap=choice.RoadMin<prior.RoadMax && choice.RoadMax>prior.RoadMin;
                if(overlap && (prior.Exclusive || choice.Exclusive) && choice.Decision<prior.RoadMax && choice.Clear>prior.RoadMin)
                {SharedReject("exclusive warning/jump area");return false;}
                if(choice.Plan.ReplacesTrack && prior.OrdinaryRoad && r.Origin<prior.RoadMax && r.Origin+choice.Plan.Track.Length>prior.RoadMin)
                {SharedReject("moving encounter requires ordinary road");return false;}
            }
            if(choice.NeedsRoad && choice.RoadMin<r.Origin)
                foreach(var prior in sharedReservations)
                    if(prior.Exclusive && prior.RoadMin<choice.RoadMax && prior.RoadMax>choice.RoadMin)
                    {SharedReject("approach road already replaced");return false;}
            return true;
        }
        private void AcceptSharedChoice(SharedChoice choice,CityPlanningRequest r,System.Random random,int groupSize)
        {
            CommitFixedSide(choice.FixedSide);
            // Count one decision row, not its decorative props or same-row companion.
            if(choice.Jump) sharedJumpDecisions++; else sharedNavigationDecisions++;
            float seconds=0;
            if(!float.IsNegativeInfinity(sharedLastDecision)) seconds=(choice.Decision-sharedLastDecision)/Mathf.Max(.1f,r.Difficulty.ForwardSpeed);
            Vector2 interval=SharedInterval(r.Difficulty,r.Library.SharedPacing);
            float target=Mathf.Max(.1f,Mathf.Lerp(interval.x,interval.y,(float)random.NextDouble()));
            if(captureDecisionDiagnostics)
                decisionDiagnostics.Add(new DecisionDiagnostic{Segment=r.Index,Identity=ChoiceIdentity(choice),Family=choice.Candidate.Family,
                    Prefab=choice.Variant.name,Layout=choice.Candidate.Source.name,DecisionZ=choice.Decision,ClearZ=choice.Clear,RecoveryEndZ=choice.Plan.EndZ,
                    PassageMin=choice.Low,PassageMax=choice.High,ExitX=choice.X,Jump=choice.Jump,Seconds=seconds,
                    TargetSeconds=target,GroupSize=groupSize,FixedSide=choice.FixedSide,Reason=seconds>interval.y+.1f?sharedDelayReason:"target"});
            sharedLastDecision=choice.Decision;
            nextSharedDecision=choice.Decision+target*r.Difficulty.ForwardSpeed;
            sharedClear=choice.Clear;sharedExit=choice.X;
            if(choice.Jump) sharedLastJumpEnd=choice.Clear;
            sharedCounts[choice.Candidate.Family]=SharedCount(choice.Candidate.Family)+1;
            sharedHistory.Add(new SharedHistory{Family=choice.Candidate.Family,Variant=choice.Variant,Decision=choice.Decision});
            if(++sharedSinceVarietyBreath>=8)
            {
                sharedSinceVarietyBreath=0;
                string first=null,second=null;bool varied=false;
                for(int i=Mathf.Max(0,sharedHistory.Count-8);i<sharedHistory.Count;i++)
                {
                    string family=sharedHistory[i].Family;
                    if(first==null) first=family;
                    else if(family!=first && second==null) second=family;
                    else if(family!=first && family!=second) {varied=true;break;}
                }
                if(r.Difficulty.Mode!=DifficultyMode.Hard && !varied && SharedFamilies(r.Library).Count>2)
                {
                    float nextX=sharedExit>=0?-r.Difficulty.RoadWidth*.25f:r.Difficulty.RoadWidth*.25f;
                    float speed=r.Difficulty.ForwardSpeed+r.Difficulty.SpeedAdjustmentRange;
                    nextSharedDecision=Mathf.Max(nextSharedDecision,sharedClear+Radius+speed*(EncounterPlanner.SteeringTime(sharedExit,nextX,LateralSpeed,Acceleration)+Time.fixedDeltaTime));
                    SharedReject("short reachable side-change breath after low variety");
                }
            }
            sharedReservations.Add(new SharedReservation{Hazard=choice.Hazard,RoadMin=choice.RoadMin,RoadMax=choice.RoadMax,
                OrdinaryRoad=choice.NeedsRoad,Exclusive=choice.Exclusive,
                ZSlope=choice.ZSlope,ZIntercept=choice.ZSlope==0?choice.Hazard.center.z:choice.ZIntercept,
                HalfDepth=choice.HalfDepth>0?choice.HalfDepth:choice.Hazard.extents.z,
                ActiveMin=r.Origin-r.SpawnAhead,ActiveMax=Mathf.Max(r.Origin+24,choice.Plan.EndZ)+r.RecycleBehind});
            planner.Commit(r.Index,choice.Clear,choice.X,choice.Candidate.Family);
            if(choice.Plan.CityVehicle is LongitudinalTrafficEncounter traffic) lastLongitudinalOncoming=traffic.Oncoming;
        }
        private static long ChoiceIdentity(SharedChoice choice) => choice.Plan.Encounters!=null && choice.Plan.Encounters.Count>0?
            choice.Plan.Encounters[0].EncounterId:choice.Plan.EncounterId;
        private void TrySharedCompanion(SharedChoice first,CityPlanningRequest r,System.Random random,List<EncounterPlan> ordinary)
        {
            foreach(var family in sharedFamilyOrder)
            {
                if(family.Name==first.Candidate.Family) continue;
                foreach(var candidate in family.Variants)
                {
                    if(candidate.Kind!=SharedKind.Fixed || candidate.Jump || !SharedVariety(candidate.Family,candidate.Source,first.Decision,false)) continue;
                    for(int side=0;side<2;side++)
                    {
                        // A companion shares this row's blocked side and does not consume
                        // the next encounter's side-pattern slot.
                        var companion=BuildSharedChoice(candidate,r,first.Decision,random,side,first.FixedSide);
                        if(companion==null || companion.Hazard.Intersects(first.Hazard)) continue;
                        float low=Mathf.Max(first.Low,companion.Low),high=Mathf.Min(first.High,companion.High);
                        if(low>high) continue;
                        float x=Mathf.Clamp(sharedRowPreviousExit,low,high);
                        float speed=r.Difficulty.ForwardSpeed+r.Difficulty.SpeedAdjustmentRange;
                        float time=(first.Decision-Radius-speed*Time.fixedDeltaTime-sharedRowPreviousClear)/Mathf.Max(.1f,speed);
                        if(!float.IsNegativeInfinity(sharedRowPreviousClear) && time<EncounterPlanner.SteeringTime(sharedRowPreviousExit,x,LateralSpeed,Acceleration)) continue;
                        bool conflict=false;
                        for(int i=0;i<sharedReservations.Count-1;i++)
                            if(sharedReservations[i].Hazard.Intersects(companion.Hazard) && SharedTimeOverlap(companion,sharedReservations[i],r)) {conflict=true;break;}
                        if(conflict) continue;
                        var original=ordinary[ordinary.Count-1];var added=companion.Plan.Encounters[0];
                        var placements=new Placement[original.Placements.Length+1];Array.Copy(original.Placements,placements,original.Placements.Length);
                        placements[placements.Length-1]=added.Placements[0];
                        TaskWindow window=original.Windows[0];
                        window.LandingStartZ=Mathf.Max(window.LandingStartZ,added.Windows[0].LandingStartZ);
                        window.DeadlineZ=Mathf.Max(window.DeadlineZ,added.Windows[0].DeadlineZ);
                        window.RecoveryEndZ=Mathf.Max(window.RecoveryEndZ,added.Windows[0].RecoveryEndZ);
                        ordinary[ordinary.Count-1]=new EncounterPlan(this,runVersion,r.Index,r.Road,placements,false,window.RecoveryEndZ,x,
                            r.Difficulty.RoadWidth,original.EncounterId,original.Family,new[]{window});
                        sharedHistory.Add(new SharedHistory{Family=candidate.Family,Variant=candidate.Source,Decision=first.Decision});
                        sharedCounts[candidate.Family]=SharedCount(candidate.Family)+1;
                        sharedReservations.Add(new SharedReservation{Hazard=companion.Hazard,RoadMin=companion.Decision,RoadMax=companion.Clear,
                            ZIntercept=companion.Hazard.center.z,HalfDepth=companion.Hazard.extents.z,ActiveMin=r.Origin-r.SpawnAhead,ActiveMax=Mathf.Max(r.Origin+24,window.RecoveryEndZ)+r.RecycleBehind});
                        sharedClear=Mathf.Max(sharedClear,companion.Clear);sharedExit=x;
                        planner.Commit(r.Index,sharedClear,x,original.Family);
                        if(captureDecisionDiagnostics)
                        {var d=decisionDiagnostics[decisionDiagnostics.Count-1];d.GroupSize=2;d.Family+=" + "+candidate.Family;d.Prefab+=" + "+candidate.Source.name;d.PassageMin=low;d.PassageMax=high;d.ExitX=x;d.ClearZ=sharedClear;}
                        return;
                    }
                }
            }
        }
        private EncounterPlan ShiftSharedPlan(EncounterPlan plan,float delta,TrackSegment track)
        {
            var placements=new Placement[plan.Placements.Length];
            for(int i=0;i<placements.Length;i++) {var p=plan.Placements[i];placements[i]=new Placement(p.Prefab,p.Position+Vector3.forward*delta,p.Jump,p.Phase,p.Speed,p.TaskIndex,p.YawDegrees);}
            return new EncounterPlan(this,plan.Version,plan.Index,track,placements,plan.IsGap,plan.EndZ,plan.ExitX,plan.Width,plan.EncounterId,plan.Family,plan.Windows,plan.GapTaskIndex);
        }
        private void AttachSharedExtras(TrackSegment segment,SegmentPlan plan,TrackGenerator generator)
        {
            if(plan.Encounters!=null) foreach(var encounter in plan.Encounters) AttachPlanned(segment,encounter,generator);
        }
        internal bool UsesSharedObstaclePacing => sharedLibrary != null;
        [ContextMenu("Log Active Encounter Inventory")]
        private void LogActiveEncounterInventory()
        {
            foreach(var entry in active)
                foreach(var pair in entry.Value)
                    Debug.Log($"Encounter payload: segment={entry.Key.name}, prefab={pair.Prefab.name}, position={pair.Instance.transform.position}, active={pair.Instance.activeInHierarchy}, planned Z={pair.PlannedStart:F2}..{pair.PlannedEnd:F2}",pair.Instance);
            foreach(var entry in activeCityVehicles)
                Debug.Log($"Vehicle encounter: segment={entry.Key.name}, family={entry.Value.FamilyId}, vehicle={entry.Value.Vehicle?.name}, position={entry.Value.Vehicle?.transform.position}",entry.Key);
            foreach(var entry in attachedEncounters)
                foreach(long identity in entry.Value) Debug.Log($"Encounter ownership: segment={entry.Key.name}, id={identity}",entry.Key);
        }
        internal void ProtectRecoveryArrival(float destinationZ,float clearThroughZ)
        {
            // Recovery is an explicit event, not padding added after every successful pass.
            // Existing support stays intact. Cancelled hazards are released by their normal owner.
            foreach(var entry in active)
                foreach(var pair in entry.Value)
                {
                    bool overlaps=pair.PlannedEnd>=destinationZ-Radius && pair.PlannedStart<=clearThroughZ;
                    foreach(var shape in pair.Colliders)
                        if(shape!=null && shape.enabled && shape.bounds.max.z>=destinationZ-Radius && shape.bounds.min.z<=clearThroughZ)
                        {overlaps=true;break;}
                    if(overlaps) pair.Instance.SetActive(false);
                }
            foreach(var entry in activeCityVehicles)
            {
                bool overlaps=activeCityWindows.TryGetValue(entry.Key,out var window) && window.RecoveryEndZ>=destinationZ-Radius && window.HazardStartZ<=clearThroughZ;
                foreach(var shape in entry.Value.Hazards)
                    if(shape!=null && shape.bounds.max.z>=destinationZ-Radius && shape.bounds.min.z<=clearThroughZ) {overlaps=true;break;}
                if(overlaps && entry.Value.Vehicle!=null) entry.Value.Vehicle.SetActive(false);
            }
            nextSharedDecision=Mathf.Max(nextSharedDecision,clearThroughZ);
        }
        private SegmentPlan PlanSharedSegment(CityPlanningRequest request)
        {
            var road=request.Road;
            if(!ready || road==null || request.Library==null || request.Library.SharedPacing==null) return default;
            if(sharedLibrary!=request.Library || sharedZone!=request.Zone)
            {
                FinishSharedVisit(); sharedCounts.Clear(); sharedHistory.Clear();
                sharedNavigationDecisions=sharedJumpDecisions=0;
                BeginFixedSidePattern(request.Index,request.Zone);
                sharedLibrary=request.Library;sharedZone=request.Zone;sharedVisitStart=request.Origin;sharedSinceVarietyBreath=0;
                nextSharedDecision=Mathf.Max(request.Origin+1,sharedClear);
            }
            if(request.Blocked)
            {
                SharedReject("protected startup/transition/finish/event");
                sharedReservations.Add(new SharedReservation{Hazard=new Bounds(new Vector3(0,1,request.Origin+12),new Vector3(request.Difficulty.RoadWidth,40,24)),
                    RoadMin=request.Origin,RoadMax=request.Origin+24,Exclusive=true});
                return new SegmentPlan(road,new List<EncounterPlan>());
            }
            sharedReservations.RemoveAll(r=>r.Hazard.max.z<request.Origin-192 && r.RoadMax<request.Origin);
            while(sharedHistory.Count>8 && sharedHistory[0].Decision<request.Origin-24) sharedHistory.RemoveAt(0);
            sharedSpecialCache.Clear();
            var random=new System.Random(unchecked(runSeed ^ request.Index*7919));
            var ordinary=new List<EncounterPlan>(8);
            SegmentPlan special=default;
            float cursor=Mathf.Max(request.Origin+1f,nextSharedDecision);
            int decisions=0;
            // Choose replacements before committing ordinary payloads. This avoids starving
            // junctions/barriers simply because an early static obstacle already used the road.
            var initialRandom = new System.Random(unchecked(runSeed ^ request.Index*3571));
            SharedChoice initial=null;
            bool balanceActions=request.Library.SharedPacing.navigationShare>0;
            bool preferNavigation=balanceActions && PreferSharedNavigation(request.Library);
            if(!preferNavigation && request.Gap!=null && GapTracksEnabled && initialRandom.NextDouble()<jumpFraction*runGapShare)
            {
                sharedAuthoredProbe=true;
                var gap=BuildSharedGap(request,cursor);
                sharedAuthoredProbe=false;
                if(gap!=null && SharedVariety("Gap",gap.Variant,gap.Decision,true) && ValidateSharedChoice(gap,request)) initial=gap;
            }
            sharedFamilyOrder.Clear();sharedFamilyOrder.AddRange(SharedFamilies(request.Library));
            SharedShuffle(sharedFamilyOrder,random);
            sharedFamilyOrder.Sort((a,b)=>SharedCount(a.Name).CompareTo(SharedCount(b.Name)));
            bool coverageDue=false;
            foreach(var family in sharedFamilyOrder) if(SharedCount(family.Name)<request.Library.SharedPacing.minimumFamilyAppearances) coverageDue=true;
            // Hard favours compact ordinary rows once special-family coverage is met.
            // Special candidates remain eligible in the ordinary search below.
            double specialChance=request.Difficulty.Mode==DifficultyMode.Hard
                ? (coverageDue?.5:.15) : (coverageDue?.7:.35);
            if(initial==null && random.NextDouble()<specialChance)
                foreach(var family in sharedFamilyOrder)
                {
                    // An unavailable underrepresented family must not starve other families.
                    SharedOrderVariants(family,random);
                    foreach(var candidate in sharedVariantOrder)
                    {
                        if(candidate.Kind==SharedKind.Fixed || candidate.Kind==SharedKind.Trap) continue;
                        sharedAuthoredProbe=true;
                        var choice=BuildSharedChoice(candidate,request,request.Origin-6f,random,0);
                        sharedAuthoredProbe=false;
                        if(balanceActions && choice!=null && choice.Jump==preferNavigation) continue;
                        if(choice==null || !SharedVariety(candidate.Family,choice.Variant,choice.Decision,false) || !ValidateSharedChoice(choice,request)) continue;
                        initial=choice;break;
                    }
                    if(initial!=null) break;
                }
            if(initial!=null)
            {
                bool replaces=initial.Plan.BeachBarrier!=null || initial.Plan.Junction!=null || initial.Plan.Roadworks!=null || initial.Plan.CityVehicle!=null || initial.Plan.IsGap;
                if(replaces) special=initial.Plan; else ordinary.AddRange(initial.Plan.Encounters);
                AcceptSharedChoice(initial,request,random,1);decisions++;
                cursor=Mathf.Max(nextSharedDecision,initial.Decision+.5f);
            }
            while(cursor<request.Origin+(special.Track!=null?special.Track.Length:24f)-1f && decisions<16)
            {
                SharedChoice accepted=null;
                bool quotaSteeringBlocked=false;
                preferNavigation=balanceActions && PreferSharedNavigation(request.Library);
                sharedFamilyOrder.Clear();sharedFamilyOrder.AddRange(SharedFamilies(request.Library));
                SharedShuffle(sharedFamilyOrder,random);
                for(int i=1;i<sharedFamilyOrder.Count;i++)
                {
                    var value=sharedFamilyOrder[i];int j=i-1;
                    while(j>=0 && SharedCount(sharedFamilyOrder[j].Name)>SharedCount(value.Name))
                    {sharedFamilyOrder[j+1]=sharedFamilyOrder[j];j--;}
                    sharedFamilyOrder[j+1]=value;
                }
                // Search the remaining segment for the due action before falling back.
                // Within each action, retain family variety and density-first selection.
                for(int actionPass=0;actionPass<(balanceActions?2:1) && accepted==null;actionPass++)
                for(int advance=0;advance<44 && accepted==null;advance++)
                for(int pass=0;pass<2 && accepted==null;pass++)
                {
                    // A family waiting for landing/steering room must not veto a
                    // different safe family on Hard. Coverage remains prioritised.
                    if(pass==1 && quotaSteeringBlocked && !balanceActions && request.Difficulty.Mode!=DifficultyMode.Hard) continue;
                    float sample=cursor+advance*.5f;
                    if(sample>=request.Origin+(special.Track!=null?special.Track.Length:24f)-1f) break;
                    foreach(var family in sharedFamilyOrder)
                    {
                        SharedOrderVariants(family,random);
                        foreach(var candidate in sharedVariantOrder)
                        {
                            bool wantedJump=actionPass==0?!preferNavigation:preferNavigation;
                            // Dungeon traps can share a family but require different actions.
                            if(balanceActions && (candidate.Kind==SharedKind.Fixed || candidate.Kind==SharedKind.Trap || candidate.Kind==SharedKind.Stone) &&
                                candidate.Jump!=wantedJump) continue;
                            if(special.Track!=null && (candidate.Kind==SharedKind.Beach || candidate.Kind==SharedKind.Junction ||
                                candidate.Kind==SharedKind.Roadworks || candidate.Kind==SharedKind.Vehicle)) continue;
                            for(int side=0;side<2;side++)
                            {
                                var choice=BuildSharedChoice(candidate,request,sample,random,side);
                                if(choice==null) continue;
                                if(balanceActions && choice.Jump!=wantedJump) continue;
                                if(!SharedVariety(candidate.Family,choice.Variant,choice.Decision,pass==1)) {SharedReject("variety cooldown");continue;}
                                if(choice.Plan.ReplacesTrack && ordinary.Count>0) {SharedReject("replacement requires uncommitted segment");continue;}
                                if(!ValidateSharedChoice(choice,request))
                                {
                                    if(pass==0 && SharedCount(candidate.Family)<request.Library.SharedPacing.minimumFamilyAppearances &&
                                        (sharedDelayReason=="steering/previous hazard clearance" || sharedDelayReason=="jump flight/landing")) quotaSteeringBlocked=true;
                                    continue;
                                }
                                accepted=choice;break;
                            }
                            if(accepted!=null) break;
                        }
                        if(accepted!=null) break;
                    }
                }
                if(accepted==null && ordinary.Count==0 && special.Track==null && request.Gap!=null && GapTracksEnabled &&
                    random.NextDouble()<jumpFraction*runGapShare && SharedVariety("Gap",request.Gap.gameObject,cursor,true))
                {
                    var gapChoice=BuildSharedGap(request,cursor);
                    if(gapChoice!=null && ValidateSharedChoice(gapChoice,request)) accepted=gapChoice;
                }
                if(accepted==null) {SharedReject(quotaSteeringBlocked?"passage/variety safety breathing":"no family fits in remaining road");break;}
                var plan=accepted.Plan;
                bool isSpecial=plan.BeachBarrier!=null || plan.Junction!=null || plan.Roadworks!=null || plan.CityVehicle!=null || plan.IsGap;
                if(isSpecial) special=plan; else ordinary.AddRange(plan.Encounters);
                sharedRowPreviousExit=sharedExit;sharedRowPreviousClear=sharedClear;
                AcceptSharedChoice(accepted,request,random,1);
                decisions++;
                if(!isSpecial && !accepted.Jump && accepted.Candidate.Kind==SharedKind.Fixed &&
                    request.Difficulty.Mode!=DifficultyMode.Easy && ordinary.Count>0)
                    TrySharedCompanion(accepted,request,random,ordinary);
                cursor=Mathf.Max(nextSharedDecision,accepted.Decision+.5f);
            }
            if(special.Track==null) return new SegmentPlan(road,ordinary);
            if(special.IsGap && special.Encounters!=null) ordinary.InsertRange(0,special.Encounters);
            float rootDelta=special.Track.EntryLocal.z;
            for(int i=0;i<ordinary.Count;i++)
                if(!ordinary[i].IsGap) ordinary[i]=ShiftSharedPlan(ordinary[i],rootDelta,special.Track);
            return new SegmentPlan(special.Track,ordinary,special.Roadworks,special.Index,special.EncounterId,
                special.RoadworksWindow,special.Junction,special.JunctionWindow,special.TrafficSeed,
                special.CityVehicle,special.VehiclePrefab,special.VehiclePosition,special.VehicleWindow,special.BeachBarrier);
        }
    }
}
