using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerStateMachine : IDisposable
{
    private readonly Dictionary<Type, PlayerState> _states = new();
    private PlayerStateContext _context;

    public PlayerState CurrentState { get; private set; }
    public bool IsInitialized => _context != null;

    public event Action<PlayerState, PlayerState> StateChanged;

    public void Initialize(PlayerStateContext context, PlayerStateCatalog catalog)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));
        if (IsInitialized)
            throw new InvalidOperationException("The player state machine is already initialized.");

        _context = context;
        catalog.ValidateOrThrow();

        try
        {
            // SO 에셋 원본에는 Inspector 설정만 보관하고, 런타임 데이터는 플레이어별 복제본에 기록한다.
            foreach (PlayerState prototype in catalog.StatePrototypes)
            {
                PlayerState runtimeState = UnityEngine.Object.Instantiate(prototype);
                runtimeState.name = $"{prototype.name} ({context.Player.name} Runtime)";
                runtimeState.hideFlags = HideFlags.DontSave;
                runtimeState.Initialize(_context);
                _states.Add(runtimeState.GetType(), runtimeState);
            }

            CurrentState = GetState<PlayerIdleState>();
            EnterState(CurrentState);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool TryChangeState<TState>()
        where TState : PlayerState
    {
        return TryChangeState<TState>(null);
    }

    // 구덩이처럼 진입 전에 런타임 데이터를 받아야 하는 구체 State를 타입 안전하게 설정한다.
    // 전환 가능 여부를 먼저 검사하여 실패한 전환이 캐싱된 State에 데이터를 남기지 않게 한다.
    public bool TryChangeState<TState>(Action<TState> configure)
        where TState : PlayerState
    {
        EnsureInitialized();

        TState nextState = GetState<TState>();
        if (ReferenceEquals(CurrentState, nextState))
            return false;
        if (!CurrentState.CanExitTo(nextState) || !nextState.CanEnterFrom(CurrentState))
            return false;
        if (
            CurrentState.LocksLowerPriorityTransitions
            && CurrentState.Priority > nextState.Priority
        )
            return false;

        configure?.Invoke(nextState);

        PlayerState previousState = CurrentState;
        previousState.PrepareExitTo(_context, nextState);
        previousState.ExitPresentationPolicy(_context);
        previousState.Exit(_context);
        CurrentState = nextState;
        EnterState(CurrentState);
        StateChanged?.Invoke(previousState, CurrentState);
        return true;
    }

    internal void FixedTick(float fixedDeltaTime)
    {
        if (IsInitialized)
            CurrentState.FixedTick(_context, fixedDeltaTime);
    }

    // 물리와 무관한 상태 수명은 프레임 시간으로 진행하여 낮은 Fixed Timestep에서도 표현 종료 시각이 흔들리지 않게 한다.
    internal void Tick(float deltaTime)
    {
        if (IsInitialized)
            CurrentState.Tick(_context, deltaTime);
    }

    internal void ForceChangeState<TState>()
        where TState : PlayerState
    {
        EnsureInitialized();

        PlayerState previousState = CurrentState;
        PlayerState nextState = GetState<TState>();
        previousState.PrepareExitTo(_context, nextState);
        previousState.ExitPresentationPolicy(_context);
        previousState.Exit(_context);
        CurrentState = nextState;
        EnterState(CurrentState);
        StateChanged?.Invoke(previousState, CurrentState);
    }
    public bool IsInState<TState>()
        where TState : PlayerState
    {
        return CurrentState is TState;
    }

    public void Dispose()
    {
        foreach (PlayerState state in _states.Values)
        {
            if (state != null)
                UnityEngine.Object.Destroy(state);
        }

        _states.Clear();
        CurrentState = null;
        _context = null;
        StateChanged = null;
    }

    private TState GetState<TState>()
        where TState : PlayerState
    {
        Type stateType = typeof(TState);
        if (_states.TryGetValue(stateType, out PlayerState state))
            return (TState)state;

        throw new InvalidOperationException(
            $"{stateType.Name} State 원본이 PlayerStateCatalog에 등록되지 않았습니다."
        );
    }

    // 초기화, 일반 전환, 강제 전환이 모두 같은 입력·표현 정책 적용 순서를 사용하도록 진입 절차를 통합한다.
    private void EnterState(PlayerState state)
    {
        state.ApplyInputPolicy(_context);
        state.ApplyPresentationPolicy(_context);
        state.Enter(_context);
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("The player state machine is not initialized.");
    }
}
