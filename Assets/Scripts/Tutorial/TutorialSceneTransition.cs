using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EndlessRollball.Tutorial
{
    // Independent of the procedural gameplay readiness protocol.
    public sealed class TutorialSceneTransition : MonoBehaviour
    {
        public const string SceneName = "TutorialScene";
        private static TutorialSceneTransition instance;
        public static bool IsLoading => instance != null;
        private CanvasGroup cover;

        public static bool Load(string destination, Action<string> failed)
        {
            if (instance != null) return false;
            if (!Application.CanStreamedLevelBeLoaded(destination))
            {
                failed?.Invoke($"Add {destination} to the active build scene list, then retry.");
                return false;
            }
            var root = new GameObject("Tutorial Loading Fade", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(TutorialSceneTransition));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = short.MaxValue;
            TutorialView.Panel(root.transform, "Cover", Vector2.zero, Vector2.one, Color.black);
            instance = root.GetComponent<TutorialSceneTransition>();
            instance.cover = root.GetComponent<CanvasGroup>();
            instance.cover.alpha = 0;
            DontDestroyOnLoad(root);
            instance.StartCoroutine(instance.LoadScene(destination, failed));
            return true;
        }

        private IEnumerator LoadScene(string destination, Action<string> failed)
        {
            yield return Fade(1);
            yield return null;
            AsyncOperation request = null;
            string error = null;
            try { request = SceneManager.LoadSceneAsync(destination); }
            catch (Exception exception) { error = exception.Message; }
            if (request == null)
            {
                yield return Fade(0);
                // Clear the guard before exposing Retry.
                instance = null;
                failed?.Invoke(error ?? "The scene could not be loaded. Please retry.");
                Destroy(gameObject);
                yield break;
            }
            yield return request;
            yield return null; // Scene Start creates the tutorial UI before reveal.
            yield return Fade(0);
            Destroy(gameObject);
        }

        private IEnumerator Fade(float target)
        {
            float start = cover.alpha;
            for (float elapsed = 0; elapsed < .3f; elapsed += Time.unscaledDeltaTime)
            {
                cover.alpha = Mathf.Lerp(start, target, elapsed / .3f);
                yield return null;
            }
            cover.alpha = target;
        }

        private void OnDestroy() { if (instance == this) instance = null; }

        public static void ShowMenuError(string message, Action retry)
        {
            var view = TutorialView.Create(null);
            view.ShowError(message, () => { Destroy(view.gameObject); retry(); });
            view.SetPauseCaption("Close");
            view.Pause += () => Destroy(view.gameObject);
        }
    }
}
