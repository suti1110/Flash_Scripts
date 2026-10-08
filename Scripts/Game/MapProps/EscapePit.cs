using System.Collections.Generic;
using System;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Collider))]
public sealed class EscapePit : MonoBehaviour
{
    // 구덩이는 피해를 주지 않으며, 탈출 횟수와 입·출구 위치만 제공한다.
    // 실제 입력 소비와 플레이어 물리 고정은 PlayerTrappedState가 담당한다.
    [SerializeField, Min(1)]
    private int _requiredJumpPresses = 6;

    [SerializeField]
    private Transform _trapPoint;

    [SerializeField]
    private Transform _escapePoint;

    [SerializeField, Min(0f)]
    private float _releaseUpwardSpeed = 6f;

    [Header("Sound")]
    [SerializeField]
    private AudioClip _trapAudio;

    [SerializeField]
    private AudioClip _struggleAudio;

    [SerializeField]
    private AudioClip _releaseAudio;

    [Header("Trap presentation")]
    [SerializeField] private DecalProjector _projector;
    [SerializeField] private Material _crackedMaterial;
    [SerializeField] private Material _openMaterial;
    [SerializeField] private ParticleSystem _collapseDust;
    [SerializeField] private ParticleSystem _struggleDust;
    [SerializeField] private ParticleSystem _releaseDust;

    // 표현은 각 피어에서 재생한다. 여러 명이 같은 구덩이에 있으면 마지막 플레이어가 나갈 때 복구한다.
    private readonly HashSet<PlayerPitPresentation> _occupants = new();
    private static readonly HashSet<EscapePit> ActivePits = new();
    private static readonly HashSet<Rigidbody> ReleasingBodies = new();
    public const float PositionTransitionDuration = 0.2f;
    private Vector3 _projectorSize;
    private float _pulseRemaining;
    public bool IsVisuallyOpen => _occupants.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        ActivePits.Clear();
        ReleasingBodies.Clear();
    }

    private void Awake()
    {
        if (_projector != null)
            _projectorSize = _projector.size;
    }

    private void OnEnable()
    {
        ActivePits.Add(this);
        RefreshDecal();
    }

    private void OnDisable()
    {
        ActivePits.Remove(this);
        _occupants.Clear();
        _pulseRemaining = 0f;
        if (_projector != null)
            _projector.size = _projectorSize;
        RefreshDecal();
        StopDust(_collapseDust);
        StopDust(_struggleDust);
        StopDust(_releaseDust);
    }

    private void Update()
    {
        if (_occupants.RemoveWhere(occupant => occupant == null) > 0)
            RefreshDecal();
        if (_projector == null || _pulseRemaining <= 0f)
            return;

        // 저항할 때 흙 가장자리가 순간적으로 벌어졌다 가라앉는 느낌을 준다. 판정 크기는 바꾸지 않는다.
        _pulseRemaining = Mathf.Max(0f, _pulseRemaining - Time.deltaTime);
        float expansion = 1f + Mathf.Sin((1f - _pulseRemaining / 0.22f) * Mathf.PI) * 0.035f;
        _projector.size = new Vector3(_projectorSize.x * expansion, _projectorSize.y * expansion, _projectorSize.z);
    }

    // 정적인 맵 소품에는 NetworkObject가 없으므로 동기화된 함정 위치를 같은 씬의 소품에 연결한다.
    internal static EscapePit FindAt(Vector3 trapPosition)
    {
        foreach (EscapePit pit in ActivePits)
            if (pit != null && (pit.TrapPosition - trapPosition).sqrMagnitude < 0.01f)
                return pit;
        return null;
    }

    internal void SetOccupied(PlayerPitPresentation occupant, bool occupied, bool playEffect = true)
    {
        bool changed = occupied ? _occupants.Add(occupant) : _occupants.Remove(occupant);
        if (!changed)
            return;
        RefreshDecal();
        if (playEffect)
            Burst(occupied ? _collapseDust : _releaseDust, occupied ? 48 : 32);
        if (!IsVisuallyOpen)
        {
            _pulseRemaining = 0f;
            if (_projector != null)
                _projector.size = _projectorSize;
        }
    }

    internal void ShowStruggle()
    {
        _pulseRemaining = 0.22f;
        Burst(_struggleDust, 14);
    }

    private void RefreshDecal()
    {
        if (_projector != null)
            _projector.material = IsVisuallyOpen ? _openMaterial : _crackedMaterial;
    }

    private static void Burst(ParticleSystem particles, int count)
    {
        if (particles != null)
        {
            particles.Play();
            particles.Emit(count);
        }
    }

    private static void StopDust(ParticleSystem particles)
    {
        if (particles != null)
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public int RequiredJumpPresses => _requiredJumpPresses;
    public Vector3 TrapPosition => _trapPoint != null ? _trapPoint.position : transform.position;

    private void OnTriggerEnter(Collider other)
    {
        TryTrap(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryTrap(other);
    }

    private void TryTrap(Collider other)
    {
        Player player = other.GetComponentInParent<Player>();
        if (player == null || !player.isActiveAndEnabled || !player.StateMachine.IsInitialized)
            return;

        // 탈출 보간 중에는 아직 Trigger 안에 있어도 다시 갇히지 않는다.
        if (ReleasingBodies.Contains(player.GetComponent<Rigidbody>()))
            return;

        // Owner 권한 플레이어만 자신의 State와 물리 위치를 변경하고 NetworkTransform으로 결과를 전파한다.
        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner)
            return;

        if (player.StateMachine.TryChangeState<PlayerTrappedState>(state => state.Configure(this)))
            AudioManager.SfxPlayAtPoint(_trapAudio, player.transform.position);
    }

    internal Tween Release(Rigidbody rigidbody, Action onCompleted = null, bool preserveVelocity = false)
    {
        // 물리 주기에 맞춰 0.2초간 탈출 위치로 이동한다. 피격으로 탈출하는 경우에는
        // 피격 State가 적용한 넉백 속도를 보존하며, 정상 탈출만 완료 후 위쪽 속도를 부여한다.
        Vector3 destination = _escapePoint != null
            ? _escapePoint.position
            : transform.position + transform.up * 2f;
        ReleasingBodies.Add(rigidbody);
        return rigidbody.DOMove(destination, PositionTransitionDuration)
            .SetEase(Ease.InOutSine)
            .SetUpdate(UpdateType.Fixed)
            .SetLink(rigidbody.gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (!preserveVelocity)
                    rigidbody.linearVelocity = transform.up * _releaseUpwardSpeed;
                AudioManager.SfxPlayAtPoint(_releaseAudio, destination);
                onCompleted?.Invoke();
            })
            .OnKill(() => ReleasingBodies.Remove(rigidbody));
    }

    internal void PlayStruggleAudio(Vector3 position)
    {
        AudioManager.SfxPlayAtPoint(_struggleAudio, position);
    }

    private void OnValidate()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = true;
    }
}
// EscapePit은 맵 소품 기믹의 물리 동작과 플레이어 상호작용 경계를 담당한다.
// 멀티플레이 중요 판정은 서버 권한에서 처리하고, 플레이어 공통 컴포넌트를 통해 결과를 적용한다.
