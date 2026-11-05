using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using EndlessRollball.Track;

namespace EndlessRollball
{
    [DisallowMultipleComponent, RequireComponent(typeof(GameplayRunController))]
    public sealed class StreetCloudEventBridge : MonoBehaviour
    {
        [SerializeField, Min(10f)] private float streetSecondsBeforeEvent = 60f;
        [SerializeField, Min(.1f)] private float fadeSeconds = .4f;
        private GameplayRunController run;
        private CloudPlatformEvent cloud;
        private Scene streetScene, cloudScene;
        private readonly List<Behaviour> hidden = new List<Behaviour>();
        private readonly List<bool> enabledBefore = new List<bool>();
        private GameObject launch;
        private Material launchMaterial;
        private Camera streetCamera;
        private bool launchActive, busy, returning;
        private int lastOfferedZone = -1;
        private double nextEventAt;
        private float nextScanAt, cover;
        private string loadError;
        private Material savedSkybox;
        private AmbientMode savedAmbientMode;
        private Color savedAmbientLight;
        private float savedAmbientIntensity;
        private bool savedFog;
        private Coroutine transition;

        private void Start()
        {
            run = GetComponent<GameplayRunController>();
            streetScene = gameObject.scene;
            nextEventAt = streetSecondsBeforeEvent;
        }

        private void Update()
        {
            if (GetComponent<GameSequenceCoordinator>() != null) return;
            if (run == null || busy || run.State != SessionFlowState.Running) return;
            BallMovement ball = run.PlayerBall;
            if (launchActive)
            {
                if (!run.Generator.HasVisibleCityRoad(launch.transform.position.z - 5f, launch.transform.position.z + 10f))
                {
                    launch.SetActive(false); launchActive = false;
                    return;
                }
                if (ball.transform.position.z >= launch.transform.position.z)
                {
                    launch.SetActive(false); launchActive = false;
                    if (run.Tasks.IsSpecialEventAreaClear(ball.transform.position.z - 2f, ball.transform.position.z + 15f) && run.TryBeginSpecialEvent())
                        transition = StartCoroutine(EnterClouds());
                }
                return;
            }
            if (run.ActiveDurationSeconds < nextEventAt || Time.unscaledTime < nextScanAt) return;
            nextScanAt = Time.unscaledTime + .5f;

            bool sceneReady = Application.CanStreamedLevelBeLoaded("CloudEvent");
            bool roadFound = run.Generator.TryFindCloudGateway(
                ball.transform.position.z, run.Tasks,
                out Vector3 point, out int zone);

            EndlessRollball.Infrastructure.RuntimeLog.Info(
                $"[CloudCheck] sceneReady={sceneReady}, roadFound={roadFound}, " +
                $"zone={zone}, previousZone={lastOfferedZone}");

            if (!sceneReady || !roadFound || zone == lastOfferedZone) return;
            if (launch == null) BuildLaunch();
            point.x = ball.Corridor.CentreX;
            launch.transform.position = point;
            launch.SetActive(true);
            launchActive = true;
            lastOfferedZone = zone;
            streetCamera = Camera.main;
        }

        private void BuildLaunch()
        {
            launch = new GameObject("Street Cloud Launch");
            SceneManager.MoveGameObjectToScene(launch, streetScene);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            launchMaterial = new Material(shader) { color = new Color(.8f, .94f, 1f) };
            // Harmless visual cue spanning the road: entry is compulsory, with no lane choice.
            for (int i = 0; i < 7; i++)
            {
                var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                puff.name = "Launch Cloud";
                puff.transform.SetParent(launch.transform, false);
                puff.transform.localPosition = new Vector3((i - 3) * 1.05f, .1f, 0f);
                puff.transform.localScale = new Vector3(1.5f, .5f, 2f);
                puff.GetComponent<Collider>().enabled = false;
                puff.GetComponent<Renderer>().sharedMaterial = launchMaterial;
            }
        }

        private IEnumerator Fade(float target)
        {
            while (!Mathf.Approximately(cover, target))
            {
                cover = Mathf.MoveTowards(cover, target, Time.unscaledDeltaTime / fadeSeconds);
                yield return null;
            }
        }

