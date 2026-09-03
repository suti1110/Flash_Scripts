using UnityEngine;

[CreateAssetMenu(fileName = "Blinking", menuName = "Player/Skill/Blinking")]
public class SO_Blinking : SO_Skill, ISkillNetworkEffect
{
    private const int BlinkEffectId = 0;
    private const float EffectPositionTolerance = 5f;

    [Header("Blinking")]
    [SerializeField]
    private Vector3 _playerBodyOffset = new(0, 1f);

    [SerializeField]
    private float _maxBlinkDistance = 20f;

    [SerializeField]
    private LayerMask _wall;

    [SerializeField]
    private float _sphereCastRadius = 0.5f;

    [SerializeField]
    private GameObject _blickEffect;

    public override void ExecuteSkill(in SkillExecutionContext context)
    {
        IEnergyTracker energyTracker = context.EnergyTracker;
        energyTracker.SetTrackingPause(true); // 순간이동 중에는 에너지 회복이 일어나지 않도록 함

        Transform transform = context.Transform;
        if (transform == null)
        {
            energyTracker.SetTrackingPause(false);
            return;
        }

        if (!context.TryGetComponent(out PlayerCamera playerCamera))
        {
            energyTracker.SetTrackingPause(false);
            EditorLog.LogError("PlayerCamera is required to execute Blinking.", transform);
            return;
        }

        Vector3 blinkDirection = playerCamera.CameraPivot.forward;

        context.RequestNetworkEffect(
            BlinkEffectId,
            transform.position + _playerBodyOffset,
            Quaternion.identity
        ); // effectId: 0 (블링크 이펙트)

        Vector3 newPosition = transform.position;

        if (
            Physics.SphereCast(
                transform.position + _playerBodyOffset,
                _sphereCastRadius,
                blinkDirection,
                out RaycastHit hit,
                _maxBlinkDistance,
                _wall
            )
        )
        {
            newPosition += blinkDirection * hit.distance;
        }
        else
        {
            newPosition += blinkDirection * _maxBlinkDistance;
        }

        bool isTeleported = false;

        if (transform.TryGetComponent(out Unity.Netcode.Components.NetworkTransform netTransform))
        {
            netTransform.Teleport(newPosition, transform.rotation, transform.localScale);
            isTeleported = true;
        }

        if (transform.TryGetComponent(out Unity.Netcode.Components.NetworkRigidbody netRigidbody))
        {
            netRigidbody.SetPosition(newPosition);
            isTeleported = true;
        }

        if (!isTeleported)
        {
            transform.position = newPosition;
        }

        //if (transform.TryGetComponent(out Rigidbody rb))
        //{
        //    rb.linearVelocity = Vector3.zero; // 물리 속도 초기화 (튕겨나감 방지)
        //}

        energyTracker.SetTrackingPause(false); // 순간이동이 끝난 후 에너지 회복 재개
    }

    public bool IsNetworkEffectRequestValid(
        GameObject caster,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        return effectId == BlinkEffectId
            && _blickEffect != null
            && caster != null
            && Vector3.Distance(caster.transform.position + _playerBodyOffset, position)
                <= _maxBlinkDistance + EffectPositionTolerance;
    }

    public void PlayNetworkEffect(
        GameObject caster,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (effectId != BlinkEffectId || _blickEffect == null)
            return;

        // 순수 시각 효과이므로 각 피어가 로컬로 생성합니다.
        Instantiate(_blickEffect, position, rotation);
    }
}
