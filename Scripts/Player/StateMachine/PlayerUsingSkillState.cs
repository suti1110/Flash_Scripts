using UnityEngine;

[CreateAssetMenu(fileName = "PlayerUsingSkillState", menuName = "Flash/Player/States/Using Skill")]
public sealed class PlayerUsingSkillState : PlayerState
{
    private ISkill _skill;

    // 스킬마다 설정된 차단 입력은 State 에셋의 정적 허용 목록보다 우선하는 런타임 정책이다.
    protected override PlayerInputType ResolveAllowedInputs(PlayerStateContext context) =>
        PlayerInputType.All & ~_skill.GetCurrentSkillConstraints();

    public override bool UsesDetachedCameraRotation =>
        _skill != null && _skill.CurrentSkill != null && _skill.CurrentSkill.DetachCameraRotationDuringSkill;

    internal override void Initialize(PlayerStateContext context)
    {
        _skill = context.Player.GetComponent<ISkill>();
    }
}
// PlayerUsingSkillState은 플레이어 상태의 진입·종료 조건과 해당 상태에서의 입력 및 표현 규칙을 정의한다.
// 상태 전환 책임을 상태 머신에 모아 서로 다른 행동 로직이 직접 상태를 덮어쓰지 않도록 한다.
