using Unity.Netcode;
using UnityEngine;

public interface IDamagableOnServer
{
    public NetworkVariable<int> Hp { get; }
}

[RequireComponent(typeof(PlayerDeath))]
public class PlayerDamage : NetworkBehaviour, IDamagable, IDamagableOnServer
{
    [SerializeField]
    private SO_TakingDamage _takingDamage;

    [SerializeField]
    private SO_GameModeFilter _damageModeFilter;

    private readonly PlayerStateManager _state = PlayerStateManager.Instance;

    [SerializeField]
    private Rigidbody _rb;

    private IDeathable _death;

    public NetworkVariable<int> Hp { get; } = new NetworkVariable<int>();

    private void Awake()
    {
        _death = GetComponent<IDeathable>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Hp.Value = _takingDamage.MaxHp;
        }
    }

    // 죽음 체크 (타격 애니메이션 이후 실행)
    public void DeathCheck()
    {
        if (Hp.Value <= 0)
        {
            _death.Death();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)] // 서버를 통해서만 실행 가능
    public void TakeDamageRpc(int damage, Vector3 knockback)
    {
        if (_damageModeFilter != null && _damageModeFilter.IsAllowed(GameManager.Instance.GameKind))
            Hp.Value -= damage;

        ExecuteTakeDamageRpc(knockback);
    }

    [Rpc(SendTo.Owner)]
    private void ExecuteTakeDamageRpc(Vector3 knockback)
    {
        AudioManager.SfxPlay(AudioManager.Instance.Container.Hit);

        _rb.linearVelocity = Vector3.zero;
        _rb.AddForce(knockback, ForceMode.Impulse);

        _state[gameObject].State = PlayerState.TakingDamage; // 타격 애니메이션 시작
    }

    private void OnValidate()
    {
        if (!_damageModeFilter)
        {
            EditorLog.LogError("SO_GameModeFilter가 할당되지 않았습니다!", this);
        }

        if (!_takingDamage)
        {
            EditorLog.LogError("SO_TakingDamage가 할당되지 않았습니다!", this);
        }

        if (!_rb)
        {
            EditorLog.LogError("Rigidbody가 할당되지 않았습니다!", this);
        }
    }
}
