using UnityEngine;

namespace EndlessRollball
{
    public sealed class WaterSurfCourse : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform waterSurface;
        [SerializeField] private WhaleSurfObstacle rockPrefab;
        [SerializeField] private WhaleSurfObstacle buoyPrefab;
        [SerializeField] private WhaleSurfObstacle[] boatPrefabs;

        [Header("Course")]
        [SerializeField, Min(1f)] private float duration = 45f;
        [SerializeField, Min(0f)] private float endPadding = 30f;
        [SerializeField, Min(18f)] private float seaVisualWidth = 200f;

        // -1 = rock, -2 = buoy, 0–4 = boat array index.
        private static readonly int[] PrefabPattern =
        {
            -1, -2, 0, 1, -1, 2, 3, 4, -2
        };

        private static readonly int[] LanePattern =
        {
            -1, 1, 0, -1, 1, -1, -1, 1, 0
        };

        private static readonly float[] EncounterTimes =
        {
            8f, 12f, 16f, 20f, 24f, 28f, 32f, 36f, 40f
        };

        private WhaleSurfObstacle[] obstacles;

        internal void ScorePassed(EndlessRollball.Track.SessionTaskTracker tracker,float elapsed)
        {
            if(obstacles==null)return;
            for(int i=0;i<obstacles.Length;i++)
                if(obstacles[i].gameObject.activeSelf && elapsed>EncounterTimes[i]+3f)
                    tracker.RecordEventNavigation(obstacles[i].GetInstanceID(),true);
        }

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

            for (int i = 0; i < 5; i++)
            {
                if (boatPrefabs[i] == null)
                {
                    Debug.LogWarning(
                        $"Boat Prefabs element {i} is empty.", this);
                    return false;
                }
            }

            if (obstacles != null)
                return true;

            obstacles = new WhaleSurfObstacle[PrefabPattern.Length];

            for (int i = 0; i < obstacles.Length; i++)
            {
                int selection = PrefabPattern[i];

                WhaleSurfObstacle prefab;

                if (selection == -1)
                    prefab = rockPrefab;
                else if (selection == -2)
                    prefab = buoyPrefab;
                else
                    prefab = boatPrefabs[selection];

                obstacles[i] = Instantiate(prefab, transform);
                obstacles[i].name =
                    $"{prefab.name} - Encounter {i + 1}";

                obstacles[i].gameObject.SetActive(false);
            }

            return true;
        }

        public void Show(
            Vector3 start,
            float centreX,
            float speed,
            float channelWidth,
            float waterHeight,
            Transform surfer)
        {
            transform.SetPositionAndRotation(
                new Vector3(centreX, waterHeight, start.z),
                Quaternion.identity);

            float travelLength = speed * duration;

            waterSurface.localPosition =
                new Vector3(0f, .05f, travelLength * .5f);

            // Unity's default Plane is 10 × 10 metres.
            waterSurface.localScale = new Vector3(
                Mathf.Max(seaVisualWidth, channelWidth + 6f) / 10f,
                1f,
                (travelLength + endPadding * 2f) / 10f);

            float laneSpacing = channelWidth * .25f;

            for (int i = 0; i < obstacles.Length; i++)
            {
                float encounterTime = EncounterTimes[i];

                obstacles[i].transform.localPosition = new Vector3(
                    LanePattern[i] * laneSpacing,
                    0f,
                    encounterTime * speed);

                obstacles[i].gameObject.SetActive(
                    encounterTime < duration - 4f);

                obstacles[i].ConfigureMovement(
                    surfer, speed, centreX);
            }

            gameObject.SetActive(true);
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
