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
// TooltipIndicator은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
