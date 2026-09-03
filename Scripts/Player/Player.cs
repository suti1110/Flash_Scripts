using System;
using UnityEngine;
using UnityEngine.InputSystem;

public interface IPlayerMovingInput
{
    public PlayerMoving Moving { get; }
}

[RequireComponent(typeof(PlayerMove), typeof(PlayerJump), typeof(PlayerAttack))]
public class Player : MonoBehaviour, IPlayerMovingInput
{
    [SerializeField]
    private PlayerStateCatalog _stateCatalog;

    [SerializeField]
    private PlayerJumpingStateCatalog _jumpingStateCatalog;

    private IMovable _move;
    private IJumpable _jump;
    private IAttackable _attack;
    private ISkill _skill;
    private PlayerMapInteraction _mapInteraction;

    private readonly PlayerStateMachine _stateMachine = new();
    private readonly PlayerJumpingStateMachine _jumpingStateMachine = new();
    private PlayerInputType _currentInputConstraints;

    private float _vertical;
    private float _horizontal;

    private PlayerMoving _moving;
    private PlayerJumping _jumping;
    private PlayerAttacking _attacking;
    private PlayerInteracting _interacting;
    private PlayerUsingSkill _usingSkill;
    private InputAction[] _skillInputActions;
    private Action<InputAction.CallbackContext>[] _skillInputHandlers;

    public PlayerMoving Moving => _moving;
    public PlayerStateMachine StateMachine => _stateMachine;
    public PlayerJumpingStateMachine JumpingStateMachine => _jumpingStateMachine;

    private void Awake()
    {
        _move = GetComponent<IMovable>();
        _jump = GetComponent<IJumpable>();
        _attack = GetComponent<IAttackable>();
        _skill = GetComponent<ISkill>();
        _mapInteraction = GetComponent<PlayerMapInteraction>();

        _moving = new PlayerMoving();
        _jumping = new PlayerJumping();
        _attacking = new PlayerAttacking();
        _interacting = new PlayerInteracting();
        _usingSkill = new PlayerUsingSkill();
        _jumping.Jumping.Jump.performed += input => _jump.Jump();
        _attacking.Attacking.Attack.performed += input => _attack.Attack();
        _interacting.Interacting.Interact.performed += input => _mapInteraction?.Interact();
        BindSkillInputs();
    }

    private void Start()
    {
        PlayerJumpingStateContext jumpingStateContext = new(this);
        _jumpingStateMachine.Initialize(jumpingStateContext, _jumpingStateCatalog);

        PlayerStateContext stateContext = new(this);
        _stateMachine.Initialize(stateContext, _stateCatalog);
    }

    internal void ApplyInputConstraints(PlayerInputType constraints)
    {
        _currentInputConstraints = constraints;

        if (constraints.HasFlag(PlayerInputType.Moving))
            _moving.Disable();
        else
            _moving.Enable();

        SetJumpInputEnabled(_jumpingStateMachine.CurrentState.AllowsJumpInput);

        if (constraints.HasFlag(PlayerInputType.Attacking))
            _attacking.Disable();
        else
            _attacking.Enable();

        if (constraints.HasFlag(PlayerInputType.Interacting))
            _interacting.Disable();
        else
            _interacting.Enable();

        if (constraints.HasFlag(PlayerInputType.UsingSkill))
            _usingSkill.Disable();
        else
            _usingSkill.Enable();
    }

    internal void SetJumpInputEnabled(bool isEnabledByJumpingState)
    {
        bool isConstrained = _currentInputConstraints.HasFlag(PlayerInputType.Jumping);

        if (isEnabledByJumpingState && !isConstrained)
            _jumping.Enable();
        else
            _jumping.Disable();
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.RegisterPlayer(this);

        if (_stateMachine.IsInitialized)
            ApplyInputConstraints(_currentInputConstraints);
        else
        {
            _moving.Enable();
            _jumping.Enable();
            _attacking.Enable();
            _interacting.Enable();
            _usingSkill.Enable();
        }

        GetComponent<PlayerStateInputLifetime>()?.Resume();
    }

    private void FixedUpdate()
    {
        _stateMachine.FixedTick(Time.fixedDeltaTime);
        _move.Move(new Vector3(_horizontal, 0, _vertical).normalized);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            _vertical = 0f;
            _horizontal = 0f;
            return;
        }

        // 투척처럼 프레임 기반 지속시간을 가진 현재 State만 진행하며, 일시정지 중에는 위에서 함께 멈춘다.
        _stateMachine.Tick(Time.deltaTime);

        _vertical = _moving.Move.Vertical.ReadValue<float>();
        _horizontal = _moving.Move.Horizontal.ReadValue<float>();
    }

    private void OnDisable()
    {
        _moving.Disable();
        _jumping.Disable();
        _attacking.Disable();
        _interacting.Disable();
        _usingSkill.Disable();
        GetComponent<PlayerStateInputLifetime>()?.Suspend();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterPlayer(this);
        UnbindSkillInputs();
        _stateMachine.Dispose();
        _jumpingStateMachine.Dispose();

        _moving.Dispose();
        _jumping.Dispose();
        _attacking.Dispose();
        _interacting.Dispose();
        _usingSkill.Dispose();
    }

    private void OnValidate()
    {
        if (_stateCatalog == null)
            Debug.LogError("Player에 PlayerStateCatalog가 설정되지 않았습니다.", this);
        if (_jumpingStateCatalog == null)
            Debug.LogError("Player에 PlayerJumpingStateCatalog가 설정되지 않았습니다.", this);
    }

    public void FreezeForMatchEnd()
    {
        enabled = false;

        if (TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null)
            animator.enabled = false;
    }

    internal void EnterDeadState()
    {
        if (_stateMachine.IsInitialized)
            _stateMachine.TryChangeState<PlayerDeadState>();
    }

    internal void ResetAfterRespawn()
    {
        if (!_stateMachine.IsInitialized || !_jumpingStateMachine.IsInitialized)
            return;

        _stateMachine.ForceChangeState<PlayerIdleState>();
        _jumpingStateMachine.ForceChangeState<PlayerJumpingIdleState>();
    }

    private void BindSkillInputs()
    {
        var actions = _usingSkill.asset.FindActionMap("UsingSkill", true).actions;
        _skillInputActions = new InputAction[actions.Count];
        _skillInputHandlers = new Action<InputAction.CallbackContext>[actions.Count];

        for (int i = 0; i < actions.Count; i++)
        {
            int slotIndex = i;
            InputAction action = actions[i];
            Action<InputAction.CallbackContext> handler = _ => _skill.UseSkill(slotIndex);

            _skillInputActions[i] = action;
            _skillInputHandlers[i] = handler;
            action.performed += handler;
        }
    }

    private void UnbindSkillInputs()
    {
        if (_skillInputActions == null || _skillInputHandlers == null)
            return;

        for (int i = 0; i < _skillInputActions.Length; i++)
        {
            if (_skillInputActions[i] != null)
                _skillInputActions[i].performed -= _skillInputHandlers[i];
        }
    }
}
