using UnityEngine;

public class SkillSetTooltipIndicator : TooltipIndicator
{
    [SerializeField, RequireInterface(typeof(ISkillGetter))]
    private Object _skillGetter;

    [SerializeField]
    private SkillSet _skillSet;

    private ISkillGetter SkillGetter => _skillGetter as ISkillGetter;

    protected override string Title => SkillGetter.GetSkill(_skillSet).SkillName;

    protected override string Content => SkillGetter.GetSkill(_skillSet).SkillDescription;
}
