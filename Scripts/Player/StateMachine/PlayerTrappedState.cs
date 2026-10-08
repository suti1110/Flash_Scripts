using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

// 구덩이에 갇힌 동안 일반 플레이어 입력을 차단하고 독립된 점프 Action Map을 탈출 입력으로 사용한다.
// 구덩이 진행도, 위치 고정과 탈출 완료를 한 State에 모아 Player의 일반 점프 경로에 예외를 만들지 않는다.
[CreateAssetMenu(fileName = "PlayerTrappedState", menuName = "Flash/Player/States/Trapped")]
public sealed class PlayerTrappedState : PlayerState
{
    [Header("탈출 횟수 표시")]
    [SerializeField]
    private WorldCountIndicatorView _escapeIndicatorPrefab;

    [SerializeField]
    private Vector3 _escapeIndicatorLocalPosition = new(0f, 2.25f, 0f);

    [Header("탈출 점프 액션")]
    [SerializeField, Min(0.05f)]
    private float _escapeJumpDuration = 0.3f;

    [Header("탈출 점프 표현 정책")]
    [SerializeField]
    private PlayerBaseAnimationFlags _escapeJumpAnimationFlags;

    private Player _player;
    private PlayerAnimation _animation;
    private Rigidbody _rigidbody;
    private PlayerJumping _escapeInput;
    private PlayerPitPresentation _pitPresentation;
    private EscapePit _pit;
    private WorldCountIndicatorView _escapeIndicator;
    private int _escapeProgress;
    private bool _isActive;
    private Vector3 _entryPosition;
    private float _entryElapsed;
    private bool _isEscaping;
    private Tween _releaseTween;
    private readonly PlayerActionStateMachine _escapeJumpActionStateMachine = new();

    public int EscapeProgress => _escapeProgress;
    public int RequiredEscapePresses => _pit != null ? _pit.RequiredJumpPresses : 0;

    internal override void Initialize(PlayerStateContext context)
    {
        _player = context.Player;
        _animation = _player.GetComponent<PlayerAnimation>();
        _rigidbody = _player.GetComponent<Rigidbody>();
        _pitPresentation = _player.GetComponent<PlayerPitPresentation>();

        // 탈출 점프 입력 시 인스펙터에 설정된 Base Layer 표현 플래그를 적용한다.
        _escapeJumpActionStateMachine.Started += () =>
            _animation.ApplyBaseLayerState(_escapeJumpAnimationFlags);
        _escapeJumpActionStateMachine.Completed += () =>
            _animation.ApplyBaseLayerState(BaseAnimationFlags);
        _escapeJumpActionStateMachine.Cancelled += () =>
            _animation.ApplyBaseLayerState(BaseAnimationFlags);

        // Player가 소유한 일반 점프와 별개의 InputActionAsset을 State 수명 동안 한 번만 생성한다.
        _escapeInput = new PlayerJumping();
        _escapeInput.Jumping.Jump.performed += HandleEscapeInput;

        PlayerStateInputLifetime lifetime = _player.GetComponent<PlayerStateInputLifetime>();
        if (lifetime == null)
            lifetime = _player.gameObject.AddComponent<PlayerStateInputLifetime>();

        lifetime.Register(
            _escapeInput,
            _escapeInput,
            () => _isActive,
            () => _escapeInput.Jumping.Jump.performed -= HandleEscapeInput
        );
    }

    internal void Configure(EscapePit pit)
    {
        _pit = pit != null ? pit : throw new ArgumentNullException(nameof(pit));
    }

    internal override void Enter(PlayerStateContext context)
    {
        if (_pit == null)
            throw new InvalidOperationException("PlayerTrappedState에 EscapePit이 설정되지 않았습니다.");

        // Player 소유 입력은 State 공통 정책에서 이미 잠겼으므로 탈출 전용 입력만 활성화한다.
        _escapeProgress = 0;
        _isActive = true;
        _isEscaping = false;
        _entryPosition = _rigidbody.position;
        _entryElapsed = 0f;
        _rigidbody.linearVelocity = Vector3.zero;
        _pitPresentation?.Begin(_pit);
        _escapeInput.Enable();
        CreateEscapeIndicator();
        UpdateEscapeIndicator();
    }

    internal override void PrepareExitTo(PlayerStateContext context, PlayerState nextState)
    {
        if (nextState is not PlayerTakingDamageState || _pit == null)
            return;

        // 피격은 정상 탈출 횟수와 무관하게 구덩이를 즉시 해제한다.
        // 탈출 위치까지 보간하는 동안 재진입을 막고, 이후 적용되는 넉백 속도를 덮어쓰지 않는다.
        _releaseTween?.Kill();
        _releaseTween = null;
        _pit.Release(_rigidbody, preserveVelocity: true);
    }

