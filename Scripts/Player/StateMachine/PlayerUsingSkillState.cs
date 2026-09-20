using UnityEngine;

[CreateAssetMenu(fileName = "PlayerUsingSkillState", menuName = "Flash/Player/States/Using Skill")]
public sealed class PlayerUsingSkillState : PlayerState
{
    private ISkill _skill;
    private PlayerCamera _camera;
    private PlayerSkillMotion _skillMotion;
    private Rigidbody _rigidbody;
    private Quaternion _lockedRotation = Quaternion.identity;

    // 스킬마다 설정된 차단 입력은 State 에셋의 정적 허용 목록보다 우선하는 런타임 정책이다.
    protected override PlayerInputType ResolveAllowedInputs(PlayerStateContext context) =>
        PlayerInputType.All & ~_skill.GetCurrentSkillConstraints();

    internal override void Initialize(PlayerStateContext context)
    {
        _skill = context.Player.GetComponent<ISkill>();
        _camera = context.Player.GetComponent<PlayerCamera>();
        _skillMotion = context.Player.GetComponent<PlayerSkillMotion>();
        _rigidbody = context.Player.GetComponent<Rigidbody>();
    }

    internal override void Enter(PlayerStateContext context)
    {
        _lockedRotation = _rigidbody != null
            ? _rigidbody.rotation
            : context.Player.transform.rotation;
    }

    internal override void FixedTick(PlayerStateContext context, float fixedDeltaTime)
    {
        if (_skill == null || _rigidbody == null)
            return;

        SkillCameraInfluenceAxes influenceAxes = _skill.GetCurrentCameraInfluenceAxes();
        if (influenceAxes != SkillCameraInfluenceAxes.None && _camera?.CameraPivot != null)
        {
            _lockedRotation = GetInfluencedRotation(
                _lockedRotation,
                _camera.CameraPivot.rotation,
                influenceAxes
            );
            _skillMotion?.UpdateOriginRotation(_lockedRotation);
        }

        _rigidbody.MoveRotation(_lockedRotation);
    }

    private static Quaternion GetInfluencedRotation(
        Quaternion lockedRotation,
        Quaternion cameraRotation,
        SkillCameraInfluenceAxes influenceAxes
    )
    {
        Vector3 lockedEulerAngles = lockedRotation.eulerAngles;
        Vector3 cameraEulerAngles = cameraRotation.eulerAngles;
        return Quaternion.Euler(
            (influenceAxes & SkillCameraInfluenceAxes.X) != 0
                ? cameraEulerAngles.x
                : lockedEulerAngles.x,
            (influenceAxes & SkillCameraInfluenceAxes.Y) != 0
                ? cameraEulerAngles.y
                : lockedEulerAngles.y,
            (influenceAxes & SkillCameraInfluenceAxes.Z) != 0
                ? cameraEulerAngles.z
                : lockedEulerAngles.z
        );
    }
}
// PlayerUsingSkillState은 플레이어 상태의 진입·종료 조건과 해당 상태에서의 입력 및 표현 규칙을 정의한다.
// 상태 전환 책임을 상태 머신에 모아 서로 다른 행동 로직이 직접 상태를 덮어쓰지 않도록 한다.
