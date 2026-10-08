using UnityEngine;

[CreateAssetMenu(fileName = "Gigantification", menuName = "Player/Skill/Gigantification")]
public sealed class SO_Gigantification : SO_SwordSkill, ITimedSkill
{
    private sealed class RuntimeState
    {
        public readonly ISwordSizeController Sword;

        public RuntimeState(ISwordSizeController sword) => Sword = sword;
    }

    [field: SerializeField, Min(0.01f), InspectorName("거대화 지속 시간")]
    public float Duration { get; private set; } = 10f;
    public bool CanReactivateWhileActive => false;

    [field: SerializeField, Min(1f), InspectorName("거대화 검 크기 배율")]
    public float SwordSizeMultiplier { get; private set; } = 3f;

    public void StartTimedSkill(in TimedSkillContext context)
    {
        var state = new RuntimeState(context.Caster.GetComponent<ISwordSizeController>());
        context.RuntimeState = state;
        state.Sword?.SetSwordSizeMultiplier(state, SwordSizeMultiplier);
    }

    public void UpdateTimedSkill(in TimedSkillContext context)
    {
        if (context.RuntimeState is RuntimeState state)
            state.Sword?.SetSwordSizeMultiplier(state, SwordSizeMultiplier);
    }

    public void EndTimedSkill(in TimedSkillContext context)
    {
        if (context.RuntimeState is RuntimeState state)
            state.Sword?.RemoveSwordSizeMultiplier(state);
    }

    public override bool CanExecute(in SkillExecutionContext context, out string failureMessage)
    {
        if (
            context.Transform == null
            || context.Transform.GetComponent<ISwordSizeController>() == null
        )
        {
            failureMessage = "Gigantification requires a sword.";
            return false;
        }
        if (context.TimedSkills == null)
        {
            failureMessage = "Gigantification requires PlayerSkill.";
            return false;
        }

        if (!context.TimedSkills.IsTimedSkillActive(this))
        {
            failureMessage = null;
            return true;
        }

        failureMessage = "Gigantification is already active.";
        return false;
    }

    protected override void ExecuteSwordSkill(in SkillExecutionContext context, float swordSize)
    {
        context.TimedSkills?.ActivateTimedSkill(this);
    }

    protected override void OnValidate()
    {
        base.OnValidate();

        if (SwordSizeMultiplier <= 1f)
            EditorLog.LogError("거대화 배율은 1보다 커야 합니다.", this);
        if (!float.IsFinite(Duration) || Duration <= 0f)
            EditorLog.LogError("거대화 지속 시간은 0보다 커야 합니다.", this);
    }
}
