using System.Collections.Generic;
using UnityEngine;

public class MirrorPlaneRegistrar : MonoBehaviour
{
    public static readonly List<MirrorPlaneRegistrar> AllMirrors = new();

    [HideInInspector]
    public Renderer MirrorRenderer;

    [Header("거리 컬링")]
    [Tooltip("이 거리 이상이면 반사 렌더 스킵 (0 = 무제한)")]
    public float maxReflectionDistance = 30f;

    [Header("페이드 설정")]
    [Tooltip("반사 렌더 거리의 몇 % 지점부터 Fresnel로 페이드 시작 (0.7 = 70%)")]
    [Range(0.3f, 0.9f)]
    public float fadeStartRatio = 0.7f;

    [Header("레이어")]
    [Tooltip("이 거울에 반사될 레이어 (전역 설정 덮어쓰기)")]
    public LayerMask reflectionLayers = -1;

    [Header("반사 화면 후처리")]
    [Tooltip("반사 카메라에 Volume의 후처리 효과를 적용합니다.")]
    public bool reflectionPostProcessing = true;

    [Tooltip("반사 카메라가 사용할 Volume 레이어. Nothing이면 MirrorVolume 레이어를 자동으로 사용합니다. 플레이어 카메라의 Volume Mask에서는 이 레이어를 제외하세요.")]
    public LayerMask reflectionVolumeMask = 0;

    [Header("렌더 주기")]
    [Range(1, 6), Tooltip("몇 프레임마다 반사를 갱신할지 (1 = 매 프레임)")]
    public int renderEveryNFrames = 1;

    [System.NonSerialized]
    public int FrameCounter = 0;

    private void Awake()
    {
        MirrorRenderer = GetComponent<Renderer>();
        if (MirrorRenderer == null)
            MirrorRenderer = GetComponentInChildren<Renderer>();

        if (MirrorRenderer == null)
            EditorLog.LogWarning($"[MirrorPlaneRegistrar] {name} : Renderer를 찾지 못했습니다.");
    }

    private void OnEnable() => AllMirrors.Add(this);

    private void OnDisable() => AllMirrors.Remove(this);
}
// MirrorPlaneRegistrar은 거울 반사 렌더링에 필요한 데이터와 렌더링 수명주기를 관리한다.
// 카메라별 반사 계산과 등록 상태를 분리하여 렌더 패스가 안정적으로 재사용되도록 한다.
