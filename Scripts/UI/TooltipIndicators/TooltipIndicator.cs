using UnityEngine;
using UnityEngine.EventSystems;

public abstract class TooltipIndicator
    : MonoBehaviour,
        IPointerEnterHandler,
        IPointerMoveHandler,
        IPointerExitHandler
{
    protected abstract string Title { get; }

    protected abstract string Content { get; }

    public void OnPointerEnter(PointerEventData eventData)
    {
        TooltipOnUI.OnTooltip(Title, Content);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        TooltipOnUI.TooltipTracking(eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipOnUI.OffTooltip();
    }
}