    internal override void Exit(PlayerStateContext context)
    {
        // 사망이나 피격 등 다른 State로 전이되어도 전용 입력 및 점프 액션이 즉시 종료되도록 Exit에서 정리한다.
        _isActive = false;
        // 사망 등 다른 상태가 탈출을 중단하면 뒤늦은 완료 콜백과 위치 이동도 중단한다.
        _releaseTween?.Kill();
        _releaseTween = null;
        // 정상 탈출뿐 아니라 피격·사망으로 상태를 떠날 때도 바닥 표현을 복원한다.
        _pitPresentation?.End();
        _escapeInput.Disable();
        _escapeJumpActionStateMachine.Cancel();
        DestroyEscapeIndicator();
        _escapeProgress = 0;
        _pit = null;
    }

    internal override void FixedTick(PlayerStateContext context, float fixedDeltaTime)
    {
        if (!_isActive || _pit == null)
            return;

        // 정상 탈출 중에는 DOMove가 위치를 담당한다. 구덩이 고정으로 보간을 덮어쓰지 않는다.
        if (_isEscaping)
            return;

        _escapeJumpActionStateMachine.Tick(fixedDeltaTime);
        _rigidbody.linearVelocity = Vector3.zero;
        _entryElapsed = Mathf.Min(EscapePit.PositionTransitionDuration, _entryElapsed + fixedDeltaTime);
        float progress = _entryElapsed / EscapePit.PositionTransitionDuration;
        float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
        _rigidbody.MovePosition(Vector3.Lerp(_entryPosition, _pit.TrapPosition, easedProgress));
    }

    private void HandleEscapeInput(InputAction.CallbackContext context)
    {
        if (!_isActive || _pit == null || _isEscaping || Time.timeScale <= 0f)
            return;

        // 실제 탈출 입력이 수락될 때마다 발버둥 액션을 시작하여 IsTrapJumping 애니메이션을 지정된 시간 동안 유지한다.
        if (_escapeJumpActionStateMachine.IsRunning)
            _escapeJumpActionStateMachine.Cancel();

        _escapeJumpActionStateMachine.Start(_escapeJumpDuration, 1f);
        _pit.PlayStruggleAudio(_player.transform.position);
        _pitPresentation?.Struggle();
        _escapeProgress++;
        UpdateEscapeIndicator();

        if (_escapeProgress < _pit.RequiredJumpPresses)
            return;

        // 이동이 끝날 때까지 갇힘 State의 일반 입력 잠금을 유지한다.
        // 완료 후 State를 벗어나면서 균열 데칼 복구와 탈출 먼지가 함께 발생한다.
        _isEscaping = true;
        _escapeInput.Disable();
        _releaseTween = _pit.Release(_rigidbody, () =>
        {
            _releaseTween = null;
            if (_isActive && _player.StateMachine.CurrentState == this)
                _player.StateMachine.ForceChangeState<PlayerIdleState>();
        });
    }

    private void CreateEscapeIndicator()
    {
        // 상태 진입 중에만 월드 UI를 생성하여 Player 프리팹이 개별 기믹 표시를 상시 소유하지 않게 한다.
        DestroyEscapeIndicator();

        if (_escapeIndicatorPrefab == null)
            return;

        _escapeIndicator = Instantiate(_escapeIndicatorPrefab, _player.transform, false);
        _escapeIndicator.transform.localPosition = _escapeIndicatorLocalPosition;
    }

    private void UpdateEscapeIndicator()
    {
        if (_escapeIndicator == null || _pit == null)
            return;

        int remainingPresses = Mathf.Max(0, _pit.RequiredJumpPresses - _escapeProgress);
        _escapeIndicator.Show(remainingPresses);
    }

    private void DestroyEscapeIndicator()
    {
        if (_escapeIndicator == null)
            return;

        // 사망이나 피격처럼 다른 State가 탈출 상태를 빼앗아도 Exit에서 표시가 함께 정리된다.
        Destroy(_escapeIndicator.gameObject);
        _escapeIndicator = null;
    }

    private void OnValidate()
    {
        if (_escapeIndicatorPrefab == null)
            EditorLog.LogError("PlayerTrappedState에 탈출 횟수 표시 프리팹이 설정되지 않았습니다.", this);
    }
}
