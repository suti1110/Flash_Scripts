using UnityEngine;

[CreateAssetMenu(
    fileName = "PlayerJumpingAscendingState",
    menuName = "Flash/Player/Jumping States/Ascending"
)]
public sealed class PlayerJumpingAscendingState : PlayerJumpingState
{
    private PlayerJump _playerJump;
    private PlayerAnimation _animation;

    internal override void Initialize(PlayerJumpingStateContext context)
    {
        _playerJump = context.Player.GetComponent<PlayerJump>();
        _animation = context.Player.GetComponent<PlayerAnimation>();
    }

    internal override void Enter(PlayerJumpingStateContext context)
    {
        context.Player.SetJumpInputEnabled(false);
        _playerJump.ApplyGravity(true);
        _animation.ApplyJumpingState(false, true);
    }
}
// PlayerJumpingAscendingState은 플레이어 상태의 진입·종료 조건과 해당 상태에서의 입력 및 표현 규칙을 정의한다.
// 상태 전환 책임을 상태 머신에 모아 서로 다른 행동 로직이 직접 상태를 덮어쓰지 않도록 한다.
