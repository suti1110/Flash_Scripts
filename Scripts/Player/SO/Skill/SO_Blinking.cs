using UnityEngine;

[CreateAssetMenu(fileName = "Blinking", menuName = "Player/Skill/Blinking")]
public class SO_Blinking : SO_Skill
{
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

    private PlayerCamera _playerCamera;

    public override void ExecuteSkill(PlayerSkill caster, IEnergyTracker energyTracker)
    {
        energyTracker.SetTrackingPause(true); // 순간이동 중에는 에너지 회복이 일어나지 않도록 함

        Transform transform = caster.transform;

        if (_playerCamera == null)
        {
            _playerCamera = transform.GetComponent<PlayerCamera>(); // 플레이어의 기본 설정 중 하나이기 때문에, null이 반환되지 않음
        }

        if (caster != null)
        {
            caster.RequestSpawnEffect(
                0,
                transform.position + _playerBodyOffset,
                Quaternion.identity
            ); // effectId: 0 (블링크 이펙트)
        }

        Vector3 newPosition = transform.position;

        if (
            Physics.SphereCast(
                transform.position + _playerBodyOffset,
                _sphereCastRadius,
                _playerCamera.CameraPivot.forward,
                out RaycastHit hit,
                _maxBlinkDistance,
                _wall
            )
        )
        {
            newPosition += _playerCamera.CameraPivot.forward * hit.distance;
        }
        else
        {
            newPosition += _playerCamera.CameraPivot.forward * _maxBlinkDistance;
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

        if (transform.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero; // 물리 속도 초기화 (튕겨나감 방지)
        }

        energyTracker.SetTrackingPause(false); // 순간이동이 끝난 후 에너지 회복 재개
    }

    public override void ServerSpawnEffect(int effectId, Vector3 position, Quaternion rotation)
    {
        // 순간이동 잔상 이펙트 스폰
        if (_blickEffect != null)
        {
            GameObject effect = Instantiate(_blickEffect, position, rotation);
            if (effect.TryGetComponent(out Unity.Netcode.NetworkObject netObj))
            {
                netObj.Spawn(true);
            }
        }
    }
}
