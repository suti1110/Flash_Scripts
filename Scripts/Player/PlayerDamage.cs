using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerDeath))]
public class PlayerDamage : NetworkBehaviour, IDamageable
{
    private const float BgmDuckDuration = 0.2f;

    [SerializeField]
    private SO_TakingDamage _takingDamage;

    [SerializeField]
    private SO_GameModeFilter _damageModeFilter;

    private readonly PlayerActionStateMachine _actionStateMachine = new();
    private PlayerStateMachine _playerStateMachine;

    [SerializeField]
    private Rigidbody _rb;

    private IDeathable _death;
    private PlayerCamera _playerCamera;
    private AudioSource _localHitAudioSource;
    private Quaternion _damageFacingRotation;
    private bool _hasDamageFacingRotation;

    private readonly NetworkVariable<int> _hp = new();

    public int CurrentHealth => _hp.Value;
    public int MaximumHealth
    {
        get
        {
            return RelayManager.Instance != null && RelayManager.Instance.IsCustomRoom
                ? RelayManager.Instance.CurrentRoomSettings.MaximumHealth
                : _takingDamage.MaxHp;
        }
    }
    public bool IsAlive => _hp.Value > 0;

    public event Action<int, int> OnHealthChanged;

    private void Awake()
    {
        _death = GetComponent<IDeathable>();
        _playerCamera = GetComponent<PlayerCamera>();
        _playerStateMachine = GetComponent<Player>().StateMachine;

        _actionStateMachine.Started += HandleDamageStarted;
        _actionStateMachine.Completed += HandleDamageCompleted;
        _playerStateMachine.StateChanged += HandleStateChanged;
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner)
            return;

        _actionStateMachine.Tick(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (
            (IsSpawned && !IsOwner)
            || _rb == null
            || !_hasDamageFacingRotation
            || !_playerStateMachine.IsInState<PlayerTakingDamageState>()
        )
            return;

        _rb.MoveRotation(_damageFacingRotation);
    }

    public override void OnNetworkSpawn()
    {
        _hp.OnValueChanged += HandleHealthChanged;

        if (IsServer)
        {
            _hp.Value = MaximumHealth;
        }

        OnHealthChanged?.Invoke(_hp.Value, _hp.Value);
    }

    public override void OnNetworkDespawn()
    {
        _hp.OnValueChanged -= HandleHealthChanged;
    }

    private void HandleHealthChanged(int previousValue, int newValue)
    {
        OnHealthChanged?.Invoke(previousValue, newValue);
    }

    public void TakeDamage(int damage, Vector3 knockback)
    {
        TakeDamageOnServer(damage, knockback);
    }

    // 서버가 확정한 피해가 즉시 리스폰을 발생시켰는지 호출자에게 돌려준다.
    // IDamageable 공개 계약은 유지하면서 명중 표현이 리스폰 상태를 덮지 않게 한다.
    internal bool TakeDamageOnServer(int damage, Vector3 knockback)
    {
        if (!IsSpawned || !IsServer || damage <= 0)
            return false;

        return ApplyDamageOnServer(damage, knockback, _takingDamage.ActionDuration);
    }

    // 서버에서 이미 검증된 맵 기믹은 RPC를 되돌아 거치지 않고 같은 대미지 규칙을 사용한다.
    // 반환값은 이 피해가 사망 처리를 발생시켰는지를 나타내며 중복 재스폰을 방지하는 데 사용한다.
    internal bool ApplyDamageOnServer(int damage, Vector3 knockback, float hitStunDuration)
    {
        if (!IsServer || damage <= 0 || _hp.Value <= 0)
            return false;

        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            EditorLog.LogError("대미지 판정에 필요한 GameManager를 찾을 수 없습니다.", this);
            return false;
        }

        bool appliesHealthDamage =
            _damageModeFilter != null && _damageModeFilter.IsAllowed(gameManager.GameKind);

        if (appliesHealthDamage)
            _hp.Value = Mathf.Max(0, _hp.Value - damage);

