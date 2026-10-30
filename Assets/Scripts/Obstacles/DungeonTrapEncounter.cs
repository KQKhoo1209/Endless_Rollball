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
            }
            else
            {
                // Rubble accelerates downward; sliding blocks retain their current motion.
                float movement = kind == TrapKind.Rubble
                    ? progress * progress
                    : progress;

                body.MovePosition(
                    transform.TransformPoint(
                        Vector3.Lerp(initialPosition, settledPosition, movement)));
            }

            // Play once when a pillar or rubble finishes falling.
            if (progress >= 1f &&
                !impactPlayed &&
                (fallsOnApproach || kind == TrapKind.Rubble))
            {
                impactPlayed = true;

                if (impactDust != null)
                    impactDust.Play(true);
            }
        }
        private void OnDisable()
        { running=false; started=false; elapsed=0; player=null;
          foreach(var p in GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
    }
}
