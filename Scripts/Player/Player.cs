using UnityEngine;

public interface IPlayerMovingInput
{
    public PlayerMoving Moving { get; }
}

[RequireComponent(typeof(PlayerMove), typeof(PlayerJump), typeof(PlayerAttack))]
[RequireComponent(typeof(PlayerAttack))]
public class Player : MonoBehaviour, IPlayerMovingInput
{
    private IMovable _move;
    private IJumpable _jump;
    private IAttackable _attack;
    private ISkill _skill;

    private readonly PlayerStateManager _state = PlayerStateManager.Instance;

    private readonly PlayerJumpingStateManager _jumpingState = PlayerJumpingStateManager.Instance;

    private float _vertical;
    private float _horizontal;

    private PlayerMoving _moving;
    private PlayerJumping _jumping;
    private PlayerAttacking _attacking;
    private PlayerUsingSkill _usingSkill;

    public PlayerMoving Moving => _moving;

    private void Awake()
    {
        _move = GetComponent<IMovable>();
        _jump = GetComponent<IJumpable>();
        _attack = GetComponent<IAttackable>();
        _skill = GetComponent<ISkill>();

        _moving = new PlayerMoving();
        _jumping = new PlayerJumping();
        _attacking = new PlayerAttacking();
        _usingSkill = new PlayerUsingSkill();
        _jumping.Jumping.Jump.performed += input => _jump.Jump();
        _attacking.Attacking.Attack.performed += input => _attack.Attack();
        _usingSkill.UsingSkill.First.performed += input => _skill.UseSkill(0);
        _usingSkill.UsingSkill.Second.performed += input => _skill.UseSkill(1);

        _state[gameObject].OnStateChanged += OnStateChanged;
        _jumpingState[gameObject].OnJumpingStateChanged += OnJumpingStateChanged;
        _skill.OnSkillFinished += OnSkillFinished;
    }

    private void OnStateChanged(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.Idle:
                // 기본 상태일 땐 움직임과 공격 스킬 모두 활성화
                _moving.Enable();
                _attacking.Enable();
                _usingSkill.Enable();
                break;
            case PlayerState.Attacking:
                // 공격 중인 상태일 땐 움직임은 활성화 스킬과 공격은 비활성화
                _moving.Enable();
                _attacking.Disable();
                _usingSkill.Disable();
                break;
            case PlayerState.TakingDamage:
                // 대미지를 입은 상태일 땐 움직임과 공격, 스킬 모두 비활성화
                _moving.Disable();
                _attacking.Disable();
                _usingSkill.Disable();
                break;
            case PlayerState.UsingSkill:
                ApplyInputConstraints(_skill.GetCurrentSkillConstraints());
                break;
            case PlayerState.Dead:
                _moving.Enable();
                _attacking.Disable();
                _usingSkill.Disable();
                _state[gameObject].IsLocked = true; // 상태 고정
                _jumpingState[gameObject].JumpingState = PlayerJumpingState.Dead;
                break;
        }
    }

    private void OnSkillFinished()
    {
        _jumpingState[gameObject].IsLocked = false;
        OnStateChanged(_state[gameObject].State);
        OnJumpingStateChanged(_jumpingState[gameObject].JumpingState);
    }

    private void ApplyInputConstraints(PlayerInputType constraints)
    {
        if (constraints.HasFlag(PlayerInputType.Moving))
            _moving.Disable();
        if (constraints.HasFlag(PlayerInputType.Jumping))
            _jumping.Disable();
        if (constraints.HasFlag(PlayerInputType.Attacking))
            _attacking.Disable();
        if (constraints.HasFlag(PlayerInputType.UsingSkill))
            _usingSkill.Disable();
    }

    private void OnJumpingStateChanged(PlayerJumpingState state)
    {
        switch (state)
        {
            case PlayerJumpingState.Idle:
                // 점프 중이 아닐 때는 점프 입력 활성화
                _jumping.Enable();
                break;
            case PlayerJumpingState.Jumping:
                // 점프 중일 때는 점프 입력 비활성화
                _jumping.Disable();
                break;
            case PlayerJumpingState.Falling:
                // 낙하 중일 때는 점프 입력 비활성화
                _jumping.Disable();
                break;
            case PlayerJumpingState.UsingSkill:
                _jumpingState[gameObject].IsLocked = true;
                break;
            case PlayerJumpingState.Dead:
                _jumping.Disable();
                _jump.enabled = false; // 비활성화
                _jumpingState[gameObject].IsLocked = true; // 상태 고정
                break;
        }
    }

    private void OnEnable()
    {
        _moving.Enable();
        _jumping.Enable();
        _attacking.Enable();
        _usingSkill.Enable();
    }

    private void FixedUpdate()
    {
        _move.Move(new Vector3(_horizontal, 0, _vertical).normalized);
    }

    private void Update()
    {
        _vertical = _moving.Move.Vertical.ReadValue<float>();
        _horizontal = _moving.Move.Horizontal.ReadValue<float>();
    }

    private void OnDisable()
    {
        _moving.Disable();
        _jumping.Disable();
        _attacking.Disable();
        _usingSkill.Disable();
    }

    private void OnDestroy()
    {
        _moving.Dispose();
        _jumping.Dispose();
        _attacking.Dispose();
        _usingSkill.Dispose();

        _jumpingState[gameObject].OnJumpingStateChanged -= OnJumpingStateChanged;
        _skill.OnSkillFinished -= OnSkillFinished;
    }
}
