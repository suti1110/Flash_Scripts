public interface IPlayerSkillStatus
{
    float SilencePower { get; }
    float SilenceImmunity { get; }
    bool IsSilenced { get; }
    bool IsSilenceImmune { get; }
}
