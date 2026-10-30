using UnityEngine;

namespace EndlessRollball.Track
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    public sealed class DungeonRollingStoneBall : MonoBehaviour
    {
        public const string FamilyId = "Dungeon.RollingStone";
        [Header("References")]
        [SerializeField] private Transform stoneVisual;
        [SerializeField] private SphereCollider stoneCollider;
        [Header("Movement")]
        [SerializeField, Min(.1f)] private float approachingSpeed = 4f;
        [SerializeField, Min(0f)] private float sidewaysDistance = .45f;
        [SerializeField, Min(.5f)] private float sidewaysPeriod = 4f;
        [Tooltip("Maximum travel ahead of the encounter centre, in metres. This bounds the reserved road, without a distance activation gate.")]
        [SerializeField, Min(1f)] private float approachTravel = 36f;

        private Rigidbody body;
        private Transform player;
        private TrackGenerator generator;
        private Renderer[] renderers;
        private bool[] rendererDefaults;
        private Vector3 origin, previousPosition, forward, up;
        private Quaternion roadRotation, visualRestRotation;
        private float elapsed, radius, ratio, phase, omega, greatestProgress, clearProgress, visibilityTimer, travelLimit;
        private bool cached, prepared, running, finished, presentationVisible = true;
        public SphereCollider Hazard => stoneCollider;
        public float SidewaysDistance => sidewaysDistance;
        public float AuthoredRadius
        {
            get
            {
                if(stoneCollider==null) return 0f;
                Vector3 scale=stoneCollider.transform.lossyScale;
                return stoneCollider.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
            }
        }
        public float ClearanceProgress(float playerRadius) => Mathf.Max(8f,AuthoredRadius+playerRadius+1f);
        public float PositionAtProgress(float progress, float plannedPlayerSpeed)
        {
            float scaled=progress*approachingSpeed/Mathf.Max(.1f,plannedPlayerSpeed);
            return -scaled/(1f+Mathf.Abs(scaled)/Mathf.Max(1f,approachTravel));
        }

        private void Awake() => CacheReferences();
        private void CacheReferences()
        {
            if(cached) return;
            body=GetComponent<Rigidbody>();
            if(body==null || stoneVisual==null || stoneCollider==null) return;
            visualRestRotation=stoneVisual.localRotation;
            renderers=GetComponentsInChildren<Renderer>(true);
            rendererDefaults=new bool[renderers.Length];
            for(int i=0;i<renderers.Length;i++) rendererDefaults[i]=renderers[i].enabled;
            cached=true;
        }

        // Compatibility preview: unbound, constant-speed motion in a temporary scene.
        public void Prepare(float speed)
        {
            Prepare(null,1f,0f,false,null);
            ratio=Mathf.Max(.1f,speed);
        }

        public void Prepare(BallMovement ball, float plannedPlayerSpeed, float seededPhase, bool paused, TrackGenerator trackGenerator, DungeonRollingStoneBall settings = null)
        {
            // Pooled clones retain old serialized values. Refresh authored movement
            // on acquisition so prefab edits also take effect after retry/reuse.
            if(settings!=null && settings!=this)
            {
                approachingSpeed=settings.approachingSpeed;
                sidewaysDistance=settings.sidewaysDistance;
                sidewaysPeriod=settings.sidewaysPeriod;
                approachTravel=settings.approachTravel;
            }
            CacheReferences();
            if(!cached)
            {
                Debug.LogError("Rolling stone needs its Rigidbody, visible sphere and solid SphereCollider assigned.",this);
                return;
            }
            player=ball!=null?ball.transform:null;
            generator=trackGenerator;
            origin=transform.position;
            roadRotation=transform.rotation;
            forward=roadRotation*Vector3.forward;
            up=roadRotation*Vector3.up;
            radius=Mathf.Max(.01f,AuthoredRadius);
            ratio=approachingSpeed/Mathf.Max(.1f,plannedPlayerSpeed);
            phase=seededPhase;
            travelLimit=Mathf.Max(1f,approachTravel);
            omega=2f*Mathf.PI/Mathf.Max(.5f,sidewaysPeriod);
            elapsed=0f;finished=false;visibilityTimer=0f;
            clearProgress=ClearanceProgress(ball!=null?ball.SphereRadius:.5f);
            greatestProgress=player!=null?Vector3.Dot(player.position-origin,forward):0f;
            stoneVisual.localRotation=visualRestRotation;
            if(!body.isKinematic) {body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            body.isKinematic=true;body.useGravity=false;
            stoneCollider.enabled=true;
            prepared=true;running=!paused;
            previousPosition=EvaluatePosition();
            // Teleport once at pooled preparation, before simulation/rendering.
            body.position=previousPosition;
            transform.position=previousPosition;
            UpdateVisibility();
        }

        public void SetRunning(bool value) => running=prepared && !finished && value;
        public void SetPresentationVisible(bool value)
        {
            if(presentationVisible==value) return;
            presentationVisible=value;
            if(cached) UpdateVisibility();
        }
        private Vector3 EvaluatePosition()
        {
            float scaled=greatestProgress*ratio;
            float z=player!=null ? -scaled/(1f+Mathf.Abs(scaled)/travelLimit) : -ratio*elapsed;
            return origin+roadRotation*new Vector3(Mathf.Sin(elapsed*omega+phase)*sidewaysDistance,0f,z);
        }
        private void FixedUpdate()
        {
            if(!prepared || !running) return;
            elapsed+=Time.fixedDeltaTime;
            if(player!=null) greatestProgress=Mathf.Max(greatestProgress,Vector3.Dot(player.position-origin,forward));
            Vector3 position=EvaluatePosition();
            Vector3 movement=position-previousPosition;
            body.MovePosition(position);
            if(movement.sqrMagnitude>.000001f)
            {
                Vector3 axis=Vector3.Cross(up,movement.normalized);
                stoneVisual.rotation=Quaternion.AngleAxis(movement.magnitude/radius*Mathf.Rad2Deg,axis)*stoneVisual.rotation;
            }
            previousPosition=position;
            visibilityTimer+=Time.fixedDeltaTime;
            if(visibilityTimer>=.1f) {visibilityTimer=0f;UpdateVisibility();}
            if(player!=null && greatestProgress>=clearProgress)
            {
                finished=true;running=false;
                stoneCollider.enabled=false;
            }
        }
        private void UpdateVisibility()
        {
            bool visible=prepared && presentationVisible && (generator==null || generator.HasVisibleDungeonRoad(previousPosition.z-radius,previousPosition.z+radius));
            for(int i=0;i<renderers.Length;i++)
                if(renderers[i]!=null) renderers[i].enabled=visible && rendererDefaults[i];
        }
        [ContextMenu("Preview Movement In Play Mode")]
        private void PreviewMovement() {if(Application.isPlaying) Prepare(approachingSpeed);}
        private void OnDisable()
        {
            running=false;prepared=false;finished=false;elapsed=0f;
            player=null;generator=null;greatestProgress=0f;
            if(!cached) return;
            stoneVisual.localRotation=visualRestRotation;
            stoneCollider.enabled=true;
            for(int i=0;i<renderers.Length;i++) if(renderers[i]!=null) renderers[i].enabled=rendererDefaults[i];
        }
    }
}
