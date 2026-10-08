using System;
using Unity.Netcode;
using UnityEngine;

public interface ITimedSkill
{
    float Duration { get; }
    bool CanReactivateWhileActive { get; }
    void StartTimedSkill(in TimedSkillContext context);
    void UpdateTimedSkill(in TimedSkillContext context);
    void EndTimedSkill(in TimedSkillContext context);
}

// 스킬 SO는 공유되므로 시전자별 런타임 데이터는 활성 항목에 저장한다.
public sealed class TimedSkillInfo
{
    public SO_Skill Skill { get; }
    public ITimedSkill TimedSkill { get; }
    public double EndTime { get; internal set; }
    public object RuntimeState { get; internal set; }
    public bool HasEnded { get; internal set; }

    public TimedSkillInfo(SO_Skill skill, double endTime)
    {
        Skill = skill;
        TimedSkill = skill as ITimedSkill
            ?? throw new ArgumentException("지속 스킬만 등록할 수 있습니다.", nameof(skill));
        EndTime = endTime;
    }

    public double RemainingTime(double now) => Math.Max(0d, EndTime - now);
}

public readonly struct TimedSkillContext
{
    private readonly TimedSkillInfo _info;
    private readonly Func<double> _clock;

    public Transform Caster { get; }
    public float DeltaTime { get; }
    // 영역 판정이 시전자의 Update보다 먼저 실행돼도 만료된 효과를 조회하지 않는다.
    public double RemainingTime => _info.HasEnded ? 0d : _info.RemainingTime(_clock());
    public bool IsActive => RemainingTime > 0d;
    public object RuntimeState
    {
        get => _info.RuntimeState;
        set => _info.RuntimeState = value;
    }

    public TimedSkillContext(
        Transform caster, TimedSkillInfo info, float deltaTime, Func<double> clock
    )
    {
        Caster = caster;
        _info = info;
        DeltaTime = deltaTime;
        _clock = clock;
    }
}

// ScriptableObject 참조는 네트워크로 직렬화하지 않고 카탈로그의 고정 ID로 복원한다.
public struct NetworkTimedSkillInfo : INetworkSerializable, IEquatable<NetworkTimedSkillInfo>
{
    public int SkillNetworkId { get; private set; }
    public double EndTime { get; private set; }

    public NetworkTimedSkillInfo(int skillNetworkId, double endTime)
    {
        SkillNetworkId = skillNetworkId;
        EndTime = endTime;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        int skillNetworkId = SkillNetworkId;
        double endTime = EndTime;
        serializer.SerializeValue(ref skillNetworkId);
        serializer.SerializeValue(ref endTime);
        if (serializer.IsReader)
        {
            SkillNetworkId = skillNetworkId;
            EndTime = endTime;
        }
    }

    public bool Equals(NetworkTimedSkillInfo other) =>
        SkillNetworkId == other.SkillNetworkId && EndTime.Equals(other.EndTime);
}
