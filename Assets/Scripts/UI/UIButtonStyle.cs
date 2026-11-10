using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EndlessRollball.UI
{
    /// <summary>The Main Menu artwork and contrast settings shared by authored and runtime buttons.</summary>
    public sealed class UIButtonStyle : ScriptableObject
    {
        public const string ResourcePath = "UI/MainMenuButtonStyle";
        [SerializeField] private Sprite background;
        [SerializeField] private Image.Type imageType = Image.Type.Sliced;
        [SerializeField] private Color backgroundColour = Color.white;
        [SerializeField] private Color labelColour = Color.black;
        [SerializeField] private ColorBlock colours = ColorBlock.defaultColorBlock;
        [SerializeField] private Material material;
        [SerializeField] private float pixelsPerUnitMultiplier = .5f;
        [SerializeField] private bool fillCenter = true;
        private static UIButtonStyle shared;

        public static bool ApplyShared(Button button)
        {
            if (shared == null) shared = Resources.Load<UIButtonStyle>(ResourcePath);
            if (shared == null || button == null) return false;
            shared.Apply(button);
            return true;
        }

        public void Apply(Button button)
        {
            var image = button.GetComponent<Image>();
            if (image == null) image = button.gameObject.AddComponent<Image>();
            // Icon-only controls retain their meaning inside the common button background.
            if (image.sprite != null && image.sprite != background &&
                button.GetComponentsInChildren<TMP_Text>(true).Length == 0 && button.transform.Find("ButtonIcon") == null)
            {
                var icon = new GameObject("ButtonIcon", typeof(RectTransform), typeof(Image));
                icon.layer = button.gameObject.layer;
                var rect = (RectTransform)icon.transform;
                rect.SetParent(button.transform, false);
                rect.anchorMin = new Vector2(.23f, .23f); rect.anchorMax = new Vector2(.77f, .77f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var graphic = icon.GetComponent<Image>();
                graphic.sprite = image.sprite; graphic.preserveAspect = true;
                graphic.color = labelColour; graphic.raycastTarget = false;
            }
            image.sprite = background; image.overrideSprite = null;
            image.type = imageType; image.color = backgroundColour; image.material = material;
            image.preserveAspect = false; image.fillCenter = fillCenter;
            image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = colours;
            button.spriteState = default;
            foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.GetComponentInParent<Button>(true) != button) continue;
                label.color = labelColour;
                label.enableVertexGradient = false;
            }
            foreach (var graphic in button.GetComponentsInChildren<Image>(true))
            {
                if (graphic == image || graphic.GetComponentInParent<Button>(true) != button) continue;
                for (var t = graphic.transform; t != button.transform; t = t.parent)
                {
                    if (t.name == "ButtonIcon" || t.name == "SelectedIndicator" || t.name == "LockedIndicator" ||
                        t.name == "PreviewIndicator" || t.name == "SelectionUnderline" || t.name == "SelectedUnderline")
                    { graphic.color = labelColour; break; }
                }
            }
        }
    }
}
