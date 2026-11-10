using UnityEngine;

namespace EndlessRollball
{
    public sealed class WaterSurfCourse : MonoBehaviour
    {
        internal const float SeaSurfaceOffset = -.25f;
        internal float SurfaceHeight { get; private set; } = SeaSurfaceOffset;
        [Header("References")]
        [SerializeField] private Transform waterSurface;
        [SerializeField] private WhaleSurfObstacle rockPrefab;
        [SerializeField] private WhaleSurfObstacle buoyPrefab;
        [SerializeField] private WhaleSurfObstacle[] boatPrefabs;

        [Header("Course")]
        [SerializeField, Min(1f)] private float duration = 45f;
        [SerializeField, Min(0f)] private float endPadding = 30f;
        [SerializeField, Min(18f)] private float seaVisualWidth = 200f;

        private readonly System.Collections.Generic.List<WhaleSurfObstacle> obstacles = new System.Collections.Generic.List<WhaleSurfObstacle>();
        private readonly System.Collections.Generic.Dictionary<WhaleSurfObstacle,System.Collections.Generic.List<WhaleSurfObstacle>> pools =
            new System.Collections.Generic.Dictionary<WhaleSurfObstacle,System.Collections.Generic.List<WhaleSurfObstacle>>();
        private readonly System.Collections.Generic.Dictionary<WhaleSurfObstacle,int> used = new System.Collections.Generic.Dictionary<WhaleSurfObstacle,int>();

        private WhaleSurfObstacle Acquire(WhaleSurfObstacle prefab)
        {
            if(!pools.TryGetValue(prefab,out var pool))
            {pool=new System.Collections.Generic.List<WhaleSurfObstacle>();pools.Add(prefab,pool);}
            int index=used.TryGetValue(prefab,out int count)?count:0;
            if(index==pool.Count)
            {
                var instance=Instantiate(prefab,transform);
                instance.PrepareWake();instance.gameObject.SetActive(false);pool.Add(instance);
            }
            used[prefab]=index+1;
            return pool[index];
        }

        internal void ScorePassed(EndlessRollball.Track.SessionTaskTracker tracker, float rearZ)
        {
            foreach (WhaleSurfObstacle obstacle in obstacles)
                if (obstacle.TryGetContactBounds(out Bounds bounds) && bounds.max.z < rearZ)
                    tracker.RecordEventNavigation(obstacle.GetInstanceID(), true);
        }

        internal bool TryFindRecovery(WhaleSurfObstacle failed, Bounds whale, float centreX,
            float width, float limitZ, float clearance, out Vector3 destination)
        {
            destination = whale.center;
            float startZ = whale.center.z + clearance;
            if (failed.TryGetContactBounds(out Bounds hit))
                startZ = Mathf.Max(startZ, hit.max.z + whale.extents.z + clearance);
            float bestZ = float.PositiveInfinity;
            float halfWidth = Mathf.Max(0, width * .5f - whale.extents.x - clearance);
            for (int lane = 0; lane < 4; lane++)
            {
                float x = lane == 0 ? whale.center.x : lane == 1 ? centreX :
                    centreX + (lane == 2 ? -halfWidth : halfWidth);
                x = Mathf.Clamp(x, centreX - halfWidth, centreX + halfWidth);
                float z = startZ;
                // Every blocking encounter advances Z, so this search is bounded.
                for (int pass = 0; pass <= obstacles.Count && z <= limitZ; pass++)
                {
                    float nextZ = z;
                    foreach (WhaleSurfObstacle obstacle in obstacles)
                    {
                        if (!obstacle.TryGetContactBounds(out Bounds bounds)) continue;
                        if (x + whale.extents.x + clearance > bounds.min.x &&
                            x - whale.extents.x - clearance < bounds.max.x &&
                            z + whale.extents.z + clearance > bounds.min.z &&
                            z - whale.extents.z - clearance < bounds.max.z)
                            nextZ = Mathf.Max(nextZ, bounds.max.z + whale.extents.z + clearance);
                    }
                    if (Mathf.Approximately(nextZ, z))
                    {
                        if (z < bestZ)
                        {
                            bestZ = z;
                            destination = new Vector3(x, whale.center.y, z);
                        }
                        break;
                    }
                    z = nextZ;
                }
            }
            return !float.IsPositiveInfinity(bestZ);
        }

        internal void ExcludeRecoverySkips(EndlessRollball.Track.SessionTaskTracker tracker,
            float oldZ, float newRearZ)
        {
            foreach (WhaleSurfObstacle obstacle in obstacles)
                if (obstacle.TryGetContactBounds(out Bounds bounds) &&
                    bounds.max.z > oldZ && bounds.max.z < newRearZ)
                    tracker.ExcludeEventNavigation(obstacle.GetInstanceID());
        }

        internal Material SurfaceMaterial => waterSurface != null &&
            waterSurface.TryGetComponent(out Renderer renderer) ? renderer.sharedMaterial : null;

