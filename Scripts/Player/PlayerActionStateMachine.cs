using System;

public enum PlayerActionPhase
{
    Idle,
    Windup,
    Recovery,
    Completed,
    Cancelled,
}

public sealed class PlayerActionStateMachine
{
    public event Action Started;
    public event Action ExecutionPointReached;
    public event Action Completed;
    public event Action Cancelled;

    public PlayerActionPhase Phase { get; private set; } = PlayerActionPhase.Idle;
    public float ElapsedTime { get; private set; }
    public float Duration { get; private set; }
    public bool IsRunning =>
        Phase == PlayerActionPhase.Windup || Phase == PlayerActionPhase.Recovery;

    private float _executionTime;
    private bool _hasExecuted;

    public void Start(float duration, float normalizedExecutionTime)
    {
        if (IsRunning)
            throw new InvalidOperationException("A player action is already running.");

        Duration = Math.Max(0f, duration);
        ElapsedTime = 0f;
        _executionTime = Duration * Math.Clamp(normalizedExecutionTime, 0f, 1f);
        _hasExecuted = false;
        Phase = PlayerActionPhase.Windup;

        Started?.Invoke();

        if (IsRunning)
            Advance(0f);
    }

    public void Tick(float deltaTime)
    {
        if (!IsRunning)
            return;

        Advance(Math.Max(0f, deltaTime));
    }

    public void Cancel()
    {
        if (!IsRunning)
            return;

        Phase = PlayerActionPhase.Cancelled;
        Cancelled?.Invoke();
    }

    private void Advance(float deltaTime)
    {
        ElapsedTime = Math.Min(Duration, ElapsedTime + deltaTime);

        if (!_hasExecuted && ElapsedTime >= _executionTime)
        {
            _hasExecuted = true;
            Phase = PlayerActionPhase.Recovery;
            ExecutionPointReached?.Invoke();
        }

        if (IsRunning && ElapsedTime >= Duration)
        {
            Phase = PlayerActionPhase.Completed;
            Completed?.Invoke();
        }
    }
}
// PlayerActionStateMachine은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
