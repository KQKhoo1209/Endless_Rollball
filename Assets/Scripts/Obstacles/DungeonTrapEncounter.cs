using UnityEngine;
namespace EndlessRollball.Track
{
    // One encounter owner; particles and support geometry never register separate tasks.
    public sealed class DungeonTrapEncounter : MonoBehaviour
    {
        public enum TrapKind { Fire, Rubble, SlidingBlock, PillarJump, PillarCollapse }
        [SerializeField] private TrapKind kind;
        [SerializeField] private Rigidbody body;
        [SerializeField] private BoxCollider hazard;
        [SerializeField] private Vector3 settledPosition;
        [SerializeField] private Vector3 initialPosition;
        [SerializeField] private GameObject warning;
        [SerializeField] private bool fallsOnApproach;
        [SerializeField] private Vector3 fallPivot;
        [SerializeField] private float standingAngle;
        [SerializeField, Min(.2f)] private float pillarFallSeconds=1.4f;
        [SerializeField] private ParticleSystem impactDust;
        private bool impactPlayed;
        private BallMovement player;
        private bool running, started;
        private float elapsed, triggerZ;
        public TrapKind Kind => kind;
        public bool RequiredJump => kind == TrapKind.Fire || kind == TrapKind.PillarJump;
        public string Family => kind==TrapKind.PillarCollapse ? "Dungeon.CollapsedPillar" : "Dungeon." + kind;
        public bool FallsOnApproach => fallsOnApproach;
        public float ApproachLeadSeconds => 2f+(fallsOnApproach?pillarFallSeconds:.8f);
        public void ConfigureImpact(ParticleSystem dust) {impactDust=dust;pillarFallSeconds=1.4f;}
        public BoxCollider Hazard => hazard;
        public Bounds SettledBounds => new Bounds(settledPosition+hazard.center,hazard.size);
        public bool CanClearJump(float minimumSpeed,float maximumSpeed,float jumpSpeed,float radius,float step)
        {
            Bounds bounds=SettledBounds;
            float gravity=Mathf.Max(.1f,-Physics.gravity.y);
            float discriminant=jumpSpeed*jumpSpeed-2*gravity*(bounds.max.y+.08f);
            return discriminant>0 && 2*Mathf.Sqrt(discriminant)/gravity*minimumSpeed >= bounds.size.z+2*radius+maximumSpeed*step+.2f;
        }
        public float PassageX => RequiredJump ? 0 : -Mathf.Sign(SettledBounds.center.x)*1.5f;
        public void ConfigureFall(Vector3 pivot,float angle)
        { fallsOnApproach=true; fallPivot=pivot; standingAngle=angle; }
        public void Configure(TrapKind type, Rigidbody mover, BoxCollider shape, Vector3 initial, Vector3 settled, GameObject marker)
        { kind=type; body=mover; hazard=shape; initialPosition=initial; settledPosition=settled; warning=marker; }
        public void Prepare(BallMovement ball, float maximumSpeed, bool paused)
        {
            player=ball; elapsed=0; started=false;impactPlayed=false;
            body.position=transform.TransformPoint(initialPosition);
            body.rotation=transform.rotation;
            if(fallsOnApproach)
            {
                Quaternion standing=Quaternion.Euler(0,0,standingAngle);
                body.position=transform.TransformPoint(fallPivot+standing*(settledPosition-fallPivot));
                body.rotation=transform.rotation*standing;
            }
            body.isKinematic=true; body.useGravity=false;
            hazard.enabled=true;
            triggerZ=transform.position.z+SettledBounds.min.z-maximumSpeed*(ApproachLeadSeconds+2*Time.fixedDeltaTime)-ball.SphereRadius;
            if(warning!=null) warning.SetActive(kind==TrapKind.Fire);
            foreach(var p in GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            if(kind==TrapKind.Fire) foreach(var p in GetComponentsInChildren<ParticleSystem>(true)) p.Play();
            SetRunning(!paused);
        }
        public void SetRunning(bool value)
        {
            running=value;
            foreach(var p in GetComponentsInChildren<ParticleSystem>(true))
                if(value) { if(p.isPaused) p.Play(); } else if(p.isPlaying) p.Pause();
        }
        private void FixedUpdate()
        {
            if(!running || player==null || (RequiredJump && !fallsOnApproach)) return;
            if(!started && player.transform.position.z>=triggerZ)
            {
                started=true; if(warning!=null) warning.SetActive(true);
                foreach(var p in GetComponentsInChildren<ParticleSystem>(true))
                    if(impactDust==null || !p.transform.IsChildOf(impactDust.transform)) p.Play();
            }
            if(!started) return;
            elapsed+=Time.fixedDeltaTime;
            float progress=Mathf.Clamp01((elapsed-2f)/(fallsOnApproach?pillarFallSeconds:.8f));
            if(fallsOnApproach)
            {
                Quaternion tilt=Quaternion.Euler(0,0,Mathf.Lerp(standingAngle,0,Mathf.Pow(progress,2.5f)));
                body.MovePosition(transform.TransformPoint(fallPivot+tilt*(settledPosition-fallPivot)));
                body.MoveRotation(transform.rotation*tilt);
                if(progress>=1 && !impactPlayed)
                { impactPlayed=true; if(impactDust!=null) impactDust.Play(); }
            }
            else body.MovePosition(transform.TransformPoint(Vector3.Lerp(initialPosition,settledPosition,progress)));
        }
        private void OnDisable()
        { running=false; started=false; elapsed=0; player=null;
          foreach(var p in GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
    }
}
