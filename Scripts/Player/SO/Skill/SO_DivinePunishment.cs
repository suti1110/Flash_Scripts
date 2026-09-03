using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DivinePunishment", menuName = "Player/Skill/Divine Punishment")]
public sealed class SO_DivinePunishment : SO_Skill, ISkillNetworkEffect, ISkillCastIndicator
{
    private const int StrikeEffectId = 0;
    private const float EffectPositionTolerance = 7f;
    private const float GroundProbeOffset = 0.5f;
    private const float GroundPointTolerance = 0.15f;

    [Header("천벌")]
    [SerializeField, InspectorName("최대 조준 거리"), Min(0f)]
    private float _maxAimDistance = 100f;

    [SerializeField, InspectorName("지면 레이어")]
    private LayerMask _groundLayers;

    [SerializeField, InspectorName("허용 지면 최대 경사각"), Range(0f, 89f)]
    [Tooltip("위쪽 방향을 기준으로 낙뢰 착탄 지면으로 인정할 최대 경사각입니다.")]
    private float _maxGroundAngle = 60f;

    [SerializeField, InspectorName("피해 대상 레이어")]
    private LayerMask _targetLayers;

    [SerializeField, InspectorName("공격 반경"), Min(0f)]
    private float _strikeRadius = 5f;

    [SerializeField, InspectorName("피해량"), Min(0)]
    private int _damage = 1;

    [SerializeField, InspectorName("넉백 힘"), Min(0f)]
    private float _knockbackForce = 10f;

    [SerializeField, InspectorName("위쪽 넉백 비율"), Min(0f)]
    private float _upwardKnockbackRatio = 0.35f;

    [SerializeField, InspectorName("낙뢰 이펙트 프리팹")]
    private GameObject _strikeEffect;

    [SerializeField, InspectorName("공격 범위 Decal 프리팹")]
    private GameObject _castIndicatorPrefab;

    public GameObject CastIndicatorPrefab => _castIndicatorPrefab;

    public override bool CanExecute(in SkillExecutionContext context, out string failureMessage)
    {
        return TryGetStrikePoint(context, out _, out _, out failureMessage);
    }

    public override void ExecuteSkill(in SkillExecutionContext context)
    {
        Vector3 strikePoint;
        if (context.TryGetTargetPosition(out Vector3 preparedStrikePoint))
        {
            if (!TryValidateStrikePoint(preparedStrikePoint, out strikePoint))
                return;
        }
        else if (!TryGetStrikePoint(context, out strikePoint, out _, out _))
        {
            return;
        }

        context.RequestNetworkEffect(StrikeEffectId, strikePoint, Quaternion.identity);
    }

    public bool TryGetCastIndicator(
        in SkillExecutionContext context,
        out SkillCastIndicatorData indicator
    )
    {
        indicator = default;
        if (!TryGetStrikePoint(context, out Vector3 strikePoint, out Vector3 surfaceNormal, out _))
            return false;

        Vector3 decalUp = Vector3.Cross(surfaceNormal, Vector3.right);
        if (decalUp.sqrMagnitude <= Mathf.Epsilon)
            decalUp = Vector3.Cross(surfaceNormal, Vector3.forward);

        Quaternion rotation = Quaternion.LookRotation(-surfaceNormal, decalUp.normalized);
        indicator = new SkillCastIndicatorData(strikePoint, rotation, _strikeRadius);
        return true;
    }

    public bool IsNetworkEffectRequestValid(
        GameObject caster,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        return effectId == StrikeEffectId
            && _strikeEffect != null
            && caster != null
            && Vector3.Distance(caster.transform.position, position)
                <= _maxAimDistance + EffectPositionTolerance
            && TryValidateStrikePoint(position, out _);
    }

    public void PlayNetworkEffect(
        GameObject caster,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (effectId != StrikeEffectId || _strikeEffect == null)
            return;

        GameObject strikeEffect = Instantiate(_strikeEffect, position, rotation);
        if (strikeEffect.TryGetComponent(out DivinePunishmentStrikeEffect scalableEffect))
            scalableEffect.Initialize(_strikeRadius);

        if (MapPropAuthority.CanSimulate)
            ApplyAreaDamage(caster, position);
    }

