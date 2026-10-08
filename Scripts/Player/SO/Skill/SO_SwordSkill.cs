using UnityEngine;

public abstract class SO_SwordSkill : SO_Skill
{
    // 모든 검 스킬은 같은 진입점에서 실행 당시의 검 크기를 받는다.
    // 하위 스킬은 이 값으로 자신의 공격 범위나 명중 반경을 계산한다.
    public sealed override void ExecuteSkill(in SkillExecutionContext context)
    {
        ExecuteSwordSkill(context, GetSwordSize(context.Transform));
    }

    protected abstract void ExecuteSwordSkill(in SkillExecutionContext context, float swordSize);

    protected static float GetSwordSize(Transform caster)
    {
        ISwordSizeProvider sword = caster != null ? caster.GetComponent<ISwordSizeProvider>() : null;
        return sword != null ? Mathf.Max(0.01f, sword.SwordSize) : 1f;
    }
}
