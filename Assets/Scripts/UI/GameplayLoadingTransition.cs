using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>One black overlay spanning the menu/scene boundary; no asset dependencies.</summary>
public sealed class GameplayLoadingTransition : MonoBehaviour
{
    private const float FadeOutSeconds = .25f;
    private const float FadeInSeconds = .45f;
    private const float RenderSettleSeconds = .25f;
    private const float LoadingTargetSeconds = 5f;
    private static GameplayLoadingTransition instance;
    private CanvasGroup cover;
    private Camera watchedCamera;
    private int renderedFrames, lastRenderedFrame = -1;
    private bool loadingScene, targetReported;
    private double beganAt;

    private static GameplayLoadingTransition GetOrCreate()
    {
        if (instance != null) return instance;
        var root = new GameObject("Gameplay Loading Fade", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasGroup), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var imageObject = new GameObject("Black Cover", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(root.transform, false);
        var rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        imageObject.GetComponent<Image>().color = Color.black;
        instance = root.AddComponent<GameplayLoadingTransition>();
        instance.cover = root.GetComponent<CanvasGroup>();
        instance.cover.alpha = 0f;
        instance.cover.blocksRaycasts = true;
        instance.beganAt = Time.realtimeSinceStartupAsDouble;
        DontDestroyOnLoad(root);
        return instance;
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += CameraRendered;
        SceneManager.sceneLoaded += SceneLoaded;
    }

    private void CameraRendered(ScriptableRenderContext context, Camera camera)
    {
        if (camera != watchedCamera || Time.frameCount == lastRenderedFrame) return;
        lastRenderedFrame = Time.frameCount;
        renderedFrames++;
    }

    private void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "GameplayScene") Destroy(gameObject);
    }

    public static void LoadGameplay()
    {
        var transition = GetOrCreate();
        if (transition.loadingScene) return;
        transition.loadingScene = true;
        transition.StartCoroutine(transition.LoadScene());
    }

    private IEnumerator LoadScene()
    {
        yield return Fade(1f, FadeOutSeconds);
        // Let the fully opaque cover render before beginning scene deserialization.
        yield return null;
        AsyncOperation request = SceneManager.LoadSceneAsync("GameplayScene");
        if (request == null)
        {
            Debug.LogError("Gameplay scene loading could not be started.");
            yield return Fade(0f, FadeInSeconds);
            Destroy(gameObject);
            yield break;
        }
        yield return request;
        // The generator, not sceneLoaded, owns the readiness/reveal decision.
    }

    public static void CoverPreparation()
    {
        GetOrCreate().cover.alpha = 1f;
    }

    public static IEnumerator RevealPreparedScene(Camera camera)
    {
        var transition = GetOrCreate();
        transition.cover.alpha = 1f;
        transition.watchedCamera = camera;
        transition.renderedFrames = 0;
        transition.lastRenderedFrame = -1;
        double readyAt = Time.realtimeSinceStartupAsDouble;
        // URP must submit the gameplay camera twice AFTER the last initial segment
        // is activated. Keep physics/scoring stopped throughout this handoff.
        while (transition != null && (transition.renderedFrames < 2 ||
            Time.realtimeSinceStartupAsDouble - readyAt < RenderSettleSeconds))
            yield return null;
        if (transition == null) yield break;
        transition.watchedCamera = null;
        yield return transition.Fade(0f, FadeInSeconds);
        if (transition != null) Destroy(transition.gameObject);
    }

    public static void AbortPreparation()
    {
        if (instance != null) Destroy(instance.gameObject);
    }

    private IEnumerator Fade(float alpha, float duration)
    {
        float from = cover.alpha;
        double start = Time.realtimeSinceStartupAsDouble;
        while (Time.realtimeSinceStartupAsDouble - start < duration)
        {
            float t = (float)((Time.realtimeSinceStartupAsDouble - start) / duration);
            cover.alpha = Mathf.Lerp(from, alpha, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        cover.alpha = alpha;
    }

    private void Update()
    {
        if (!targetReported && Time.realtimeSinceStartupAsDouble - beganAt > LoadingTargetSeconds)
        {
            targetReported = true;
            Debug.LogWarning("[Loading] The 5-second target was exceeded. Waiting for preparation/rendering; gameplay will not start on an unfinished environment.", this);
        }
    }

    private void OnDestroy()
    {
        RenderPipelineManager.endCameraRendering -= CameraRendered;
        SceneManager.sceneLoaded -= SceneLoaded;
        if (instance == this) instance = null;
    }
}
