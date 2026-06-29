using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class GraphicGroup : Graphic
{
    [Header("색상을 동기화할 대상 그래픽들")]
    [SerializeField]
    private Graphic[] targetGraphics;

    // 각 대상 Graphic이 원래 가지고 있던 색상.
    // GraphicGroup.color는 이 색상에 곱해지는 tint로 사용된다.
    [SerializeField, HideInInspector]
    private Color[] _targetBaseColors;

    // targetGraphics 배열 구성이 바뀌었는지 감지하기 위한 이전 참조 목록.
    [SerializeField, HideInInspector]
    private Graphic[] _cachedTargetGraphics;

    protected override void Awake()
    {
        base.Awake();
        CacheTargetBaseColors(false);
        SyncColorToTargets(base.color);
    }

    // 이 Graphic 자체는 버튼의 Target Graphic 역할만 하고 실제 메시는 그리지 않는다.
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
    }

    public override Color color
    {
        get => base.color;
        set
        {
            if (base.color == value)
                return;

            base.color = value;
            SyncColorToTargets(value);
        }
    }

    // Button의 Color Tint나 코드 기반 색상 페이드가 들어올 때도
    // 각 대상의 원본 색상에 targetColor를 곱해서 전달한다.
    public override void CrossFadeColor(
        Color targetColor,
        float duration,
        bool ignoreTimeScale,
        bool useAlpha
    )
    {
        base.CrossFadeColor(targetColor, duration, ignoreTimeScale, useAlpha);

        if (targetGraphics == null)
            return;

        for (int i = 0; i < targetGraphics.Length; i++)
        {
            if (targetGraphics[i] != null)
            {
                targetGraphics[i]
                    .CrossFadeColor(
                        GetTintedColor(i, targetColor),
                        duration,
                        ignoreTimeScale,
                        useAlpha
                    );
            }
        }
    }

    // 알파만 페이드할 때도 대상 Graphic의 원본 알파를 기준으로 곱해준다.
    public override void CrossFadeAlpha(float alpha, float duration, bool ignoreTimeScale)
    {
        base.CrossFadeAlpha(alpha, duration, ignoreTimeScale);

        if (targetGraphics == null)
            return;

        for (int i = 0; i < targetGraphics.Length; i++)
        {
            if (targetGraphics[i] == null)
                continue;

            Color targetColor = GetTintedColor(i, base.color);
            targetColor.a = _targetBaseColors[i].a * alpha;
            targetGraphics[i].CrossFadeColor(targetColor, duration, ignoreTimeScale, true);
        }
    }

    // Animator가 GraphicGroup.color를 조정한 경우 대상 Graphic에도 tint 결과를 반영한다.
    protected override void OnDidApplyAnimationProperties()
    {
        base.OnDidApplyAnimationProperties();
        SyncColorToTargets(base.color);
    }

#if UNITY_EDITOR
    // Inspector에서 targetGraphics나 GraphicGroup.color를 수정했을 때 에디터에서도 즉시 반영한다.
    protected override void OnValidate()
    {
        base.OnValidate();
        CacheTargetBaseColors(false);
        SyncColorToTargets(base.color);
    }
#endif

    // 대상 Graphic의 현재 색상을 새로운 기준 색상으로 다시 저장한다.
    // 대상의 원래 색을 바꾼 뒤 Inspector 우클릭 메뉴로 실행하면 된다.
    [ContextMenu("Refresh Target Base Colors")]
    private void RefreshTargetBaseColors()
    {
        CacheTargetBaseColors(true);
        SyncColorToTargets(base.color);
    }

    // targetGraphics 구성이 바뀌었거나 강제 갱신할 때 대상들의 기준 색상을 저장한다.
    private void CacheTargetBaseColors(bool force)
    {
        if (targetGraphics == null)
        {
            _targetBaseColors = null;
            _cachedTargetGraphics = null;
            return;
        }

        if (!force && !HasTargetListChanged())
            return;

        if (_targetBaseColors == null || _targetBaseColors.Length != targetGraphics.Length)
            _targetBaseColors = new Color[targetGraphics.Length];

        if (_cachedTargetGraphics == null || _cachedTargetGraphics.Length != targetGraphics.Length)
            _cachedTargetGraphics = new Graphic[targetGraphics.Length];

        for (int i = 0; i < targetGraphics.Length; i++)
        {
            _cachedTargetGraphics[i] = targetGraphics[i];

            if (targetGraphics[i] != null)
                _targetBaseColors[i] = targetGraphics[i].color;
            else
                _targetBaseColors[i] = Color.white;
        }
    }

    // 모든 대상 Graphic에 "대상 원본 색상 * 그룹 tint 색상" 결과를 적용한다.
    private void SyncColorToTargets(Color tintColor)
    {
        if (targetGraphics == null)
            return;

        EnsureBaseColors();

        for (int i = 0; i < targetGraphics.Length; i++)
        {
            if (targetGraphics[i] != null)
                targetGraphics[i].color = GetTintedColor(i, tintColor);
        }
    }

    // 런타임에서 아직 기준 색상을 저장하지 못했거나 배열 길이가 바뀐 경우 보정한다.
    private void EnsureBaseColors()
    {
        if (_targetBaseColors == null || _targetBaseColors.Length != targetGraphics.Length)
            CacheTargetBaseColors(true);
    }

    // targetGraphics 배열 자체가 바뀌었는지 확인한다.
    private bool HasTargetListChanged()
    {
        if (_targetBaseColors == null || _targetBaseColors.Length != targetGraphics.Length)
            return true;

        if (_cachedTargetGraphics == null || _cachedTargetGraphics.Length != targetGraphics.Length)
            return true;

        for (int i = 0; i < targetGraphics.Length; i++)
        {
            if (_cachedTargetGraphics[i] != targetGraphics[i])
                return true;
        }

        return false;
    }

    // index번째 대상의 기준 색상에 tintColor를 곱한 최종 출력 색상을 계산한다.
    private Color GetTintedColor(int index, Color tintColor)
    {
        EnsureBaseColors();

        Color baseColor =
            index >= 0 && index < _targetBaseColors.Length ? _targetBaseColors[index] : Color.white;

        return new Color(
            baseColor.r * tintColor.r,
            baseColor.g * tintColor.g,
            baseColor.b * tintColor.b,
            baseColor.a * tintColor.a
        );
    }
}
