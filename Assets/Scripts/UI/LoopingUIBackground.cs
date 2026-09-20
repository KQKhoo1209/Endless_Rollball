using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public sealed class LoopingUIBackground : MonoBehaviour
{
    [SerializeField] private float scrollSpeed = 0.03f;

    private RawImage background;

    private void Awake()
    {
        background = GetComponent<RawImage>();
    }

    private void Update()
    {
        Rect uv = background.uvRect;

        uv.x = Mathf.Repeat(
            uv.x + scrollSpeed * Time.unscaledDeltaTime,
            1f);

        background.uvRect = uv;
    }
}