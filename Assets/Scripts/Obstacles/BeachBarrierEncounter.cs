using UnityEngine;
namespace EndlessRollball.Track
{
    [DisallowMultipleComponent, RequireComponent(typeof(TrackSegment))]
    public sealed class BeachBarrierEncounter : MonoBehaviour
    {
        [SerializeField] private bool explosive;
        [SerializeField] private Transform rollerVisual;
        [SerializeField] private Rigidbody rollerBody;
        [SerializeField] private BoxCollider rollerCollider;
        [SerializeField] private GameObject deckPatch;
        [SerializeField] private ParticleSystem[] warning = System.Array.Empty<ParticleSystem>();
        [SerializeField] private ParticleSystem[] burst = System.Array.Empty<ParticleSystem>();
        [SerializeField] private ParticleSystem[] edgeFire = System.Array.Empty<ParticleSystem>();
        private BallMovement ball;
        private bool running, exploded;
        private float elapsed, travelSeconds, detonationLocalZ;
        [SerializeField] private BoxCollider fallSensor;
        private SessionTaskTracker tracker;
        private TrackSegment track;
        // Aim a normal jump so its apex is over the centre of the opening.
        // Reveal the gap two seconds before that takeoff, with a physics-step allowance.
        public static float ExplosionTriggerLocalZ(float maximumSpeed, float jumpSpeed, float radius)
        {
            float speed=Mathf.Max(1f,maximumSpeed);
            return 12f-radius-speed*(2f+jumpSpeed/Mathf.Max(.1f,-Physics.gravity.y)+2f*Time.fixedDeltaTime);
        }
        public static float RollingAngle(float distance, float radius) => -distance/Mathf.Max(.01f,radius)*Mathf.Rad2Deg;
        public bool IsFallSensor(Collider shape) => explosive && exploded && shape == fallSensor;
        public bool Explosive => explosive;
        public Collider Hazard => rollerCollider;
        public string Family => explosive ? "Beach.ExplosiveRoller" : "Beach.RollingBarrier";
        public void Configure(bool isExplosive, Transform visual, Rigidbody body, BoxCollider shape,
            GameObject patch, ParticleSystem[] flames, ParticleSystem[] explosion, ParticleSystem[] edges, BoxCollider sensor = null)
        { explosive=isExplosive; rollerVisual=visual; rollerBody=body; rollerCollider=shape;
          deckPatch=patch; warning=flames; burst=explosion; edgeFire=edges; fallSensor=sensor; }
        public void Prepare(BallMovement player, float maximumSpeed, bool paused, SessionTaskTracker scoring = null)
        {
            ball=player; elapsed=0; exploded=false; tracker=scoring; track=GetComponent<TrackSegment>();
            float localPlayer=transform.InverseTransformPoint(player.transform.position).z;
            detonationLocalZ=ExplosionTriggerLocalZ(maximumSpeed,player.JumpSpeed,player.SphereRadius);
            // Both bodies start moving immediately. A slower approach may leave the barrel
            // burning at its impact point until the player reaches the visible warning distance.
            travelSeconds=explosive ? Mathf.Max(1.2f,(detonationLocalZ-localPlayer)/Mathf.Max(1f,maximumSpeed))
                : Mathf.Max(.25f,(12f-localPlayer)/Mathf.Max(1f,maximumSpeed));
            if(fallSensor!=null) fallSensor.enabled=false;
            if(deckPatch!=null) deckPatch.SetActive(true);
            rollerVisual.gameObject.SetActive(true); rollerCollider.enabled=true;
            rollerBody.position=transform.TransformPoint(new Vector3(0,explosive?.55f:.3f,22f));
            rollerBody.rotation=transform.rotation;
            rollerVisual.localRotation=Quaternion.identity;
            foreach(var p in GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach(var p in warning) p.Play(true);
            SetRunning(!paused);
        }
        public void SetRunning(bool value)
        {
            running=value;
            foreach(var p in GetComponentsInChildren<ParticleSystem>(true))
                if(value) { if(p.isPaused) p.Play(true); } else if(p.isPlaying) p.Pause(true);
        }
        private void FixedUpdate()
        {
            if(!running || ball==null || exploded) return;
            elapsed+=Time.fixedDeltaTime;
            float z=explosive ? Mathf.Lerp(22f,12f,Mathf.Clamp01(elapsed/travelSeconds))
                              : Mathf.Max(2f,22f-10f*elapsed/travelSeconds);
            rollerBody.MovePosition(transform.TransformPoint(new Vector3(0,explosive?.55f:.3f,z)));
            rollerVisual.localRotation=Quaternion.Euler(RollingAngle(22f-z,explosive?.55f:.3f),0,0);
            if(!explosive) tracker?.UpdateMovingJumpWindow(track,transform.position.z+z-.3f,transform.position.z+z+.3f);
            if(explosive && elapsed>=travelSeconds &&
                transform.InverseTransformPoint(ball.transform.position).z>=detonationLocalZ)
            {
                exploded=true; rollerCollider.enabled=false;
                rollerVisual.gameObject.SetActive(false);
                foreach(var p in warning) p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                // Visual patch and its supporting collider change in the same fixed step.
                deckPatch.SetActive(false);
                if(fallSensor!=null) fallSensor.enabled=true;
                foreach(var p in burst) p.Play(true);
                foreach(var p in edgeFire) p.Play(true);
            }
        }
        private void OnDisable()
        { running=false; ball=null; exploded=false; elapsed=0; tracker=null;
          if(deckPatch!=null) deckPatch.SetActive(true);
          foreach(var p in GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); }
    }
}
