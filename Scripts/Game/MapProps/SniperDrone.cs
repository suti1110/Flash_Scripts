using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(NetworkObject))]
public sealed class SniperDrone : NetworkBehaviour
{
    // 서버가 표적과 발사 시각을 확정하고, 모든 피어는 복제된 같은 표적을 향해 조준선을 표시한다.
    [SerializeField]
    private Transform _muzzle;

    [SerializeField, Min(1f)]
    private float _range = 30f;

    [SerializeField, Min(0.1f)]
    private float _shotInterval = 2f;

    [SerializeField, Min(0f)]
    private float _telegraphDuration = 1f;

    [SerializeField, Range(0.01f, 1f)]
    [FormerlySerializedAs("_slowMultiplier")]
    private float _velocityMultiplier = 0.1f;

    [SerializeField]
    private LayerMask _lineOfSightMask = ~0;

    [SerializeField]
    private LineRenderer _aimLine;

    [SerializeField]
    private SniperDroneVisual _visual;

    [Header("Sound")]
    [SerializeField]
    private AudioClip _acquireAudio;

    [SerializeField]
    private AudioClip _fireAudio;

    [SerializeField]
    private AudioClip _hitAudio;

    private readonly NetworkVariable<NetworkObjectReference> _targetReference = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<double> _fireServerTime = new(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private PlayerVelocityDampener _target;
    private double _nextAcquireTime;
    private double _localFireTime;

    private void Awake()
    {
        if (_muzzle == null)
            _muzzle = transform;

        if (_visual == null)
            TryGetComponent(out _visual);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _targetReference.OnValueChanged += HandleTargetReferenceChanged;
        ResolveNetworkTarget(_targetReference.Value);

        if (IsServer)
            _nextAcquireTime = NetworkManager.ServerTime.Time;
    }

    public override void OnNetworkDespawn()
    {
        _targetReference.OnValueChanged -= HandleTargetReferenceChanged;
        _target = null;
        SetAimLine(false, default);
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        double currentTime = GetSimulationTime();

        if (_target == null)
        {
            SetAimLine(false, default);

            if (IsSpawned)
            {
                // 클라이언트는 서버가 복제한 NetworkObjectReference만 해석하며 자체 표적을 선택하지 않는다.
                ResolveNetworkTarget(_targetReference.Value);
                if (IsServer && _target == null && currentTime >= _nextAcquireTime)
                    AcquireTarget(currentTime);
            }
            else if (currentTime >= _nextAcquireTime)
            {
                AcquireTarget(currentTime);
            }

            return;
        }

        Vector3 targetPosition = _target.transform.position + Vector3.up;
        if (_visual != null)
            _visual.AimAt(targetPosition);
        else
            transform.rotation = Quaternion.LookRotation(targetPosition - transform.position, Vector3.up);

        SetAimLine(true, targetPosition);

        double fireTime = IsSpawned ? _fireServerTime.Value : _localFireTime;
        if (currentTime >= fireTime && (!IsSpawned || IsServer))
        {
            Fire(targetPosition, currentTime);
        }
    }

    private void AcquireTarget(double currentTime)
    {
        // 이미 GameManager에 등록된 플레이어만 순회하여 반복적인 씬 전체 검색과 배열 할당을 피한다.
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            _nextAcquireTime = currentTime + 0.5d;
            return;
        }

        IReadOnlyList<Player> players = gameManager.Players;
        float nearestSqrDistance = _range * _range;
        PlayerVelocityDampener nearest = null;

        for (int i = 0; i < players.Count; i++)
        {
            Player player = players[i];
            if (
                player == null
                || !player.isActiveAndEnabled
                || !player.TryGetComponent(out PlayerVelocityDampener candidate)
                || (IsSpawned && !candidate.IsSpawned)
            )
            {
                continue;
            }

            Vector3 offset = candidate.transform.position - transform.position;
            if (offset.sqrMagnitude >= nearestSqrDistance)
                continue;

            nearest = candidate;
            nearestSqrDistance = offset.sqrMagnitude;
        }

        if (nearest == null)
        {
            _nextAcquireTime = currentTime + 0.5d;
            return;
        }

        double fireTime = currentTime + _telegraphDuration;
        if (IsSpawned)
        {
            if (!IsServer || !nearest.NetworkObject.IsSpawned)
                return;

            // 발사 시각을 먼저 기록한 뒤 표적을 공개하여 클라이언트가 표적 수신 즉시 올바른 타이머를 사용하게 한다.
            _fireServerTime.Value = fireTime;
            _targetReference.Value = new NetworkObjectReference(nearest.NetworkObject);
        }
        else
        {
            _target = nearest;
            _localFireTime = fireTime;
            AudioManager.SfxPlayAtPoint(_acquireAudio, _muzzle.position);
        }
    }

