using UnityEngine;
using EndlessRollball.Track;

namespace EndlessRollball
{
    [RequireComponent(typeof(GameplayRunController))]
    public sealed class StreetBouncePadSpawner : MonoBehaviour
    {
        [Min(0f)]
        public float spawnAfterSeconds = 60f;

        [SerializeField]
        private BouncePadLauncher bouncePadPrefab;

        [SerializeField]
        private CloudPlatformEvent cloudEvent;

        private GameplayRunController run;
        private GameSequenceCoordinator sequence;
        private BouncePadLauncher pad;
        private float nextScanAt;
        private bool spawned;

        private void Awake()
        {
            run = GetComponent<GameplayRunController>();
            sequence=GetComponent<GameSequenceCoordinator>();
        }

        private void Update()
        {
            if(run.State==SessionFlowState.Preparing) {if(pad!=null)Destroy(pad.gameObject);pad=null;spawned=false;nextScanAt=0;return;}
            if(sequence!=null) {
                if(!sequence.Available || !sequence.IsStreet)return;
                sequence.Window(out float earliest,out float target,out float latest);
                if(sequence.Elapsed>latest) {sequence.Notify(GameSequenceCoordinator.SequencePhase.RoadAfterEvent);Debug.LogWarning("[Sequence] Cloud entry window missed. Check clear Street approach and launch prefab.",this);return;}
                // The pad lies 25-80 m ahead. Prepare visibly before arrival.
                if(sequence.Elapsed<target-55f/Mathf.Max(.1f,run.PlayerBall.ForwardSpeed))return;
            }
            if (run.State != SessionFlowState.Running)
                return;

            BallMovement player = run.PlayerBall;

            if (pad != null)
            {
                // Preserve compulsory entry if the player jumps
                // above the pad's trigger.
                if (!pad.HasLaunched &&
                    player.transform.position.z >= pad.transform.position.z)
                {
                    pad.TryLaunch(player);
                }

                return;
            }

            if (spawned ||
                bouncePadPrefab == null ||
                cloudEvent == null ||
                !cloudEvent.IsReady ||
                (sequence==null && run.ActiveDurationSeconds < spawnAfterSeconds) ||
                Time.unscaledTime < nextScanAt)
                return;

            nextScanAt = Time.unscaledTime + .5f;

            if (!run.Generator.TryFindCloudGateway(
                    player.transform.position.z,
                    run.Tasks,
                    out Vector3 position,
                    out _))
                return;

            position.x = player.Corridor.CentreX;

            pad = Instantiate(
                bouncePadPrefab,
                position,
                Quaternion.identity,
                transform);

            pad.Configure(cloudEvent);

            BoxCollider trigger = pad.GetComponent<BoxCollider>();

            Vector3 centre = trigger.center;
            centre.x = 0f;
            trigger.center = centre;

            float width =
                trigger.size.x * Mathf.Abs(pad.transform.lossyScale.x);

            if (width > .001f)
            {
                Vector3 scale = pad.transform.localScale;
                scale.x *= player.Corridor.Width / width;
                pad.transform.localScale = scale;
            }

            spawned = true;
        }
    }
}