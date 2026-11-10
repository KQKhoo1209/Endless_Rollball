using System.Collections.Generic;
using EndlessRollball.Track;
using UnityEngine;

namespace EndlessRollball
{
    public sealed class PhoenixLavaCourse : MonoBehaviour
    {
        [SerializeField] private PhoenixLavaHazard eruptionPrefab;
        [SerializeField] private PhoenixLavaHazard bouncingBallPrefab;
        [SerializeField, Min(10f)] private float duration = 45f;
        private readonly List<PhoenixLavaHazard> hazards = new List<PhoenixLavaHazard>();
        private readonly List<float> encounterTimes = new List<float>();
        private readonly List<PhoenixLavaHazard> eruptions = new List<PhoenixLavaHazard>();
        private readonly List<PhoenixLavaHazard> balls = new List<PhoenixLavaHazard>();
        private int usedEruptions,usedBalls;

        public void SetDuration(float seconds) => duration = Mathf.Max(10f, seconds);

        internal void ScorePassed(SessionTaskTracker tracker,float elapsed)
        {
            for(int i=0;i<hazards.Count;i++)
                if(hazards[i].gameObject.activeSelf && elapsed>encounterTimes[i]+3f)
                    tracker.RecordEventNavigation(hazards[i].GetInstanceID(),true);
        }

        private PhoenixLavaHazard Acquire(bool ball)
        {
            var pool=ball?balls:eruptions;
            int index=ball?usedBalls++:usedEruptions++;
            if(index==pool.Count)
            {
                var instance=Instantiate(ball?bouncingBallPrefab:eruptionPrefab,transform);
                instance.Prepare();instance.gameObject.SetActive(false);pool.Add(instance);
            }
            return pool[index];
        }

        public bool Prepare()
        {
            if (eruptionPrefab == null || bouncingBallPrefab == null) return false;
            if(eruptions.Count==0) Acquire(false);
            if(balls.Count==0) Acquire(true);
            usedEruptions=usedBalls=0;
            return true;
        }

        public void Show(Vector3 start, float centreX, float speed, float width,
            float flightHeight, Transform carrier, float lavaSurfaceHeight = -.6f,
            int seed=0,DifficultyMode difficulty=DifficultyMode.Normal,float progression=0f,
            float steeringSpeed=5f,float steeringAcceleration=20f,Vector3 carrierExtents=default)
        {
            foreach(var old in hazards) old.gameObject.SetActive(false);
            hazards.Clear();encounterTimes.Clear();usedEruptions=usedBalls=0;
            transform.SetPositionAndRotation(new Vector3(centreX,lavaSurfaceHeight,start.z),Quaternion.identity);
            var random=new System.Random(unchecked(seed^Mathf.RoundToInt(start.z*100)^0x1A7A));
            var kinds=new List<int>();
            Vector2 interval=CarrierObstaclePacing.Interval(difficulty,progression,true);
            if(carrierExtents==default) carrierExtents=new Vector3(1,1,2);
            float time=7f+(float)random.NextDouble(),previousTime=0,previousDepth=0,previousPassage=0;
            int previousKind=-1,previousSide=random.Next(2)==0?-1:1;
            while(time<duration-5f && hazards.Count<64)
            {
                int kind=CarrierObstaclePacing.Draw(kinds,3,random,previousKind);
                bool ball=kind==2;
                Bounds bounds=CarrierObstaclePacing.Footprint(ball?bouncingBallPrefab:eruptionPrefab);
                int side=random.NextDouble()<.8?-previousSide:previousSide;
                float x=ball || random.NextDouble()<.2?0:side*width*.25f;
                if(!CarrierObstaclePacing.TryPassage(bounds,x,width,carrierExtents,-side*width*.25f,out float passage))
                {time+=interval.y;continue;}
                if(hazards.Count>0)
                    time=Mathf.Max(time,previousTime+CarrierObstaclePacing.Clearance(previousDepth,bounds.extents.z,
                        carrierExtents,previousPassage,passage,speed,steeringSpeed,steeringAcceleration));
                if(time>=duration-5f) break;
                PhoenixLavaHazard hazard=Acquire(ball);
                Vector3 position=new Vector3(centreX+x,lavaSurfaceHeight,start.z+time*speed);
                hazard.gameObject.SetActive(true);
                hazard.ResetEncounter(position,time,flightHeight,centreX,random.Next(2)==0?-1f:1f);
                hazards.Add(hazard);encounterTimes.Add(time);
                previousTime=time;previousDepth=bounds.extents.z;previousPassage=passage;
                previousSide=side;previousKind=kind;
                time+=CarrierObstaclePacing.Range(random,interval);
            }
            gameObject.SetActive(true);
            Tick(0f);
            EndlessRollball.Infrastructure.RuntimeLog.Info(
                $"[LavaCourse] {difficulty}, encounters={hazards.Count}, target interval={interval.x:F1}-{interval.y:F1}s, seed={seed}.",this);
        }

        public void Tick(float time)
        {
            foreach(PhoenixLavaHazard hazard in hazards)
                if(hazard.gameObject.activeSelf) hazard.Tick(time);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
