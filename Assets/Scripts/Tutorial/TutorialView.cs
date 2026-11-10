using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EndlessRollball.Tutorial
{
    // Built once per scene; only the small gesture illustration animates each frame.
    public sealed class TutorialView : MonoBehaviour
    {
        private TMP_Text heading, instructions, progress, actionLabel;
        private Button action, pause;
        private GameObject pausePanel;
        private RectTransform hand;
        private RectTransform gestureRoot;
        private TutorialControlDemo demonstration;
        private TMP_Text gestureCaption;
        private GameObject gameplayPauseUI;
        private Action primaryAction;
        public event Action Primary, Pause, Resume, Exit;

        public static TutorialView Create(Transform parent)
        {
            var root = new GameObject("Tutorial Guidance", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(Image), typeof(TutorialView));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = Color.clear;
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            root.GetComponent<Canvas>().sortingOrder = 50;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
            if (EventSystem.current == null)
                new GameObject("Tutorial Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var view = root.GetComponent<TutorialView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            Color ink = new Color(.035f, .065f, .11f, .96f);
            RectTransform top = Panel(transform, "Progress", new Vector2(0, .9f), Vector2.one, ink);
            progress = Label(top, "Progress text", "", 25, new Vector2(.03f, .1f), new Vector2(.75f, .9f));
            pause = MakeButton(top, "Pause", new Vector2(.81f, .15f), new Vector2(.97f, .85f), () => Pause?.Invoke());
            RectTransform bottom = Panel(transform, "Instructions", Vector2.zero, new Vector2(1, .27f), ink);
            heading = Label(bottom, "Heading", "", 31, new Vector2(.03f, .73f), new Vector2(.76f, .97f));
            instructions = Label(bottom, "Instruction text", "", 24, new Vector2(.03f, .08f), new Vector2(.76f, .72f));
            action = MakeButton(bottom, "Begin", new Vector2(.79f, .26f), new Vector2(.97f, .66f),
                () => { if (primaryAction != null) primaryAction(); else Primary?.Invoke(); });
            actionLabel = action.GetComponentInChildren<TMP_Text>();

            gestureRoot = Panel(transform, "Control guidance", new Vector2(.25f, .49f), new Vector2(.75f, .88f), Color.clear);
            gestureRoot.GetComponent<Image>().raycastTarget = false;
            hand = Panel(gestureRoot, "Whole hand", new Vector2(.5f, .67f), new Vector2(.5f, .67f), Color.white);
            hand.sizeDelta = new Vector2(150, 150);
            hand.GetComponent<Image>().preserveAspect = true;
            hand.GetComponent<Image>().raycastTarget = false;
            RectTransform captionPanel = Panel(gestureRoot, "Guidance text panel", new Vector2(.04f, .06f), new Vector2(.96f, .35f),
                new Color(.025f, .045f, .075f, .92f));
            gestureCaption = Label(captionPanel, "Guidance text", "", 25, new Vector2(.04f, .08f), new Vector2(.96f, .92f));
            gestureCaption.alignment = TextAlignmentOptions.Center;
            gestureCaption.color = Color.white;
            gestureCaption.fontStyle = FontStyles.Bold;
            gestureCaption.outlineWidth = .18f;
            gestureCaption.outlineColor = new Color32(15, 22, 28, 255);
            demonstration = gestureRoot.gameObject.AddComponent<TutorialControlDemo>();
            gestureRoot.gameObject.SetActive(false);

            pausePanel = Panel(transform, "Pause overlay", Vector2.zero, Vector2.one, new Color(0, .02f, .05f, .97f)).gameObject;
            Label(pausePanel.transform, "Paused", "Paused\nTake your time. Your current exercise is kept.", 32,
                new Vector2(.15f, .58f), new Vector2(.85f, .8f));
            MakeButton(pausePanel.transform, "Resume", new Vector2(.35f, .4f), new Vector2(.65f, .51f), () => Resume?.Invoke());
            MakeButton(pausePanel.transform, "Exit to Menu", new Vector2(.35f, .24f), new Vector2(.65f, .35f), () => Exit?.Invoke());
            pausePanel.SetActive(false);
        }

        public void Show(string title, string body, string step, string button = null)
        {
            SetDemonstration(TutorialDemonstration.None);
            primaryAction = null;
            heading.text = title;
            instructions.text = body;
            progress.text = step;
            action.gameObject.SetActive(button != null);
            if (button != null) actionLabel.text = button;
        }

        public void ShowError(string message, Action retry)
        {
            SetPaused(false);
            Show("Unable to continue", message, "Your progress is kept", "Retry");
            primaryAction = retry;
        }

        public void SetPaused(bool value)
        {
            pausePanel.SetActive(value);
            if (gameplayPauseUI != null) pause.gameObject.SetActive(!value);
            demonstration.SetPaused(value);
        }
        public void ConfigurePresentation(Sprite handSprite, GameObject pausePrefab)
        {
            hand.GetComponent<Image>().sprite = handSprite;
            if (pausePrefab == null) return;
            Destroy(pause.gameObject);
            Destroy(pausePanel);
            gameplayPauseUI = Instantiate(pausePrefab);
            gameplayPauseUI.name = "Tutorial Gameplay Pause UI";
            pause = gameplayPauseUI.transform.Find("PauseButton").GetComponent<Button>();
            pausePanel = gameplayPauseUI.transform.Find("PauseMenu").gameObject;
            Transform panel = pausePanel.transform.Find("PausePanel");
            pause.onClick.AddListener(() => Pause?.Invoke());
            panel.Find("ResumeButton").GetComponent<Button>().onClick.AddListener(() => Resume?.Invoke());
            Button exit = panel.Find("EndSessionButton").GetComponent<Button>();
            exit.onClick.AddListener(() => Exit?.Invoke());
            exit.GetComponentInChildren<TMP_Text>(true).text = "Exit to Menu";
            // The source gameplay Settings button is currently unwired.
            panel.Find("SettingsButton").gameObject.SetActive(false);
            pausePanel.SetActive(false);
            pause.gameObject.SetActive(true);
            progress.rectTransform.anchorMin = new Vector2(.1f, .1f);
            progress.rectTransform.anchorMax = new Vector2(.98f, .9f);
        }

        private void OnDestroy()
        {
            if (gameplayPauseUI != null) Destroy(gameplayPauseUI);
        }

        public void ConfigureDemonstrations(RuntimeAnimatorController controller, BallMovement ball, CameraFollow camera, Transform root)
            => demonstration.Configure(controller, ball, camera, root);
        public void SetPauseCaption(string caption) => pause.GetComponentInChildren<TMP_Text>().text = caption;
        public void SetBusy(bool value) { action.interactable = !value; pause.interactable = !value; }
        public void SetSteering(float steering)
        {
            if (demonstration.IsPlaying) return;
            hand.anchoredPosition = Vector2.Lerp(hand.anchoredPosition, new Vector2(steering * 65f, 0),
                1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        }

        public void SetDemonstration(TutorialDemonstration value)
        {
            bool showing = value != TutorialDemonstration.None;
            gestureRoot.gameObject.SetActive(showing);
            demonstration.Play(value);
            hand.anchoredPosition = Vector2.zero;
            hand.localRotation = Quaternion.identity;
            gestureCaption.text = value == TutorialDemonstration.Left ? "MOVE LEFT  ·  A / ←\nWatch the hand and the ball move left" :
                value == TutorialDemonstration.Right ? "MOVE RIGHT  ·  D / →\nWatch the hand and the ball move right" :
                value == TutorialDemonstration.Centre ? "RETURN TO CENTRE\nSteer back, then release" :
                value == TutorialDemonstration.Combined ? "LEFT → RIGHT → JUMP\nWatch the ball on the road" :
                "JUMP  ·  ↑\nKeyboard demonstration: watch the ball jump";
        }

        internal static RectTransform Panel(Transform parent, string name, Vector2 min, Vector2 max, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rect;
        }

        internal static TMP_Text Label(Transform parent, string name, string text, int size, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text; label.fontSize = size; label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            return label;
        }

        internal static Button MakeButton(Transform parent, string text, Vector2 min, Vector2 max, Action clicked)
        {
            var rect = Panel(parent, text, min, max, new Color(.08f, .38f, .52f));
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(EndlessRollball.UI.UIButtonClickAudio.PlayDefault);
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(() => clicked());
            var label = Label(rect, "Label", text, 25, new Vector2(.04f, .04f), new Vector2(.96f, .96f));
            label.alignment = TextAlignmentOptions.Center;
            EndlessRollball.UI.UIButtonStyle.ApplyShared(button);
            return button;
        }
    }
}