    private bool TryGetStrikePoint(
        in SkillExecutionContext context,
        out Vector3 strikePoint,
        out Vector3 surfaceNormal,
        out string failureMessage
    )
    {
        strikePoint = default;
        surfaceNormal = Vector3.up;
        failureMessage = null;

        if (!context.TryGetComponent(out PlayerCamera playerCamera))
        {
            failureMessage = "Unable to aim Divine Punishment.";
            return false;
        }

        Transform aimTransform =
            playerCamera.MainCamera != null
                ? playerCamera.MainCamera.transform
                : playerCamera.CameraPivot;
        if (aimTransform == null)
        {
            failureMessage = "Unable to aim Divine Punishment.";
            return false;
        }

        if (
            !Physics.Raycast(
                aimTransform.position,
                aimTransform.forward,
                out RaycastHit hit,
                _maxAimDistance,
                _groundLayers,
                QueryTriggerInteraction.Ignore
            )
        )
        {
            failureMessage = "Aim at valid ground to use Divine Punishment.";
            return false;
        }

        if (!IsValidGroundSurface(hit.normal))
        {
            failureMessage = "Divine Punishment cannot be used on surfaces this steep.";
            return false;
        }

        strikePoint = hit.point;
        surfaceNormal = hit.normal;
        return true;
    }

    private bool TryValidateStrikePoint(Vector3 requestedPoint, out Vector3 strikePoint)
    {
        strikePoint = default;
        Vector3 probeOrigin = requestedPoint + Vector3.up * GroundProbeOffset;

        if (
            !Physics.Raycast(
                probeOrigin,
                Vector3.down,
                out RaycastHit hit,
                GroundProbeOffset * 2f,
                _groundLayers,
                QueryTriggerInteraction.Ignore
            )
            || Vector3.Distance(hit.point, requestedPoint) > GroundPointTolerance
            || !IsValidGroundSurface(hit.normal)
        )
        {
            return false;
        }

        strikePoint = hit.point;
        return true;
    }

    private bool IsValidGroundSurface(Vector3 surfaceNormal)
    {
        return Vector3.Angle(surfaceNormal, Vector3.up) <= _maxGroundAngle;
    }

    private void ApplyAreaDamage(GameObject caster, Vector3 strikePoint)
    {
        // 서버에서는 호스트가 Player, 게스트가 OtherPlayer 레이어이므로
        // 시전자 관점과 관계없이 두 플레이어 레이어를 모두 검색한다.
        int targetLayers = _targetLayers | LayerMask.GetMask("Player", "OtherPlayer");
        Collider[] targets = Physics.OverlapSphere(
            strikePoint,
            _strikeRadius,
            targetLayers,
            QueryTriggerInteraction.Collide
        );
        HashSet<Rigidbody> damagedBodies = new();

        foreach (Collider target in targets)
        {
            Rigidbody targetBody = target.attachedRigidbody;
            if (targetBody == null || !damagedBodies.Add(targetBody))
                continue;
            if (caster != null && targetBody.transform == caster.transform)
                continue;
            if (!targetBody.TryGetComponent(out PlayerDamage damageable))
                continue;

            Vector3 direction = targetBody.worldCenterOfMass - strikePoint;
            direction.y = Mathf.Max(direction.y, _strikeRadius * _upwardKnockbackRatio);
            if (direction.sqrMagnitude <= Mathf.Epsilon)
                direction = Vector3.up;

            damageable.TakeDamage(_damage, direction.normalized * _knockbackForce);
        }
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        if (_groundLayers == 0)
            EditorLog.LogError("천벌의 지면 레이어가 비어 있습니다.", this);
        if (_targetLayers == 0)
            EditorLog.LogError("천벌의 피해 대상 레이어가 비어 있습니다.", this);
        if (_strikeEffect == null)
            EditorLog.LogError("천벌의 낙뢰 이펙트 프리팹이 할당되지 않았습니다.", this);
        if (_castIndicatorPrefab == null)
            EditorLog.LogError("천벌의 공격 범위 Decal 프리팹이 할당되지 않았습니다.", this);
    }
}
// SO_DivinePunishment은 인스펙터에서 조정하는 플레이어 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
