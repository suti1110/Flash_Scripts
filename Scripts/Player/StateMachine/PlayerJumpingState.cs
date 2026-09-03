using UnityEngine;

// 점프 State 에셋은 설정 원본으로만 사용하며, 플레이어마다 복제된 인스턴스가 런타임 참조를 소유한다.
public abstract class PlayerJumpingState : ScriptableObject
{
    [Header("전환 정책")]
    [SerializeField]
    private int _priority;

    [SerializeField]
    private bool _locksLowerPriorityTransitions;

    [Header("입력 정책")]
    [SerializeField]
    [Tooltip("이 점프 State에서 일반 점프 입력을 받을 수 있는지 결정합니다.")]
    private bool _allowsJumpInput;

    public virtual int Priority => _priority;
    public virtual bool LocksLowerPriorityTransitions => _locksLowerPriorityTransitions;
    public virtual bool AllowsJumpInput => _allowsJumpInput;

    public virtual bool CanEnterFrom(PlayerJumpingState previousState) => true;

    public virtual bool CanExitTo(PlayerJumpingState nextState) => true;

    internal virtual void Initialize(PlayerJumpingStateContext context) { }

    internal virtual void Enter(PlayerJumpingStateContext context) { }

    internal virtual void Exit(PlayerJumpingStateContext context) { }
}

public sealed class PlayerJumpingStateContext
{
    public PlayerJumpingStateContext(Player player)
    {
        Player = player;
    }

    public Player Player { get; }
}
// PlayerJumpingState은 플레이어 상태의 진입·종료 조건과 해당 상태에서의 입력 및 표현 규칙을 정의한다.
// 상태 전환 책임을 상태 머신에 모아 서로 다른 행동 로직이 직접 상태를 덮어쓰지 않도록 한다.
