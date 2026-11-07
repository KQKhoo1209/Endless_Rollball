using UnityEngine;
using EndlessRollball.Track;

namespace EndlessRollball
{
    [RequireComponent(typeof(GameplayRunController))]
    public sealed class StreetBouncePadSpawner : MonoBehaviour
    {
        [SerializeField]
        private BouncePadLauncher bouncePadPrefab;

        [SerializeField]
        private CloudPlatformEvent cloudEvent;

        private GameplayRunController run;
        private GameSequenceCoordinator sequence;
        private BouncePadLauncher pad;
        private TrackSegment padRoad;
        private float? targetZ;
        private bool placed;

        private void Awake()
        {
            run = GetComponent<GameplayRunController>();
            sequence = GetComponent<GameSequenceCoordinator>();
        }

        private void Update()
        {
            if (run.State != SessionFlowState.Running || pad == null ||
                pad.HasLaunched || !pad.gameObject.activeInHierarchy)
                return;

            BallMovement player = run.PlayerBall;
            // The trigger can be cleared by a jump. Keep the compulsory entry.
            if (player != null && player.transform.position.z >= pad.transform.position.z)
                pad.TryLaunch(player);
        }

        internal void PlanPlacement(float streetStartZ, float forwardSpeed)
        {
            if (targetZ.HasValue || bouncePadPrefab == null || cloudEvent == null ||
                sequence == null || !sequence.Eligible)
                return;

            sequence.Window(out _, out float target, out _);
            targetZ = streetStartZ + target * Mathf.Max(.1f, forwardSpeed);
        }

        internal bool ReservesRoad(float centreZ) =>
            targetZ.HasValue && Mathf.Abs(centreZ - targetZ.Value) < 48f;

        internal void PlaceOnRoad(TrackSegment road)
        {
            if (placed || !targetZ.HasValue || run.PlayerBall == null)
                return;

            Vector3 position = (road.EntryAnchor.position + road.ExitAnchor.position) * .5f;
            if (!ReservesRoad(position.z) ||
                position.z < targetZ.Value - TrackSegment.ContractLength * .5f)
                return;

            BallMovement player = run.PlayerBall;
            position.x = player.Corridor.CentreX;

            pad = Instantiate(
                bouncePadPrefab,
                position,
                Quaternion.identity,
                road.PresentationTransform);

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

            padRoad = road;
            placed = true;
        }

        internal void ReleaseRoad(TrackSegment road)
        {
            if (padRoad != road) return;
            if (pad != null)
            {
                pad.gameObject.SetActive(false);
                Destroy(pad.gameObject);
            }
            pad = null;
            padRoad = null;
        }

        internal void ResetPlacement()
        {
            ReleaseRoad(padRoad);
            targetZ = null;
            placed = false;
        }
    }
}
