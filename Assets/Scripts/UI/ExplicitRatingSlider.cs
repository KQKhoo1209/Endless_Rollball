using UnityEngine.EventSystems;

/// <summary>Confirms an intentional zero as well as a changed slider value.</summary>
public sealed class ExplicitRatingSlider : UnityEngine.UI.Slider, ISubmitHandler
{
    public override void OnPointerDown(PointerEventData eventData)
    {
        if (!IsActive() || !IsInteractable() || eventData.button != PointerEventData.InputButton.Left) return;
        base.OnPointerDown(eventData);
        onValueChanged.Invoke(value);
    }

    public override void OnMove(AxisEventData eventData)
    {
        base.OnMove(eventData);
        if (IsActive() && IsInteractable() &&
            (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right))
            onValueChanged.Invoke(value);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (IsActive() && IsInteractable()) onValueChanged.Invoke(value);
    }
}
