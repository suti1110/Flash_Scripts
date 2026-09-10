using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(fileName = "HomingBeam", menuName = "Player/Skill/Homing Beam")]
public sealed class SO_HomingBeam : SO_Skill, ISkillNetworkEffect, ISkillNetworkTargetEffect
{
    private const int StraightEffectId = 0;
    private const int HomingEffectId = 1;
    private const int StraightContinuationEffectId = 2;
    private const float EffectPositionTolerance = 3f;
    private const float TargetDistanceTolerance = 5f;

    [Header("광선 날리기")]
    [SerializeField, InspectorName("탐색 대상 레이어")]
    private LayerMask _targetLayers;

    [SerializeField, InspectorName("대상 탐색 거리"), Min(0f)]
    private float _acquisitionRange = 100f;

    [SerializeField, InspectorName("발사 위치 오프셋")]
    private Vector3 _spawnOffset = new(0f, 1f, 1.25f);

    [SerializeField, InspectorName("직선 비행 시간"), Min(0.01f)]
    private float _straightDuration = 1f;

    [SerializeField, InspectorName("타깃 미발견 시 총 비행 시간"), Min(0.01f)]
    private float _noTargetLifetime = 10f;

    [SerializeField, InspectorName("유도 지속 시간"), Min(0.01f)]
    private float _homingDuration = 2f;

    [SerializeField, InspectorName("광선 이동 속도"), Min(0.01f)]
    private float _speed = 45f;

    [SerializeField, InspectorName("초당 회전 각도"), Min(0f)]
    private float _turnRate = 360f;

    [SerializeField, InspectorName("유도 가속도"), Min(0.01f)]
    [Tooltip("현재 속도를 목표 방향의 속도로 변화시키는 최대 가속도입니다. 낮을수록 관성이 강합니다.")]
    private float _homingAcceleration = 55f;

    [SerializeField, InspectorName("명중 거리"), Min(0.01f)]
    private float _hitDistance = 1.25f;

    [SerializeField, InspectorName("피해량"), Min(0)]
    private int _damage = 1;

    [SerializeField, InspectorName("넉백 힘"), Min(0f)]
    private float _knockbackForce = 8f;

    [SerializeField, InspectorName("유도 광선 이펙트 프리팹")]
    private GameObject _beamEffectPrefab;

    [Header("Sound")]
    [SerializeField, InspectorName("발사 효과음")]
    private AudioClip _launchAudio;

    [SerializeField, InspectorName("유도 전환 효과음")]
    private AudioClip _homingAudio;

    [Header("카메라 연출")]
    [SerializeField, InspectorName("시야각 축소량"), Min(0f)]
    private float _cameraFieldOfViewDecrease = 10f;

    [SerializeField, InspectorName("화면 일렁임 강도"), Min(0f)]
    private float _cameraRippleStrength = 1f;

    [SerializeField, InspectorName("원래 시점 복귀 시간"), Min(0.01f)]
    private float _cameraReturnDuration = 0.6f;

    public override void ExecuteSkill(in SkillExecutionContext context)
    {
        Transform caster = context.Transform;
        if (caster == null)
            return;

        if (context.TryGetComponent(out PlayerCamera playerCamera))
        {
            playerCamera.PlayHomingBeamFeedback(
                _cameraFieldOfViewDecrease,
                _cameraRippleStrength,
                _cameraReturnDuration
            );
        }

        Vector3 spawnPosition = caster.TransformPoint(_spawnOffset);
        context.RequestNetworkEffect(StraightEffectId, spawnPosition, caster.rotation);
    }

    public bool IsNetworkEffectRequestValid(
        GameObject caster,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (
            (effectId != StraightEffectId && effectId != StraightContinuationEffectId)
            || _beamEffectPrefab == null
            || caster == null
        )
            return false;

        if (effectId == StraightEffectId)
        {
            Vector3 expectedSpawnPosition = caster.transform.TransformPoint(_spawnOffset);
            return Vector3.Distance(position, expectedSpawnPosition) <= EffectPositionTolerance;
        }

        float maximumStraightDistance =
            _spawnOffset.magnitude + _speed * _straightDuration + EffectPositionTolerance;
        return Vector3.Distance(position, caster.transform.position) <= maximumStraightDistance;
    }

