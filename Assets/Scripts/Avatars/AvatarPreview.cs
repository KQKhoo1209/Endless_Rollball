using UnityEngine;
using UnityEngine.UI;

namespace EndlessRollball.Avatars
{
    // Owns one isolated stage only while this UI is visible. Shared gameplay prefabs remain untouched.
    public sealed class AvatarPreview : MonoBehaviour
    {
        [SerializeField] private RawImage image;
        [SerializeField] private GameObject cow, whale, phoenix;
        [SerializeField] private Color previewBackground = new Color(.035f, .095f, .15f, 1f);
        private GameObject stage, model;
        private RenderTexture texture;
        private AvatarAppearance selection;
        private string displayedAvatar;
        private AvatarPresentation presentation;
        private static int nextStage;
        public void Show(AvatarAppearance appearance)
        {
            selection = appearance.Copy();
            if (!isActiveAndEnabled) return;
            EnsureStage();
            if (model != null && displayedAvatar == selection.avatarId)
            {
                if (presentation != null) presentation.Apply(selection);
                return;
            }
            if (model != null) Destroy(model);
            var prefab = selection.avatarId == AvatarCatalog.Whale ? whale : selection.avatarId == AvatarCatalog.Phoenix ? phoenix : cow;
            if (prefab == null) return;
            model = Instantiate(prefab, stage.transform, false);
            displayedAvatar = selection.avatarId;
            SetLayer(model.transform);
            presentation = model.GetComponent<AvatarPresentation>();
            if (presentation != null) presentation.Apply(selection);
        }
        public void React() { if (presentation != null) presentation.React(); }
        private void OnEnable() { if (selection != null) Show(selection); }
        private void EnsureStage()
        {
            if (stage != null) return;
            stage = new GameObject("Avatar Preview Stage");
            stage.transform.position = new Vector3(10000f + 12f * (nextStage++ % 100), 10000f, 10000f);
            texture = new RenderTexture(640, 640, 24) { name = "Avatar Preview", antiAliasing = 2 };
            texture.Create(); image.texture = texture;
            var camera = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.transform.localPosition = new Vector3(0f, .3f, -4f);
            camera.transform.LookAt(stage.transform.position);
            camera.orthographic = true; camera.orthographicSize = 1.35f;
            camera.nearClipPlane = .1f; camera.farClipPlane = 10f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = previewBackground;
            camera.cullingMask = 1 << 30; camera.targetTexture = texture;
            var light = new GameObject("Preview Light", typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(stage.transform, false); light.transform.localRotation = Quaternion.Euler(35, -35, 0);
            light.type = LightType.Directional; light.intensity = 1.8f; light.cullingMask = 1 << 30;
        }
        private static void SetLayer(Transform target)
        { target.gameObject.layer = 30; foreach (Transform child in target) SetLayer(child); }
        private void OnDisable()
        {
            if (image != null) image.texture = null;
            if (stage != null) { stage.SetActive(false); Destroy(stage); }
            if (texture != null) { texture.Release(); Destroy(texture); }
            stage = model = null; texture = null; presentation = null; displayedAvatar = null;
        }
    }
}
