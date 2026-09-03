using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Timeline의 EffectControlTrack에서 사용되는 이펙트 클립 에셋입니다.
/// 인스펙터에서 직접 프리팹, 부모 타겟, 위치/회전 오프셋, 스케일을 설정할 수 있습니다.
/// </summary>
[Serializable]
public sealed class EffectControlClip : PlayableAsset, ITimelineClipAsset
{
    [Header("프리팹 설정")]
    [Tooltip("생성할 이펙트 프리팹 에셋 (원점 0,0,0 상태 권장)")]
    [SerializeField]
    private GameObject _prefab;

    [Header("부모 및 좌표 오프셋")]
    [Tooltip("스폰 시 부모로 지정할 씬 트랜스폼 (미지정 시 트랙 바인딩 타겟 사용)")]
    [SerializeField]
    private ExposedReference<Transform> _parentTransform;

    [Tooltip("부모 기준 로컬 위치 오프셋 (X, Y, Z)")]
    [SerializeField]
    private Vector3 _positionOffset = Vector3.zero;

    [Tooltip("부모 기준 로컬 회전 오프셋 (Euler Angle X, Y, Z)")]
    [SerializeField]
    private Vector3 _rotationOffset = Vector3.zero;

    [Tooltip("이펙트 크기 배율")]
    [SerializeField]
    private Vector3 _scaleMultiplier = Vector3.one;

    [Header("고급 설정")]
    [Tooltip("부모를 무시하고 월드 고정 좌표에 스폰할지 여부")]
    [SerializeField]
    private bool _worldSpacePosition = false;

    [Tooltip("타임라인 스크러빙 시 파티클 시뮬레이션을 동기화할지 여부")]
    [SerializeField]
    private bool _simulateParticles = true;

    public ClipCaps clipCaps => ClipCaps.None;

    public GameObject Prefab => _prefab;
    public Vector3 PositionOffset => _positionOffset;
    public Vector3 RotationOffset => _rotationOffset;
    public Vector3 ScaleMultiplier => _scaleMultiplier;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<EffectControlBehaviour>.Create(graph);
        var behaviour = playable.GetBehaviour();

        Transform resolvedParent = _parentTransform.Resolve(graph.GetResolver());

        behaviour.Setup(
            _prefab,
            resolvedParent,
            _positionOffset,
            _rotationOffset,
            _scaleMultiplier,
            _worldSpacePosition,
            _simulateParticles
        );

        return playable;
    }
}