        public bool Prepare()
        {
            if (waterSurface == null ||
                rockPrefab == null ||
                buoyPrefab == null)
            {
                Debug.LogWarning(
                    "Assign the water surface, rock and buoy prefabs.",
                    this);
                return false;
            }

            if (boatPrefabs == null || boatPrefabs.Length < 5)
            {
                Debug.LogWarning(
                    "Assign all five boat prefabs.", this);
                return false;
            }

            for (int i = 0; i < boatPrefabs.Length; i++)
            {
                if (boatPrefabs[i] == null)
                {
                    Debug.LogWarning(
                        $"Boat Prefabs element {i} is empty.", this);
                    return false;
                }
            }

            if (pools.Count>0)
                return true;
            Acquire(rockPrefab);Acquire(buoyPrefab);
            foreach(var boat in boatPrefabs) if(boat!=null) Acquire(boat);
            used.Clear();
            return true;
        }

        public void Show(
            Vector3 start,
            float centreX,
            float speed,
            float channelWidth,
            float waterHeight,
            Transform surfer, int seed=0, EndlessRollball.Track.DifficultyMode difficulty=EndlessRollball.Track.DifficultyMode.Normal,
            float progression=0f,float steeringSpeed=7f,float steeringAcceleration=22f,Vector3 carrierExtents=default)
        {
            transform.SetPositionAndRotation(
                new Vector3(centreX, waterHeight, start.z),
                Quaternion.identity);

            SurfaceHeight = waterHeight + SeaSurfaceOffset;
            float travelLength = speed * duration;

            waterSurface.localPosition =
                new Vector3(0f, SeaSurfaceOffset, travelLength * .5f);

            // Unity's default Plane is 10 × 10 metres.
            waterSurface.localScale = new Vector3(
                Mathf.Max(seaVisualWidth, channelWidth + 6f) / 10f,
                1f,
                (travelLength + endPadding * 2f) / 10f);

            foreach(var old in obstacles) old.gameObject.SetActive(false);
            obstacles.Clear();used.Clear();
            var random=new System.Random(unchecked(seed^Mathf.RoundToInt(start.z*100)^0x51A7));
            var families=new System.Collections.Generic.List<int>();
            var boats=new System.Collections.Generic.List<int>();
            Vector2 interval=CarrierObstaclePacing.Interval(difficulty,progression,false);
            float time=6f+(float)random.NextDouble(),previousTime=0,previousDepth=0,previousPassage=0;
            int previousFamily=-1,previousBoat=-1,previousSide=random.Next(2)==0?-1:1;
            if(carrierExtents==default) carrierExtents=new Vector3(1,1,2);
            while(time<duration-5f && obstacles.Count<64)
            {
                int family=CarrierObstaclePacing.Draw(families,3,random,previousFamily);
                WhaleSurfObstacle prefab=family==0?rockPrefab:family==1?buoyPrefab:
                    boatPrefabs[previousBoat=CarrierObstaclePacing.Draw(boats,boatPrefabs.Length,random,previousBoat)];
                Bounds footprint=CarrierObstaclePacing.Footprint(prefab);
                int side=random.NextDouble()<.8?-previousSide:previousSide;
                float x=side*channelWidth*.25f-footprint.center.x;
                // Keep a complete carrier-width opening beside stationary/approaching props.
                if(footprint.size.x+2*carrierExtents.x+.4f>channelWidth) {time+=interval.y;continue;}
                x=Mathf.Clamp(x,-channelWidth*.5f-footprint.min.x,channelWidth*.5f-footprint.max.x);
                float occupiedX=prefab.CrossesChannel?-x:x;
                float preferred=(prefab.CrossesChannel?side:-side)*channelWidth*.25f;
                if(!CarrierObstaclePacing.TryPassage(footprint,occupiedX,channelWidth,carrierExtents,preferred,out float passage))
                {time+=interval.y;continue;}
                if(obstacles.Count>0)
                    time=Mathf.Max(time,previousTime+CarrierObstaclePacing.Clearance(previousDepth,footprint.extents.z,
                        carrierExtents,previousPassage,passage,speed,steeringSpeed,steeringAcceleration));
                if(time>=duration-5f) break;
                var obstacle=Acquire(prefab);
                obstacle.transform.localPosition=new Vector3(x,0,(time+prefab.EncounterTimeOffset(speed))*speed-footprint.center.z);
                obstacle.gameObject.SetActive(true);
                obstacle.ConfigureMovement(surfer,speed,centreX,SurfaceMaterial,SurfaceHeight);
                obstacles.Add(obstacle);
                previousTime=time;previousDepth=footprint.extents.z;previousPassage=passage;
                previousSide=side;previousFamily=family;
                time+=CarrierObstaclePacing.Range(random,interval);
            }

            gameObject.SetActive(true);
            EndlessRollball.Infrastructure.RuntimeLog.Info(
                $"[SurfCourse] {difficulty}, encounters={obstacles.Count}, target interval={interval.x:F1}-{interval.y:F1}s, seed={seed}.",this);
        }

        public void SetDuration(float seconds)
        {
            duration = Mathf.Max(1f, seconds);
        }

        public void SetWaterSurfaceVisible(bool visible)
        {
            if (waterSurface != null)
                waterSurface.gameObject.SetActive(visible);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