        bool wasLethal = appliesHealthDamage && _hp.Value <= 0;
        ApplyDamageFeedbackRpc(knockback, Mathf.Max(0f, hitStunDuration));

        if (wasLethal)
            _death.Death();

        return wasLethal;
    }

    internal void RestoreFullHealth()
    {
        if (!IsServer)
            return;

        _hp.Value = MaximumHealth;
    }

    [Rpc(SendTo.Owner)]
    private void ApplyDamageFeedbackRpc(Vector3 knockback, float hitStunDuration)
    {
        PlayLocalDamagePresentation();

        // 1. 피격 상태머신을 먼저 시작하여 TakingDamage 상태로 전이합니다.
        // 상태 전이를 통해 진행 중이던 스킬 취소 및 PlayerSkillMotion의 Kinematic이 먼저 안전하게 해제됩니다.
        if (_actionStateMachine.IsRunning)
            _actionStateMachine.Cancel();

        // 0초 경직도 피격 상태 진입과 완료를 즉시 실행하여 피격 규칙은 유지하되 조작 지연은 만들지 않는다.
        _actionStateMachine.Start(hitStunDuration, 1f);

        // 2. Kinematic이 완전히 해제된 상태에서 속도를 초기화하고 넉백 Force를 적용합니다.
        if (_rb != null)
        {
            FaceIncomingImpact(knockback);
            _rb.linearVelocity = Vector3.zero;
            _rb.AddForce(knockback, ForceMode.Impulse);
        }
    }

    private void FaceIncomingImpact(Vector3 knockback)
    {
        Vector3 facingDirection = Vector3.ProjectOnPlane(-knockback, Vector3.up);
        if (facingDirection.sqrMagnitude <= 0.0001f)
            return;

        _damageFacingRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
        _hasDamageFacingRotation = true;
        _rb.angularVelocity = Vector3.zero;
        _rb.MoveRotation(_damageFacingRotation);
    }

    private void PlayLocalDamagePresentation()
    {
        if (_playerCamera != null)
        {
            _playerCamera.PlayDamageFeedback(
                _takingDamage.CameraRippleStrength,
                _takingDamage.CameraRippleDuration
            );
        }

        AudioManager audioManager = AudioManager.Instance;
        AudioClip hitClip = audioManager != null ? audioManager.Container?.Hit : null;
        Camera camera = _playerCamera != null ? _playerCamera.MainCamera : null;
        if (hitClip == null || camera == null)
            return;

        if (_localHitAudioSource == null)
        {
            _localHitAudioSource = camera.gameObject.AddComponent<AudioSource>();
            AudioManager.ConfigureListenerSfxSource(_localHitAudioSource);
        }

        AudioManager.PlayListenerSfxWithBgmDuck(
            hitClip,
            _localHitAudioSource,
            _takingDamage.HitSfxVolume,
            _takingDamage.BgmDuckVolumeRatio,
            BgmDuckDuration,
            _takingDamage.BgmRecoveryDuration
        );
    }

    private void HandleDamageStarted()
    {
        if (!_playerStateMachine.TryChangeState<PlayerTakingDamageState>())
            _actionStateMachine.Cancel();
    }

    private void HandleDamageCompleted()
    {
        if (_playerStateMachine.IsInState<PlayerTakingDamageState>())
            _playerStateMachine.ForceChangeState<PlayerIdleState>();
    }

    private void HandleStateChanged(PlayerState previousState, PlayerState currentState)
    {
        if (currentState is not PlayerTakingDamageState)
        {
            _hasDamageFacingRotation = false;
            _actionStateMachine.Cancel();
        }
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

    public override void OnDestroy()
    {
        _actionStateMachine.Started -= HandleDamageStarted;
        _actionStateMachine.Completed -= HandleDamageCompleted;

        if (_playerStateMachine != null)
            _playerStateMachine.StateChanged -= HandleStateChanged;

        base.OnDestroy();
    }
}