    private void Fire(Vector3 targetPosition, double currentTime)
    {
        // 조준 시작 이후 플레이어가 엄폐했다면 Raycast의 최초 충돌이 대상이 아니므로 명중하지 않는다.
        Vector3 direction = targetPosition - _muzzle.position;
        bool hitTarget = Physics.Raycast(
            _muzzle.position,
            direction.normalized,
            out RaycastHit hit,
            _range,
            _lineOfSightMask,
            QueryTriggerInteraction.Ignore
        ) && hit.collider.GetComponentInParent<PlayerVelocityDampener>() == _target;

        if (hitTarget)
            _target.ApplyVelocityDampingFromServer(_velocityMultiplier);

        if (IsSpawned)
            PlayShotAudioRpc(hitTarget, targetPosition);
        else
            PlayShotAudioLocal(hitTarget, targetPosition);

        FinishShot(currentTime);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayShotAudioRpc(bool hitTarget, Vector3 targetPosition)
    {
        PlayShotAudioLocal(hitTarget, targetPosition);
    }

    private void PlayShotAudioLocal(bool hitTarget, Vector3 targetPosition)
    {
        AudioManager.SfxPlayAtPoint(_fireAudio, _muzzle.position);
        if (hitTarget)
            AudioManager.SfxPlayAtPoint(_hitAudio, targetPosition);
    }

    private void FinishShot(double currentTime)
    {
        if (IsSpawned && IsServer)
            _targetReference.Value = default;

        _target = null;
        _nextAcquireTime = currentTime + _shotInterval;
        SetAimLine(false, default);
    }

    private void HandleTargetReferenceChanged(
        NetworkObjectReference previousReference,
        NetworkObjectReference currentReference
    )
    {
        ResolveNetworkTarget(currentReference);
        if (_target != null)
            AudioManager.SfxPlayAtPoint(_acquireAudio, _muzzle.position);
    }

    private void ResolveNetworkTarget(NetworkObjectReference targetReference)
    {
        if (
            targetReference.TryGet(out NetworkObject targetNetworkObject)
            && targetNetworkObject.TryGetComponent(out PlayerVelocityDampener target)
        )
        {
            _target = target;
            return;
        }

        _target = null;
    }

    private double GetSimulationTime()
    {
        return IsSpawned ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    }

    private void SetAimLine(bool visible, Vector3 targetPosition)
    {
        if (_aimLine == null)
            return;

        _aimLine.enabled = visible;
        if (!visible)
            return;

        _aimLine.SetPosition(0, _muzzle.position);
        _aimLine.SetPosition(1, targetPosition);
    }

    private void OnValidate()
    {
        if (_muzzle == null)
            EditorLog.LogError("SniperDrone에 Muzzle이 설정되지 않았습니다.", this);

        if (_aimLine == null)
            EditorLog.LogError("SniperDrone에 Aim Line이 설정되지 않았습니다.", this);

        if (_visual == null)
            EditorLog.LogError("SniperDrone에 SniperDroneVisual이 설정되지 않았습니다.", this);
    }
}
// SniperDrone은 맵 소품 기믹의 물리 동작과 플레이어 상호작용 경계를 담당한다.
// 멀티플레이 중요 판정은 서버 권한에서 처리하고, 플레이어 공통 컴포넌트를 통해 결과를 적용한다.
