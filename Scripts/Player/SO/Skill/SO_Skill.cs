using UnityEngine;

public abstract class SO_Skill : ScriptableObject
{
    [field: SerializeField]
    public float EnergyCost { get; private set; }

    [field: SerializeField]
    public Sprite SkillIcon { get; private set; }

    [field: SerializeField]
    public string SkillName { get; private set; }

    [field: SerializeField, TextArea]
    public string SkillDescription { get; private set; }

    [field: SerializeField]
    public AnimationClip SkillClip { get; private set; }

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("애니메이션의 어느 시점(0~1)에 스킬 로직을 발동할지 결정합니다.")]
    public float ExecuteTime { get; private set; } = 0.5f;

    /// <summary>
    /// 클라이언트의 요청으로 서버에서 특정 이펙트를 스폰해야 할 때 호출됩니다.
    /// </summary>
    /// <param name="effectId">스킬 이팩트의 번호</param>
    /// <param name="position">스킬 이팩트가 생성될 위치</param>
    /// <param name="rotation">스킬 이팩트가 생성될 초기 각도</param>
    public virtual void ServerSpawnEffect(int effectId, Vector3 position, Quaternion rotation) { }

    /// <summary>
    /// 발동할 스킬의 구현
    /// </summary>
    public abstract void ExecuteSkill(PlayerSkill caster, IEnergyTracker energyTracker);

    /// <summary>
    /// 스킬 발동 중 제한될 조작들 (비트마스크)
    /// </summary>
    [field: SerializeField]
    public PlayerInputType ConstrainedInputs { get; private set; } = PlayerInputType.All;

    private void OnValidate()
    {
        if (!ConstrainedInputs.HasFlag(PlayerInputType.UsingSkill))
        {
            EditorLog.LogError("스킬은 반드시 UsingSkill을 제한해야 합니다!!!", this);
        }
    }
}
