using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using EndlessRollball.Scenery;
using EndlessRollball.Track;
namespace EndlessRollball.Infrastructure
{
    public sealed class GameplayAssetPreloader : MonoBehaviour
    {
        private static GameplayAssetPreloader instance;
        private readonly Dictionary<string, ResourceRequest> requests = new Dictionary<string, ResourceRequest>();
        private GameMode mode;
        private DifficultyMode difficulty;
        private ThreadPriority previousPriority;
        private bool ownsPriority, handedOff;
        private int completed, hits;
        private double started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        public static void BeginTutorial()
        {
            if (!GameSessionSettings.HasValidSelection) return;
            if (instance != null && instance.Matches()) return;
            Release();
            var go = new GameObject("Gameplay Asset Preloader");
            instance = go.AddComponent<GameplayAssetPreloader>();
            DontDestroyOnLoad(go);
            instance.mode = GameSessionSettings.SelectedMode.Value;
            instance.difficulty = GameSessionSettings.TrackDifficulty;
            instance.started = Time.realtimeSinceStartupAsDouble;
            instance.previousPriority = Application.backgroundLoadingPriority;
            instance.ownsPriority = true;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            instance.StartCoroutine(instance.Prepare());
        }
        private bool Matches() => GameSessionSettings.HasValidSelection &&
            mode == GameSessionSettings.SelectedMode.Value && difficulty == GameSessionSettings.TrackDifficulty;
        private ResourceRequest Request(string path)
        {
            if (!requests.TryGetValue(path, out var request))
            { request = Resources.LoadAsync(path); requests.Add(path, request); }
            return request;
        }
        public static bool TryAcquire(string path, out ResourceRequest request)
        {
            request = null;
            if (instance == null || !instance.Matches() || !instance.requests.TryGetValue(path, out request)) return false;
            if (request.isDone && request.asset == null) { instance.requests.Remove(path); request = null; return false; }
            instance.hits++;
            return true;
        }
        private IEnumerator Load(string path)
        {
            var request = Request(path);
            yield return request;
            if (request.asset == null) Debug.LogWarning("[Loading] Tutorial preload failed: " + path + "; gameplay may retry.");
            else completed++;
            RuntimeLog.Info($"[Loading] Preload {path}: complete={completed}, elapsed={Time.realtimeSinceStartupAsDouble-started:F3}s.");
            yield return null;
        }
        private IEnumerator Prepare()
        {
            yield return Load("GameSequence");
            if (mode == GameMode.Standard)
                yield return Load(ThemePath(GameSequenceConfiguration.Current.Order(difficulty)[0]));
            else
            {
                var counts = new int[3];
                if (mode == GameMode.Endless) { counts[0]=counts[1]=counts[2]=1; }
                else for (int i=0;i<100;i++) counts[(int)EnvironmentJourneyPlanner.FastPacedThemeAtRoll(difficulty,i)]++;
                for (int n=0;n<3;n++)
                {
                    int best=0;
                    for (int i=1;i<3;i++) if(counts[i]>counts[best]) best=i;
                    if(counts[best]<=0) break;
                    counts[best]=0;
                    yield return Load(ThemePath((EnvironmentTheme)best));
                }
            }
            yield return Load("GameplayPreload/Manifest");
            var manifest = Request("GameplayPreload/Manifest").asset as GameplayPreloadManifest;
            if (manifest != null && manifest.sharedGroups != null)
                foreach (var path in manifest.sharedGroups) yield return Load(path);
            RestorePriority();
            RuntimeLog.Info($"[Loading] Tutorial preload finished: {completed} groups in {Time.realtimeSinceStartupAsDouble-started:F3}s.");
        }
        private static string ThemePath(EnvironmentTheme theme) => "EnvironmentScenery/" + (theme == EnvironmentTheme.Water ? "Beach" : theme.ToString());
        public static void ContinueToGameplay()
        {
            if (instance == null) return;
            if (!instance.Matches()) { Release(); return; }
            instance.handedOff = true;
            instance.RestorePriority();
            int pending=0;
            foreach(var request in instance.requests.Values) if(!request.isDone) pending++;
            RuntimeLog.Info($"[Loading] Continue: preload completed={instance.completed}, pending={pending}, cache hits={instance.hits}.");
        }
        private void Update()
        {
            if (!Matches() || (SceneManager.GetActiveScene().name == "MainMenuScene")) Release();
        }
        public static void TutorialDestroyed() { if(instance != null && !instance.handedOff) Release(); }
        public static void Release()
        {
            if (instance == null) return;
            var owner=instance; instance=null;
            owner.StopAllCoroutines(); owner.RestorePriority(); owner.requests.Clear(); Destroy(owner.gameObject);
        }
        private void RestorePriority()
        { if(ownsPriority) { Application.backgroundLoadingPriority=previousPriority; ownsPriority=false; } }
        private void OnDestroy() { RestorePriority(); if(instance==this) instance=null; }
    }
}
