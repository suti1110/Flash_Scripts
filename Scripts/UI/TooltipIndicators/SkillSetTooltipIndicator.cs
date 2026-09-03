using UnityEngine;
using UnityEngine.Serialization;

public class SkillSetTooltipIndicator : TooltipIndicator
{
    [SerializeField, RequireInterface(typeof(ISkillGetter))]
    private Object _skillGetter;

    [SerializeField]
    [FormerlySerializedAs("_skillSet")]
    private int _skillSlotIndex;

    private ISkillGetter SkillGetter => _skillGetter as ISkillGetter;

    protected override string Title => SkillGetter.GetSkill(_skillSlotIndex).SkillName;

    protected override string Content => SkillGetter.GetSkill(_skillSlotIndex).SkillDescription;
}
// SkillSetTooltipIndicator은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
