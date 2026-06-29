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
