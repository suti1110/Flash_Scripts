using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour, IAttackable
{
    [SerializeField]
    private SO_Attacking _attacking;

    private readonly PlayerActionStateMachine _actionStateMachine = new();
    private PlayerStateMachine _playerStateMachine;

    [SerializeField]
    private int _maxTarget = 100;
    private Collider[] _targets;
    private readonly Dictionary<Rigidbody, PlayerDamage> _damageableTargets = new();
    private double _nextServerAttackTime;

    private NetworkObject _netObject;

    private void Awake()
    {
        _targets = new Collider[_maxTarget];
        _playerStateMachine = GetComponent<Player>().StateMachine;

        _actionStateMachine.Started += HandleAttackStarted;
        _actionStateMachine.ExecutionPointReached += AttackHit;
        _actionStateMachine.Completed += HandleAttackCompleted;
        _playerStateMachine.StateChanged += HandleStateChanged;
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner)
            return;

        _actionStateMachine.Tick(Time.deltaTime);
    }

    public void Attack()
    {
        if (
            Time.timeScale <= 0f
            || (IsSpawned && !IsOwner)
            || _actionStateMachine.IsRunning
        )
            return;

        _actionStateMachine.Start(_attacking.ActionDuration, _attacking.ExecuteTime);
    }

    private void HandleAttackStarted()
    {
        if (!_playerStateMachine.TryChangeState<PlayerAttackingState>())
        {
            _actionStateMachine.Cancel();
            return;
        }

        if (_netObject != null || TryGetComponent(out _netObject))
        {
            if (!_netObject.IsSpawned || _netObject.IsOwner)
            {
                AudioManager.SfxPlay(AudioManager.Instance.Container.Attack);
            }
        }
    }

    private void AttackHit()
    {
        if (IsSpawned && !IsOwner)
            return;

        if (IsSpawned)
        {
            AttackHitRpc();
            return;
        }

        ApplyAttackHitOnAuthority();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackHitRpc()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double minimumInterval = Mathf.Max(0.05f, _attacking.ActionDuration * 0.8f);
        if (now < _nextServerAttackTime)
            return;

        _nextServerAttackTime = now + minimumInterval;
        ApplyAttackHitOnAuthority();
    }

    private void ApplyAttackHitOnAuthority()
    {
        if (IsSpawned && !IsServer)
            return;

        if (TryGetComponent(out PlayerDamage attackerDamage) && !attackerDamage.IsAlive)
            return;

        // 서버에서는 호스트가 Player, 게스트가 OtherPlayer 레이어이므로
        // 공격자 관점과 무관하게 양쪽 플레이어 레이어를 모두 검색한 뒤 자신을 제외한다.
        int targetLayers =
            _attacking.TargetLayer | LayerMask.GetMask("Player", "OtherPlayer");

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            _attacking.Range,
            _targets,
            targetLayers
        );

        _damageableTargets.Clear();

        // 한 객체 내에 여러 콜라이더가 있을 수 있으므로, Rigidbody 기준으로 중복 제거
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = _targets[i].attachedRigidbody;

            if (!rb || _damageableTargets.ContainsKey(rb))
                continue;

            if (rb.transform == transform)
                continue;

            if (rb.TryGetComponent(out PlayerDamage damageable))
            {
                _damageableTargets[rb] = damageable;
            }
        }

        // 공격 범위 내의 모든 IDamageable 객체에 데미지와 넉백 적용
        foreach (var target in _damageableTargets)
        {
            Vector3 direction = (target.Key.transform.position - transform.position).normalized;

            if (Vector3.Dot(transform.forward, direction) >= _attacking.RangeDot)
            {
                CustomRoomSettings roomSettings = RelayManager.Instance != null
                    ? RelayManager.Instance.CurrentRoomSettings
                    : CustomRoomSettings.Default;
                int attackDamage = RelayManager.Instance != null && RelayManager.Instance.IsCustomRoom
                    ? roomSettings.AttackDamage
                    : _attacking.Damage;
                target.Value.TakeDamage(
                    attackDamage,
                    direction * (_attacking.KnockbackForce * roomSettings.KnockbackMultiplier)
                );
            }
        }
    }

    private void HandleAttackCompleted()
    {
        if (_playerStateMachine.IsInState<PlayerAttackingState>())
            _playerStateMachine.TryChangeState<PlayerIdleState>();
    }

    private void HandleStateChanged(PlayerState previousState, PlayerState currentState)
    {
        if (currentState is not PlayerAttackingState)
            _actionStateMachine.Cancel();
    }

    private void OnValidate()
    {
        if (!_attacking)
        {
            EditorLog.LogError("SO_Attacking이 할당되지 않았습니다!", this);
        }
    }

    public override void OnDestroy()
    {
        _actionStateMachine.Started -= HandleAttackStarted;
        _actionStateMachine.ExecutionPointReached -= AttackHit;
        _actionStateMachine.Completed -= HandleAttackCompleted;

        if (_playerStateMachine != null)
            _playerStateMachine.StateChanged -= HandleStateChanged;

        base.OnDestroy();
    }
}
