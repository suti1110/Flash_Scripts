using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class GraphicGroup : Graphic
{
    [Header("Color Tint를 동기화할 대상 그래픽들")]
    [SerializeField]
    private Graphic[] targetGraphics;

    // 버튼의 Target Graphic 역할만 하며 실제 메시는 그리지 않는다.
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
    }

    // 기본 Graphic의 4인자 CrossFadeColor와 CrossFadeAlpha도 이 메서드를 호출한다.
    // 렌더러 tint만 전달하므로 각 대상의 Graphic.color는 그대로 유지된다.
    public override void CrossFadeColor(
        Color targetColor,
        float duration,
        bool ignoreTimeScale,
        bool useAlpha,
        bool useRGB
    )
    {
        base.CrossFadeColor(targetColor, duration, ignoreTimeScale, useAlpha, useRGB);

        if (targetGraphics == null)
            return;

        for (int i = 0; i < targetGraphics.Length; i++)
        {
            Graphic target = targetGraphics[i];
            if (target == null || target == this)
                continue;

            target.CrossFadeColor(targetColor, duration, ignoreTimeScale, useAlpha, useRGB);
        }
    }
}
