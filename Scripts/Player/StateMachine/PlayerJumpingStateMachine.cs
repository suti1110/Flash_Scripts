using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerJumpingStateMachine : IDisposable
{
    private readonly Dictionary<Type, PlayerJumpingState> _states = new();
    private PlayerJumpingStateContext _context;

    public PlayerJumpingState CurrentState { get; private set; }
    public bool IsInitialized => _context != null;

    public event Action<PlayerJumpingState, PlayerJumpingState> StateChanged;

    public void Initialize(PlayerJumpingStateContext context, PlayerJumpingStateCatalog catalog)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));
        if (IsInitialized)
            throw new InvalidOperationException(
                "The player jumping state machine is already initialized."
            );

        _context = context;
        catalog.ValidateOrThrow();

        try
        {
            // Catalog 원본은 공유하되 런타임 참조와 진행 상태는 플레이어별 복제본에만 기록한다.
            foreach (PlayerJumpingState prototype in catalog.StatePrototypes)
            {
                PlayerJumpingState runtimeState = UnityEngine.Object.Instantiate(prototype);
                runtimeState.name = $"{prototype.name} ({context.Player.name} Runtime)";
                runtimeState.hideFlags = HideFlags.DontSave;
                runtimeState.Initialize(_context);
                _states.Add(runtimeState.GetType(), runtimeState);
            }

            CurrentState = GetState<PlayerJumpingIdleState>();
            EnterState(CurrentState);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool TryChangeState<TState>()
        where TState : PlayerJumpingState
    {
        EnsureInitialized();

        PlayerJumpingState nextState = GetState<TState>();
        if (ReferenceEquals(CurrentState, nextState))
            return false;
        if (!CurrentState.CanExitTo(nextState) || !nextState.CanEnterFrom(CurrentState))
            return false;
        if (
            CurrentState.LocksLowerPriorityTransitions
            && CurrentState.Priority > nextState.Priority
        )
            return false;

        PlayerJumpingState previousState = CurrentState;
        previousState.Exit(_context);
        CurrentState = nextState;
        EnterState(CurrentState);
        StateChanged?.Invoke(previousState, CurrentState);
        return true;
    }

    internal void ForceChangeState<TState>()
        where TState : PlayerJumpingState
    {
        EnsureInitialized();

        PlayerJumpingState previousState = CurrentState;
        PlayerJumpingState nextState = GetState<TState>();
        previousState.Exit(_context);
        CurrentState = nextState;
        EnterState(CurrentState);
        StateChanged?.Invoke(previousState, CurrentState);
    }
    public bool IsInState<TState>()
        where TState : PlayerJumpingState
    {
        return CurrentState is TState;
    }

    public void Dispose()
    {
        foreach (PlayerJumpingState state in _states.Values)
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
        where TState : PlayerJumpingState
    {
        Type stateType = typeof(TState);
        if (_states.TryGetValue(stateType, out PlayerJumpingState state))
            return (TState)state;

        throw new InvalidOperationException(
            $"{stateType.Name} State 원본이 PlayerJumpingStateCatalog에 등록되지 않았습니다."
        );
    }

    private void EnterState(PlayerJumpingState state)
    {
        state.Enter(_context);
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException(
                "The player jumping state machine is not initialized."
            );
    }
}
// PlayerJumpingStateMachine은 플레이어 상태의 진입·종료 조건과 해당 상태에서의 입력 및 표현 규칙을 정의한다.
// 상태 전환 책임을 상태 머신에 모아 서로 다른 행동 로직이 직접 상태를 덮어쓰지 않도록 한다.
