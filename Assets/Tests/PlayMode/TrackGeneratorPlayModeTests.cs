using System.Collections;
using System.Linq;
using EndlessRollball.Track;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EndlessRollball.Tests
{
    public sealed class TrackGeneratorPlayModeTests
    {
        [UnityTest]
        public IEnumerator GameplaySceneStartsWithVisualProceduralTrackAndNoGapFillingPlane()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("GameplayScene", LoadSceneMode.Single);
            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;

            Assert.That(GameObject.Find("Plane"), Is.Null);

            TrackGenerator generator = Object.FindFirstObjectByType<TrackGenerator>();
            Assert.That(generator, Is.Not.Null);
            Assert.That(generator.ActiveSegmentCount, Is.InRange(7, 9));
            Assert.That(generator.CurrentSeed, Is.EqualTo(24001));
            Assert.That(generator.TrackIdentifier, Is.EqualTo("easy-v2-24001"));

            TrackSegment[] activeSegments = Object.FindObjectsByType<TrackSegment>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            Assert.That(activeSegments, Is.Not.Empty);
            Assert.That(
                activeSegments.All(segment =>
                    segment.GetComponentsInChildren<Collider>(true).Length > 0 &&
                    segment.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.enabled)),
                Is.True);
        }

        [UnityTest]
        public IEnumerator MovingObstacleReturnsToFirstWaypointAfterPoolReactivation()
        {
            TrackCatalog catalog = Resources.Load<TrackCatalog>("Track/TrackCatalog");
            TrackSegment prefab = catalog.Segments.First(segment => segment.SegmentId == "TRK_Moving_Barrier");
            TrackSegment instance = Object.Instantiate(prefab);

            try
            {
                DifficultySnapshot difficulty = new DifficultySnapshot(
                    DifficultyMode.Normal,
                    7.5f,
                    3.5f,
                    0.6f,
                    1.5f,
                    0f);
                instance.Activate(difficulty);
                MovingObstacle obstacle = instance.GetComponentInChildren<MovingObstacle>(true);
                Vector3 firstWaypoint = obstacle.transform.localPosition;

                yield return new WaitForSeconds(0.25f);
                Assert.That(obstacle.transform.localPosition, Is.Not.EqualTo(firstWaypoint));

                instance.Deactivate();
                instance.Activate(difficulty);
                Assert.That(obstacle.transform.localPosition, Is.EqualTo(firstWaypoint));
                Assert.That(obstacle.CurrentWaypointIndex, Is.Zero);
            }
            finally
            {
                Object.Destroy(instance.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator GeneratorRecyclesSegmentsAndEasyResetIsRepeatable()
        {
            TrackCatalog catalog = Resources.Load<TrackCatalog>("Track/TrackCatalog");
            DifficultyProfile[] profiles = Resources.LoadAll<DifficultyProfile>("Track");
            DifficultyProfile easy = profiles.FirstOrDefault(profile => profile.Mode == DifficultyMode.Easy);

            Assert.That(catalog, Is.Not.Null);
            Assert.That(easy, Is.Not.Null);

            GameObject player = new GameObject("TrackTestPlayer");
            GameObject system = new GameObject("TrackTestSystem");
            GameObject pool = new GameObject("Pool");
            pool.transform.SetParent(system.transform);

            DifficultyManager difficulty = system.AddComponent<DifficultyManager>();
            difficulty.ConfigureForScene(player.transform, null, profiles, DifficultyMode.Easy);
            TrackRunConfig run = difficulty.Configure(DifficultyMode.Easy);

            TrackGenerator generator = system.AddComponent<TrackGenerator>();
            generator.ConfigureForScene(player.transform, catalog, difficulty, pool.transform);
            generator.Initialize(run);

            yield return null;

            Assert.That(generator.ActiveSegmentCount, Is.InRange(7, 9));
            string[] initialSequence = generator.GeneratedSequence.Take(7).ToArray();

            for (int index = 1; index <= 200; index++)
            {
                player.transform.position = new Vector3(0f, 0f, index * TrackSegment.ContractLength);
                generator.Tick();
                Assert.That(generator.ActiveSegmentCount, Is.LessThanOrEqualTo(9));
            }

            generator.ResetRun();
            CollectionAssert.AreEqual(initialSequence, generator.GeneratedSequence.Take(7).ToArray());

            Object.Destroy(system);
            Object.Destroy(player);
            yield return null;
        }
    }
}