        private IEnumerator EnterClouds()
        {
            busy = true;
            loadError = null;
            yield return Fade(1f);
            savedSkybox = RenderSettings.skybox;
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbientLight = RenderSettings.ambientLight;
            savedAmbientIntensity = RenderSettings.ambientIntensity;
            savedFog = RenderSettings.fog;
            HideStreetPresentation();
            SceneManager.sceneLoaded += HandleCloudLoaded;
            AsyncOperation load = null;
            try { load = SceneManager.LoadSceneAsync("CloudEvent", LoadSceneMode.Additive); }
            catch (Exception exception) { loadError = exception.Message; }
            if (load != null) yield return load;
            SceneManager.sceneLoaded -= HandleCloudLoaded;
            double deadline = Time.realtimeSinceStartupAsDouble + 20d;
            while (cloud != null && !cloud.IsReady && cloud.PreparationError == null && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            if (cloud == null || !cloud.IsReady)
            {
                Debug.LogWarning("Cloud event unavailable; safely resuming Street. " + (loadError ?? cloud?.PreparationError), this);
                yield return ReturnToStreet(0d);
                yield break;
            }
            yield return Fade(0f);
            cloud.ResumeAfterEntry();
            transition = null;
        }

        private void HandleCloudLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "CloudEvent" || !busy) return;
            cloudScene = scene;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                cloud = root.GetComponentInChildren<CloudPlatformEvent>(true);
                if (cloud != null) break;
            }
            if (cloud == null) return;
            SceneManager.SetActiveScene(scene);
            cloud.ConfigureEmbedded(run.TrackDifficulty);
            cloud.Completed += RequestReturn;
            cloud.ReturnRequested += RequestReturn;
        }

        private void RequestReturn()
        {
            if (!busy || returning || transition != null) return;
            transition = StartCoroutine(ReturnToStreet(cloud?.ActiveSeconds ?? 0d));
        }

        private IEnumerator ReturnToStreet(double playedSeconds)
        {
            returning = true;
            yield return Fade(1f);
            if (cloud != null)
            {
                cloud.Completed -= RequestReturn;
                cloud.ReturnRequested -= RequestReturn;
            }
            if (streetScene.IsValid() && streetScene.isLoaded) SceneManager.SetActiveScene(streetScene);
            if (cloudScene.IsValid() && cloudScene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(cloudScene);
                if (unload != null) yield return unload;
            }
            cloud = null;
            RestoreStreetPresentation();
            run.EndSpecialEvent(playedSeconds);
            nextEventAt = run.ActiveDurationSeconds + streetSecondsBeforeEvent;
            yield return Fade(0f);
            busy = returning = false;
            transition = null;
        }

        private void HideStreetPresentation()
        {
            hidden.Clear(); enabledBefore.Clear();
            foreach (GameObject root in streetScene.GetRootGameObjects())
                foreach (Behaviour component in root.GetComponentsInChildren<Behaviour>(true))
                    if (component is Camera || component is AudioListener || component is Canvas || component is Light || component is Volume)
                    {
                        hidden.Add(component); enabledBefore.Add(component.enabled);
                        component.enabled = false;
                    }
        }

        private void RestoreStreetPresentation()
        {
            for (int i = 0; i < hidden.Count; i++) if (hidden[i] != null) hidden[i].enabled = enabledBefore[i];
            hidden.Clear(); enabledBefore.Clear();
            RenderSettings.skybox = savedSkybox;
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientLight = savedAmbientLight;
            RenderSettings.ambientIntensity = savedAmbientIntensity;
            RenderSettings.fog = savedFog;
        }

        private void OnGUI()
        {
            if (launchActive && !busy && streetCamera != null && launch != null)
            {
                Vector3 point = streetCamera.WorldToScreenPoint(launch.transform.position + Vector3.up * 3f);
                if (point.z > 0f) GUI.Box(new Rect(point.x - 140, Screen.height - point.y, 280, 45), "Cloud piano ahead\n45-second sky road");
            }
            if (cover <= 0f) return;
            int oldDepth = GUI.depth;
            Color old = GUI.color;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, cover);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old; GUI.depth = oldDepth;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleCloudLoaded;
            if (transition != null) StopCoroutine(transition);
            if (cloud != null) { cloud.Completed -= RequestReturn; cloud.ReturnRequested -= RequestReturn; }
            if (busy)
            {
                if (streetScene.IsValid() && streetScene.isLoaded) SceneManager.SetActiveScene(streetScene);
                if (cloudScene.IsValid() && cloudScene.isLoaded) SceneManager.UnloadSceneAsync(cloudScene);
                RestoreStreetPresentation();
                if (run != null) run.EndSpecialEvent(0d);
            }
            if (launch != null) launch.SetActive(false);
            busy = returning = launchActive = false;
        }

        private void OnDestroy()
        {
            if (launch != null) Destroy(launch);
            if (launchMaterial != null) Destroy(launchMaterial);
        }
    }
}
