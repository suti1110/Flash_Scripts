using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour, IAttackable
{
    [SerializeField]
    private SO_Attacking _attacking;

    private readonly PlayerStateManager _state = PlayerStateManager.Instance;

    [SerializeField]
    private int _maxTarget = 100;
    private Collider[] _targets;
    private readonly Dictionary<Rigidbody, IDamagable> _damagableTargets = new();

    private NetworkObject _netObject;

    private void Awake()
    {
        _targets = new Collider[_maxTarget];
    }

    public void Attack()
    {
        _state[gameObject].State = PlayerState.Attacking;

        if (_netObject != null || TryGetComponent(out _netObject))
        {
            if (_netObject.IsOwner)
            {
                AudioManager.SfxPlay(AudioManager.Instance.Container.Attack);
            }
        }
    }

    public void AttackHit()
    {
        if (!IsOwner) // 공격은 소유자만 가능
            return;

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            _attacking.Range,
            _targets,
            _attacking.TargetLayer
        );

        _damagableTargets.Clear();

        // 한 객체 내에 여러 콜라이더가 있을 수 있으므로, Rigidbody 기준으로 중복 제거
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = _targets[i].attachedRigidbody;

            if (!rb || _damagableTargets.ContainsKey(rb))
                continue;

            if (rb.TryGetComponent(out IDamagable damagable))
            {
                _damagableTargets[rb] = damagable;
            }
        }

        // 공격 범위 내의 모든 IDamagable 객체에 데미지와 넉백 적용
        foreach (var target in _damagableTargets)
        {
            Vector3 direction = (target.Key.transform.position - transform.position).normalized;

            if (Vector3.Dot(transform.forward, direction) >= _attacking.RangeDot)
            {
                target.Value.TakeDamageRpc(
                    _attacking.Damage,
                    direction * _attacking.KnockbackForce
                );
            }
        }
    }

    private void OnValidate()
    {
        if (!_attacking)
        {
            EditorLog.LogError("SO_Attacking이 할당되지 않았습니다!", this);
        }
    }
}