    public void PlayNetworkEffect(
        GameObject caster,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (
            (effectId != StraightEffectId && effectId != StraightContinuationEffectId)
            || _beamEffectPrefab == null
            || caster == null
        )
            return;

        if (effectId == StraightEffectId)
            AudioManager.SfxPlayAtPoint(_launchAudio, position);

        GameObject effectObject = Instantiate(_beamEffectPrefab, position, rotation);
        if (!effectObject.TryGetComponent(out HomingBeamEffect effect))
        {
            Destroy(effectObject);
            return;
        }

        PlayerSkill playerSkill = null;
        bool canSelectTarget = true;
        if (caster.TryGetComponent(out NetworkObject casterNetworkObject))
            canSelectTarget = !casterNetworkObject.IsSpawned || casterNetworkObject.IsOwner;

        if (canSelectTarget && effectId == StraightEffectId)
            caster.TryGetComponent(out playerSkill);

        float straightLifetime =
            effectId == StraightEffectId
                ? _straightDuration
                : Mathf.Max(0.01f, _noTargetLifetime - _straightDuration);

        effect.InitializeStraight(
            caster,
            straightLifetime,
            _speed,
            playerSkill == null
                ? null
                : (beamPosition, beamRotation) =>
                {
                    if (caster == null || playerSkill == null)
                        return;

                    if (!TryFindTarget(beamPosition, caster.transform, out GameObject target))
                    {
                        playerSkill.RequestSpawnEffect(
                            this,
                            StraightContinuationEffectId,
                            beamPosition,
                            beamRotation
                        );
                        return;
                    }

                    Vector3 direction = GetTargetPosition(target) - beamPosition;
                    Quaternion homingRotation =
                        direction.sqrMagnitude > Mathf.Epsilon
                            ? Quaternion.LookRotation(direction.normalized, caster.transform.up)
                            : beamRotation;

                    playerSkill.RequestSpawnTargetEffect(
                        this,
                        HomingEffectId,
                        target,
                        beamPosition,
                        homingRotation
                    );
                }
        );
    }

    public bool IsNetworkTargetEffectRequestValid(
        GameObject caster,
        GameObject target,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (
            effectId != HomingEffectId
            || _beamEffectPrefab == null
            || caster == null
            || target == null
            || target == caster
            || !target.TryGetComponent(out IDamageable _)
        )
        {
            return false;
        }

        float maximumStraightDistance =
            _spawnOffset.magnitude + _speed * _straightDuration + EffectPositionTolerance;
        return Vector3.Distance(position, caster.transform.position) <= maximumStraightDistance
            && Vector3.Distance(position, GetTargetPosition(target))
                <= _acquisitionRange + TargetDistanceTolerance;
    }

    public void PlayNetworkTargetEffect(
        GameObject caster,
        GameObject target,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (effectId != HomingEffectId || _beamEffectPrefab == null)
            return;

        AudioManager.SfxPlayAtPoint(_homingAudio, position);

        GameObject effectObject = Instantiate(_beamEffectPrefab, position, rotation);
        if (!effectObject.TryGetComponent(out HomingBeamEffect effect))
        {
            Destroy(effectObject);
            return;
        }

        // 모든 피어는 같은 이펙트를 표시하지만 실제 피해는 서버 한 곳에서만 적용한다.
        bool canApplyDamage = MapPropAuthority.CanSimulate;

        effect.Initialize(
            caster,
            target,
            _homingDuration,
            _speed,
            _turnRate,
            _homingAcceleration,
            _hitDistance,
            _damage,
            _knockbackForce,
            canApplyDamage
        );
    }

    private bool TryFindTarget(Vector3 origin, Transform caster, out GameObject target)
    {
        target = null;
        if (caster == null)
            return false;

        // 현재 소유자 화면뿐 아니라 서버 또는 다른 권한 구성에서도 같은 대상을 찾도록
        // 로컬 Player와 원격 OtherPlayer 레이어를 모두 포함하고 아래에서 시전자 자신을 제외한다.
        int targetLayers = _targetLayers | LayerMask.GetMask("Player", "OtherPlayer");
        Collider[] candidates = Physics.OverlapSphere(
            origin,
            _acquisitionRange,
            targetLayers,
            QueryTriggerInteraction.Collide
        );
        HashSet<Rigidbody> visitedBodies = new();
        float nearestSqrDistance = float.PositiveInfinity;

        foreach (Collider candidate in candidates)
        {
            Rigidbody body = candidate.attachedRigidbody;
            if (body == null || !visitedBodies.Add(body) || body.transform == caster)
                continue;
            if (!body.TryGetComponent(out IDamageable _))
                continue;
            if (
                body.TryGetComponent(out PlayerDamage playerDamage)
                && playerDamage.IsSpawned
                && !playerDamage.IsAlive
            )
            {
                continue;
            }

            float sqrDistance = (body.worldCenterOfMass - origin).sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance)
                continue;

            nearestSqrDistance = sqrDistance;
            target = body.gameObject;
        }

        return target != null;
    }

    private static Vector3 GetTargetPosition(GameObject target)
    {
        return target.TryGetComponent(out Rigidbody body)
            ? body.worldCenterOfMass
            : target.transform.position;
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        if (_targetLayers == 0)
            EditorLog.LogError("광선 날리기의 탐색 대상 레이어가 비어 있습니다.", this);
        if (_beamEffectPrefab == null)
            EditorLog.LogError("광선 날리기의 이펙트 프리팹이 할당되지 않았습니다.", this);
        else if (!_beamEffectPrefab.TryGetComponent<HomingBeamEffect>(out _))
            EditorLog.LogError("광선 이펙트 프리팹에 HomingBeamEffect가 필요합니다.", this);
    }
}
// SO_HomingBeam은 인스펙터에서 조정하는 플레이어 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
