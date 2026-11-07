using UnityEngine;

namespace EndlessRollball
{
    public sealed class PhoenixLavaCourse : MonoBehaviour
    {
        [SerializeField] private PhoenixLavaHazard eruptionPrefab;
        [SerializeField] private PhoenixLavaHazard bouncingBallPrefab;
        [SerializeField, Min(10f)] private float duration = 45f;
        private PhoenixLavaHazard[] hazards;
        private static readonly float[] Times = { 12, 16, 20, 28, 32, 36 };
        private static readonly int[] Lanes = { 0, 1, 0, -1, 0, 1 };
        private static readonly int[] Kinds = { 1, 1, 2, 1, 2, 1 };

        public void SetDuration(float seconds) => duration = Mathf.Max(10f, seconds);

        internal void ScorePassed(EndlessRollball.Track.SessionTaskTracker tracker,float elapsed)
        {
            if(hazards==null)return;
            for(int i=0;i<hazards.Length;i++)
                if(hazards[i].gameObject.activeSelf && elapsed>Times[i]+3f)
                    tracker.RecordEventNavigation(hazards[i].GetInstanceID(),true);
        }

        public bool Prepare()
        {
            if (eruptionPrefab == null || bouncingBallPrefab == null) return false;
            if (hazards != null) return true;
            hazards = new PhoenixLavaHazard[Times.Length];
            for (int i = 0; i < hazards.Length; i++)
            {
                PhoenixLavaHazard prefab = Kinds[i] == 1 ? eruptionPrefab : bouncingBallPrefab;
                hazards[i] = Instantiate(prefab, transform);
                hazards[i].Prepare();
                hazards[i].gameObject.SetActive(false);
            }
            return true;
        }

        public void Show(Vector3 start, float centreX, float speed, float width,
            float flightHeight, Transform carrier, float lavaSurfaceHeight = -.6f)
        {
            transform.SetPositionAndRotation(new Vector3(centreX, lavaSurfaceHeight, start.z), Quaternion.identity);
            for (int i = 0; i < hazards.Length; i++)
            {
                Vector3 position = new Vector3(centreX + Lanes[i] * width * .25f,
                    lavaSurfaceHeight, start.z + Times[i] * speed);
                hazards[i].gameObject.SetActive(Times[i] < duration - 5f);
                hazards[i].ResetEncounter(position, Times[i], flightHeight, centreX, i == 4 ? -1f : 1f);
            }
            gameObject.SetActive(true);
            Tick(0f);
        }

        public void Tick(float time)
        {
            if (hazards == null) return;
            foreach (PhoenixLavaHazard hazard in hazards)
                if (hazard.gameObject.activeSelf) hazard.Tick(time);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
