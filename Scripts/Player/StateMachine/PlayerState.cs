using UnityEngine;

// 프로젝트에 저장된 State 에셋은 설정 원본으로만 사용한다.
// 실제 플레이어는 StateMachine이 복제한 런타임 인스턴스를 사용하여 플레이어별 상태가 서로 섞이지 않는다.
public abstract class PlayerState : ScriptableObject
{
    [Header("전환 정책")]
    [SerializeField]
    private int _priority;

    [SerializeField]
    private bool _locksLowerPriorityTransitions;

    [Header("표현 정책")]
    [SerializeField]
    private bool _usesDetachedCameraRotation;

    [SerializeField]
    private PlayerAnimationLayerMask _usingLayers = PlayerAnimationLayerMask.Base;

    [SerializeField]
    [NaughtyAttributes.ShowIf(nameof(UsesBaseLayer))]
    private PlayerBaseAnimationFlags _baseAnimationFlags;

    [SerializeField]
    [NaughtyAttributes.ShowIf(nameof(UsesActionLayer))]
    private PlayerActionAnimationFlags _actionAnimationFlags;

    [Header("입력 정책")]
    [SerializeField]
    [Tooltip(
        "이 State에서 허용할 입력입니다. 목록에 없는 현재 및 미래 입력은 자동으로 차단됩니다."
    )]
    private PlayerInputType _allowedInputs = PlayerInputType.All;

    public virtual int Priority => _priority;
    public virtual bool LocksLowerPriorityTransitions => _locksLowerPriorityTransitions;
    public virtual bool UsesDetachedCameraRotation => _usesDetachedCameraRotation;
    public virtual PlayerAnimationLayerMask UsingLayers => _usingLayers;
    public virtual PlayerBaseAnimationFlags BaseAnimationFlags => _baseAnimationFlags;
    public virtual PlayerActionAnimationFlags ActionAnimationFlags => _actionAnimationFlags;

    private bool UsesBaseLayer => (_usingLayers & PlayerAnimationLayerMask.Base) != 0;
    private bool UsesActionLayer => (_usingLayers & PlayerAnimationLayerMask.Action) != 0;

    public virtual bool CanEnterFrom(PlayerState previousState) => true;

    public virtual bool CanExitTo(PlayerState nextState) => true;

    internal virtual void Initialize(PlayerStateContext context) { }

    // 모든 State 진입 경로에서 상태머신이 호출하므로 파생 State가 애니메이션 표현 정책 적용을 누락할 수 없다.
    internal virtual void ApplyPresentationPolicy(PlayerStateContext context)
    {
        PlayerAnimation animation = context.Player.GetComponent<PlayerAnimation>();
        if (animation == null)
            return;

        if (UsesBaseLayer)
            animation.ApplyBaseLayerState(ResolveBaseAnimationFlags(context));

        if (UsesActionLayer)
            animation.ApplyActionLayerState(ResolveActionAnimationFlags(context));
    }

    // Action Layer를 사용했던 State는 종료 시 자동으로 레이어 가중치 및 플래그를 정리한다.
    internal virtual void ExitPresentationPolicy(PlayerStateContext context)
    {
        PlayerAnimation animation = context.Player.GetComponent<PlayerAnimation>();
        if (animation == null)
            return;

        if (UsesActionLayer)
            animation.ApplyActionLayerState(default);
    }

    protected virtual PlayerBaseAnimationFlags ResolveBaseAnimationFlags(PlayerStateContext context)
    {
        return _baseAnimationFlags;
    }

    protected virtual PlayerActionAnimationFlags ResolveActionAnimationFlags(
        PlayerStateContext context
    )
    {
        return _actionAnimationFlags;
    }

    // 모든 State 진입 경로에서 상태머신이 호출하므로 파생 State가 입력 제약 적용을 누락할 수 없다.
    internal void ApplyInputPolicy(PlayerStateContext context)
    {
        PlayerInputType constrainedInputs = PlayerInputType.All & ~ResolveAllowedInputs(context);
        context.Player.ApplyInputConstraints(constrainedInputs);
    }

    // 스킬처럼 진입 시점의 런타임 데이터로 입력 정책을 계산해야 하는 State만 재정의한다.
    protected virtual PlayerInputType ResolveAllowedInputs(PlayerStateContext context)
    {
        return _allowedInputs;
    }

    internal virtual void Enter(PlayerStateContext context) { }

    // 애니메이션처럼 프레임 시간에 따라 완료되는 State가 자기 수명을 직접 관리할 수 있도록 현재 State에만 전달한다.
    internal virtual void Tick(PlayerStateContext context, float deltaTime) { }

    // 현재 State가 다음 State의 종류에 따라 종료 전 처리를 해야 할 때 사용하는 전환 훅이다.
    // Exit에 다음 State 정보를 넘기지 않아도 되므로 기존 종료 수명주기와 전환별 정책을 분리할 수 있다.
    internal virtual void PrepareExitTo(PlayerStateContext context, PlayerState nextState) { }

    internal virtual void Exit(PlayerStateContext context) { }

    // MonoBehaviour가 아닌 State도 물리 주기에 참여할 수 있도록 현재 State에만 전달되는 훅이다.
    internal virtual void FixedTick(PlayerStateContext context, float fixedDeltaTime) { }
}

public sealed class PlayerStateContext
{
    public PlayerStateContext(Player player)
    {
        Player = player;
    }

    public Player Player { get; }
}
